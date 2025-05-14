using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using System.Buffers;
using System.Collections.Concurrent;
using Xabe.FFmpeg;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class AudioProcessor : IAudioProcessor
    {
        private readonly ILogger<AudioProcessor> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IFileRepository FileRepository;
        private readonly IFFmpegService FFmpegService;
        private readonly ArrayPool<FileEntity> poolFileEntity = ArrayPool<FileEntity>.Shared;
        public AudioProcessor(
            ILogger<AudioProcessor> logger,
            ServerConfig serverConfig,
            IFileRepository fileRepository,
            IFFmpegService mpegService)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            FileRepository = fileRepository;
            FFmpegService = mpegService;
        }

        public Task InitializeAsync()
        {
            return FFmpegService.EnsureFFmpegDownloaded();
        }
        public async Task FillEmptyInfoAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            FileEntity[] bufferArray = poolFileEntity.Rent(fileEntities.Count());
            try
            {
                int count = 0;

                Partitioner.Create(fileEntities)
                    .AsParallel()
                    .Where(static (fe) =>
                           fe != null
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                        && (!fe.IsMetadataChecked || !fe.IsThumbnailChecked))
                    .ForAll(fe =>
                    {
                        int index = Interlocked.Increment(ref count) - 1;
                        if (index < bufferArray.Length)
                        {
                            bufferArray[index] = fe;
                        }
                    });

                var bufferMemory = bufferArray.AsMemory(0, count);
                await RefreshInfoAsync(bufferMemory, setCheckedForFailed);
            }
            finally
            {
                poolFileEntity.Return(bufferArray, clearArray: true);
            }
        }
        private async Task RefreshInfoAsync(ReadOnlyMemory<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            try
            {
                if (fileEntities.Length == 0)
                {
                    return;
                }

                await FFmpegService.EnsureFFmpegDownloaded();

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalAudio)
                    {
                        await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                    }

                    file.IsThumbnailChecked = true;
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    await Parallel.ForAsync(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        async (index, cancellationToken) =>
                        {
                            var file = fileEntities.Span[index];

                            if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalAudio)
                            {
                                await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                            }

                            file.IsThumbnailChecked = true;
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        public async Task FillEmptyMetadataAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            FileEntity[] bufferArray = poolFileEntity.Rent(fileEntities.Count());
            try
            {
                int count = 0;

                Partitioner.Create(fileEntities)
                    .AsParallel()
                    .Where(static (fe) =>
                           fe != null
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                        && !fe.IsMetadataChecked)
                    .ForAll(fe =>
                    {
                        int index = Interlocked.Increment(ref count) - 1;
                        if (index < bufferArray.Length)
                        {
                            bufferArray[index] = fe;
                        }
                    });

                var bufferMemory = bufferArray.AsMemory(0, count);
                await RefreshMetadataAsync(bufferMemory, setCheckedForFailed);
            }
            finally
            {
                poolFileEntity.Return(bufferArray, clearArray: true);
            }
        }
        private async Task RefreshMetadataAsync(ReadOnlyMemory<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            try
            {
                if (!_serverConfig.GenerateMetadataForLocalAudio
                    || fileEntities.Length == 0)
                {
                    return;
                }

                await FFmpegService.EnsureFFmpegDownloaded();

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    await Parallel.ForAsync(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        async (index, cancellationToken) =>
                        {
                            var file = fileEntities.Span[index];

                            await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private async Task RefreshSingleFileMetadataAsync(FileEntity file, bool setCheckedForFailed)
        {
            var audioMetadata = await GetFileMetadataAsync(file);
            file.AudioMetadata = audioMetadata;
            if (audioMetadata != null ||
               (setCheckedForFailed && audioMetadata == null))
            {
                file.IsMetadataChecked = true;

                InformationSetMetadata(file.FilePhysicalFullPath);
            }
        }
        private async Task<MediaAudioEntity?> GetFileMetadataAsync(FileEntity fileEntity)
        {
            if (fileEntity == null)
            {
                return null;
            }

            FileInfo fileInfo = new(fileEntity.FilePhysicalFullPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return null;
            }

            try
            {
                using (CancellationTokenSource cancellationTokenSource = new(TimeSpanValues.TimeMin5))
                {
                    if (await FFmpegService.TryGetMediaInfo(fileEntity.FilePhysicalFullPath, cancellationTokenSource.Token) is IMediaInfo mediaInfo)
                    {
                        fileEntity.FileSizeInBytes = mediaInfo.Size;
                        return (ExtractAudioMetadataAsync(ref mediaInfo));
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
        private MediaAudioEntity? ExtractAudioMetadataAsync(ref IMediaInfo mediaInfo)
        {
            try
            {
                if (mediaInfo.AudioStreams.FirstOrDefault() is IAudioStream audioStream)
                {
                    return new()
                    {
                        FilePhysicalFullPath = mediaInfo.Path,
                        Duration = audioStream.Duration,
                        Codec = !string.IsNullOrWhiteSpace(audioStream.Codec)
                            ? string.Intern(audioStream.Codec)
                            : null,
                        Bitrate = audioStream.Bitrate,
                        Channels = audioStream.Channels,
                        Language = !string.IsNullOrWhiteSpace(audioStream.Language)
                            ? string.Intern(audioStream.Language)
                            : null,
                        SampleRate = audioStream.SampleRate
                    };
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
        public async Task FillEmptyThumbnailsAsync(IEnumerable<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            FileEntity[] bufferArray = poolFileEntity.Rent(fileEntities.Count());
            try
            {
                int count = 0;

                Partitioner.Create(fileEntities)
                    .AsParallel()
                    .Where(static (fe) =>
                           fe != null
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Audio
                        && !fe.IsThumbnailChecked)
                    .ForAll(fe =>
                    {
                        int index = Interlocked.Increment(ref count) - 1;
                        if (index < bufferArray.Length)
                        {
                            bufferArray[index] = fe;
                        }
                    });

                var bufferMemory = bufferArray.AsMemory(0, count);
                await RefreshThumbnailsAsync(bufferMemory, setCheckedForFailed);
            }
            finally
            {
                poolFileEntity.Return(bufferArray, clearArray: true);
            }
        }
        private async Task RefreshThumbnailsAsync(ReadOnlyMemory<FileEntity> fileEntities, bool setCheckedForFailed = true)
        {
            try
            {
                if (fileEntities.Length == 0)
                {
                    return;
                }

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    file.IsThumbnailChecked = true;
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    _ = Parallel.For(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        (index) =>
                        {
                            var file = fileEntities.Span[index];

                            file.IsThumbnailChecked = true;
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
