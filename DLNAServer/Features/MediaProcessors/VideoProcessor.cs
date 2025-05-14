using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Files;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using System.Buffers;
using System.Collections.Concurrent;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Exceptions;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class VideoProcessor : IVideoProcessor
    {
        private readonly ILogger<VideoProcessor> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IFileRepository FileRepository;
        private readonly IFileService FileService;
        private readonly IFFmpegService FFmpegService;
        private readonly ArrayPool<FileEntity> poolFileEntity = ArrayPool<FileEntity>.Shared;
        public VideoProcessor(
            ILogger<VideoProcessor> logger,
            ServerConfig serverConfig,
            IFileRepository fileRepository,
            IFileService fileService,
            IFFmpegService mpegService)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            FileRepository = fileRepository;
            FileService = fileService;
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Video
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
                    if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalMovies)
                    {
                        await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                    }

                    if (!file.IsThumbnailChecked && _serverConfig.GenerateThumbnailsForLocalMovies)
                    {
                        await RefreshSingleFileThumbnailAsync(file, setCheckedForFailed);
                    }
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

                            if (!file.IsMetadataChecked && _serverConfig.GenerateMetadataForLocalMovies)
                            {
                                await RefreshSingleFileMetadataAsync(file, setCheckedForFailed);
                            }

                            if (!file.IsThumbnailChecked && _serverConfig.GenerateThumbnailsForLocalMovies)
                            {
                                await RefreshSingleFileThumbnailAsync(file, setCheckedForFailed);
                            }
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Video
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
                if (!_serverConfig.GenerateMetadataForLocalMovies
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
            (var videoMetadata, var audioMetadata, var subtitleMetadata) = await GetVideoFileMetadataAsync(file);
            if (audioMetadata != null)
            {
                file.AudioMetadata = audioMetadata;
            }

            if (subtitleMetadata != null)
            {
                file.SubtitleMetadata = subtitleMetadata;
            }

            if (videoMetadata != null)
            {
                file.VideoMetadata = videoMetadata;
            }

            if (videoMetadata != null ||
               (setCheckedForFailed && videoMetadata == null))
            {
                file.IsMetadataChecked = true;

                InformationSetMetadata(file.FilePhysicalFullPath);
            }
        }

        private async Task<(MediaVideoEntity?, MediaAudioEntity?, MediaSubtitleEntity?)> GetVideoFileMetadataAsync(FileEntity fileEntity)
        {
            if (fileEntity == null)
            {
                return (null, null, null);
            }

            FileInfo fileInfo = new(fileEntity.FilePhysicalFullPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return (null, null, null);
            }

            try
            {
                using (CancellationTokenSource cancellationTokenSource = new(TimeSpanValues.TimeMin5))
                {
                    if (await FFmpegService.TryGetMediaInfo(fileEntity.FilePhysicalFullPath, cancellationTokenSource.Token) is IMediaInfo mediaInfo)
                    {
                        fileEntity.FileSizeInBytes = mediaInfo.Size;
                        return (ExtractVideoMetadataAsync(ref mediaInfo),
                                ExtractAudioMetadataAsync(ref mediaInfo),
                                ExtractSubtitleMetadataAsync(ref mediaInfo));
                    }
                    return (null, null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return (null, null, null);
            }
        }
        private MediaVideoEntity? ExtractVideoMetadataAsync(ref readonly IMediaInfo mediaInfo)
        {
            try
            {
                if (mediaInfo.VideoStreams.FirstOrDefault() is IVideoStream videoStream)
                {
                    return new()
                    {
                        FilePhysicalFullPath = mediaInfo.Path,
                        Duration = videoStream.Duration,
                        Codec = !string.IsNullOrWhiteSpace(videoStream.Codec)
                            ? string.Intern(videoStream.Codec)
                            : null,
                        Ratio = !string.IsNullOrWhiteSpace(videoStream.Ratio)
                            ? string.Intern(videoStream.Ratio)
                            : null,
                        Height = videoStream.Height,
                        Width = videoStream.Width,
                        Framerate = videoStream.Framerate,
                        Bitrate = videoStream.Bitrate,
                        PixelFormat = videoStream.PixelFormat,
                        Rotation = videoStream.Rotation,
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
        private MediaAudioEntity? ExtractAudioMetadataAsync(ref readonly IMediaInfo mediaInfo)
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
        private MediaSubtitleEntity? ExtractSubtitleMetadataAsync(ref readonly IMediaInfo mediaInfo)
        {
            try
            {
                if (mediaInfo.SubtitleStreams.FirstOrDefault() is ISubtitleStream subtitleStream)
                {
                    return new()
                    {
                        FilePhysicalFullPath = mediaInfo.Path,
                        Language = !string.IsNullOrWhiteSpace(subtitleStream.Language)
                            ? string.Intern(subtitleStream.Language)
                            : null,
                        Codec = !string.IsNullOrWhiteSpace(subtitleStream.Codec)
                            ? string.Intern(subtitleStream.Codec)
                            : null,
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Video
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
                if (!_serverConfig.GenerateThumbnailsForLocalMovies
                    || fileEntities.Length == 0)
                {
                    return;
                }

                await FFmpegService.EnsureFFmpegDownloaded();

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    await RefreshSingleFileThumbnailAsync(file, setCheckedForFailed);
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

                            await RefreshSingleFileThumbnailAsync(file, setCheckedForFailed);
                        });

                }
                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private async Task RefreshSingleFileThumbnailAsync(FileEntity file, bool setCheckedForFailed)
        {
            try
            {
                (var thumbnailFileFullPath, var thumbnailData, var dlnaMime, var dlnaProfileName) = await CreateThumbnailFromVideoAsync(
                    fileEntity: file,
                    dlnaMimeRequested: _serverConfig.DefaultDlnaMimeForVideoThumbnails);

                if (thumbnailFileFullPath != null
                    && new FileInfo(thumbnailFileFullPath) is FileInfo thumbnailFileInfo
                    && thumbnailFileInfo.Exists)
                {
                    file.IsThumbnailChecked = true;
                    file.Thumbnail = new()
                    {
                        FilePhysicalFullPath = file.FilePhysicalFullPath,
                        ThumbnailFileDlnaMime = dlnaMime!.Value,
                        ThumbnailFileDlnaProfileName = dlnaProfileName != null ? string.Intern(dlnaProfileName) : null,
                        ThumbnailFileExtension = string.Intern(thumbnailFileInfo.Extension),
                        ThumbnailFilePhysicalFullPath = thumbnailFileFullPath,
                        ThumbnailFileSizeInBytes = thumbnailFileInfo.Length,
                        ThumbnailData = _serverConfig.StoreThumbnailsForLocalMoviesInDatabase
                            ? new()
                            {
                                ThumbnailData = thumbnailData.AsArray(),
                                ThumbnailFilePhysicalFullPath = thumbnailFileFullPath,
                                FilePhysicalFullPath = file.FilePhysicalFullPath,
                            }
                            : null
                    };

                    InformationSetThumbnail(file.FilePhysicalFullPath);
                }
                else if (setCheckedForFailed)
                {
                    file.IsThumbnailChecked = true;

                    InformationSetThumbnail(file.FilePhysicalFullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        private async Task<(string? thumbnailFileFullPath, ReadOnlyMemory<byte> thumbnailData, DlnaMime? dlnaMime, string? dlnaProfileName)> CreateThumbnailFromVideoAsync(FileEntity fileEntity, DlnaMime dlnaMimeRequested)
        {
            if (fileEntity == null)
            {
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }

            FileInfo fileInfo = new(fileEntity.FilePhysicalFullPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }

            try
            {
                (_, var fileExtension, var dlnaProfileName) = ConvertDlnaMime(dlnaMimeRequested);

                string outputThumbnailFileFullPath = Path.Combine([fileEntity.Folder!, _serverConfig.SubFolderForThumbnail, fileEntity.FileName + fileExtension]);
                FileInfo thumbnailFile = new(outputThumbnailFileFullPath);
                var existsBefore = thumbnailFile.Exists;
                if (!existsBefore)
                {
                    DirectoryHelper.CreateDirectoryIfNoExists(thumbnailFile.Directory);

                    using (CancellationTokenSource cancellationTokenSource_FileInfo = new(TimeSpanValues.TimeMin5))
                    {
                        var mediaInfo = await FFmpegService.TryGetMediaInfo(fileEntity.FilePhysicalFullPath, cancellationTokenSource_FileInfo.Token);
                        if (mediaInfo is null
                            || mediaInfo.VideoStreams.FirstOrDefault() is not IVideoStream videoStream)
                        {
                            return (null, ReadOnlyMemory<byte>.Empty, null, null);
                        }

                        (var newHeight, var newWidth, _) = ThumbnailHelper.CalculateResize(videoStream.Height, videoStream.Width, (int)_serverConfig.MaxHeightForThumbnails, (int)_serverConfig.MaxWidthForThumbnails);

                        IConversion conversion = FFmpeg.Conversions.New()
                                .AddStream([videoStream
                                    .SetSeek(GetCaptureTime(videoStream))
                                    .SetOutputFramesCount(1)
                                    .SetSize(newWidth, newHeight)
                                    ])
                                .AddParameter("-err_detect ignore_err", ParameterPosition.PreInput) // Ignore errors
                                .AddParameter("-loglevel panic", ParameterPosition.PreInput) // Suppress logs
                                .SetOutput(outputThumbnailFileFullPath)
                                .SetPreset(ConversionPreset.VerySlow);

                        using (CancellationTokenSource cancellationTokenSource_Conversion = new(TimeSpanValues.TimeMin5))
                        {
                            var conversionResult = await conversion.Start(cancellationTokenSource_Conversion.Token);
                            
                            DebugCreateThumbnail(outputThumbnailFileFullPath, conversionResult.Duration.TotalMilliseconds);
                        }
                    }
                }

                var thumbnailData = _serverConfig.StoreThumbnailsForLocalMoviesInDatabase
                    ? (await FileService.ReadFileAsync(outputThumbnailFileFullPath) ?? ReadOnlyMemory<byte>.Empty)
                    : ReadOnlyMemory<byte>.Empty;

                return (outputThumbnailFileFullPath, thumbnailData, dlnaMimeRequested, dlnaProfileName);
            }
            catch (ConversionException ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                await Task.Delay(1_000);
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }
        }
        private static TimeSpan GetCaptureTime(IVideoStream videoStream)
        {
            return videoStream.Duration > TimeSpanValues.TimeHours1 ? TimeSpanValues.TimeMin30
                : videoStream.Duration > TimeSpanValues.TimeMin40 ? TimeSpanValues.TimeMin10
                : videoStream.Duration > TimeSpanValues.TimeMin20 ? TimeSpanValues.TimeMin5
                : videoStream.Duration > TimeSpanValues.TimeMin5 ? TimeSpanValues.TimeMin1
                : videoStream.Duration > TimeSpanValues.TimeMin2 ? TimeSpanValues.TimeSecs30
                : videoStream.Duration > TimeSpanValues.TimeSecs30 ? TimeSpanValues.TimeSecs10
                : videoStream.Duration > TimeSpanValues.TimeSecs5 ? TimeSpanValues.TimeSecs2
                : TimeSpan.FromSeconds(0);
        }

        private (DlnaMime dlnaMime, string fileExtension, string dlnaProfileName) ConvertDlnaMime(DlnaMime dlnaMimeRequested)
        {
            DlnaMime dlnaMime;
            string fileExtension;
            string dlnaProfileName;

            var fileExtensions = _serverConfig.MediaFileExtensions.Where(e => e.Value.Key == dlnaMimeRequested).ToArray();
            if (fileExtensions.Length > 0)
            {
                dlnaMime = ConvertFromDlnaMime(fileExtensions[0].Value.Key);
                fileExtension = fileExtensions[0].Key;
                dlnaProfileName = fileExtensions[0].Value.Value
                    ?? fileExtensions[0].Value.Key.ToMainProfileNameString()
                    ?? throw new ArgumentNullException($"No defined DLNA Profile Name for {fileExtensions[0].Value.Key}");
            }
            else
            {
                dlnaMime = ConvertFromDlnaMime(dlnaMimeRequested);
                fileExtension = dlnaMimeRequested.DefaultFileExtensions()[0];
                dlnaProfileName = dlnaMimeRequested.ToMainProfileNameString()
                    ?? throw new ArgumentNullException($"No defined DLNA Profile Name for {dlnaMimeRequested}");
            }

            return (dlnaMime, fileExtension, dlnaProfileName);
        }
        private static DlnaMime ConvertFromDlnaMime(DlnaMime dlnaMime)
        {
            return dlnaMime switch
            {
                DlnaMime.ImageBmp => DlnaMime.ImageBmp,
                DlnaMime.ImageGif => DlnaMime.ImageGif,
                DlnaMime.ImageJpeg => DlnaMime.ImageJpeg,
                DlnaMime.ImagePng => DlnaMime.ImagePng,
                DlnaMime.ImageWebp => DlnaMime.ImageWebp,
                DlnaMime.ImageXIcon => DlnaMime.ImageXIcon,
                DlnaMime.ImageXWindowsBmp => DlnaMime.ImageXWindowsBmp,
                _ => throw new NotImplementedException($"Not defined image format = {dlnaMime}"),
            };
        }

        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
