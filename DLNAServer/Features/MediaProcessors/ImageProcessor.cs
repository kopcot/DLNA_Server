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
using SkiaSharp;
using System.Buffers;
using System.Collections.Concurrent;

namespace DLNAServer.Features.MediaProcessors
{
    public partial class ImageProcessor : IImageProcessor
    {
        private readonly ILogger<ImageProcessor> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IFileRepository FileRepository;
        private readonly IFileService FileService;
        private readonly IFFmpegService FFmpegService;
        private readonly ArrayPool<FileEntity> poolFileEntity = ArrayPool<FileEntity>.Shared;
        public ImageProcessor(
            ILogger<ImageProcessor> logger,
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
            return Task.CompletedTask;
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Image
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
                    file.IsMetadataChecked = true;
                    if (!file.IsThumbnailChecked && _serverConfig.GenerateThumbnailsForLocalImages)
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

                            file.IsMetadataChecked = true;

                            if (!file.IsThumbnailChecked && _serverConfig.GenerateThumbnailsForLocalImages)
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Image
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
                if (!_serverConfig.GenerateMetadataForLocalImages
                    || fileEntities.Length == 0)
                {
                    return;
                }

                if (fileEntities.Length == 1)
                {
                    var file = fileEntities.Span[0];
                    file.IsMetadataChecked = true;
                }
                else
                {
                    var maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                    _ = Parallel.For(
                        0,
                        fileEntities.Length,
                        parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                        (index, cancellationToken) =>
                        {
                            var file = fileEntities.Span[index];

                            file.IsMetadataChecked = true;
                        });
                }

                _ = await FileRepository.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
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
                        && fe.FileDlnaMime.ToDlnaMedia() == DlnaMedia.Image
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
                if (!_serverConfig.GenerateThumbnailsForLocalImages
                    || fileEntities.Length == 0)
                {
                    return;
                }

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
                (var thumbnailFileFullPath, var thumbnailData, var dlnaMime, var dlnaProfileName) = await CreateThumbnailFromImage(file, _serverConfig.DefaultDlnaMimeForImageThumbnails);

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
                        ThumbnailData = _serverConfig.StoreThumbnailsForLocalImagesInDatabase
                            ? new()
                            {
                                ThumbnailData = thumbnailData.AsArray(),
                                ThumbnailFilePhysicalFullPath = thumbnailFileFullPath,
                                FilePhysicalFullPath = file.FilePhysicalFullPath
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

        private async Task<(string? thumbnailFileFullPath, ReadOnlyMemory<byte> thumbnailData, DlnaMime? dlnaMime, string? dlnaProfileName)> CreateThumbnailFromImage(FileEntity fileEntity, DlnaMime dlnaMimeRequested)
        {
            if (fileEntity == null || !File.Exists(fileEntity.FilePhysicalFullPath))
            {
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }

            try
            {
                // can be done easier, but this way is using lower memory on the Linux → QNAP NAS 
                //
                // needed to have global env.variable on Linux set "MALLOC_TRIM_THRESHOLD_"
                // otherwise, memory will grow up with each Thumbnail creation with usage of SkiaSharp
                int newHeight;
                int newWidth;

                (SKEncodedImageFormat imageFormat, string fileExtension, string dlnaProfileName) = ConvertDlnaMime(dlnaMimeRequested);

                var outputThumbnailFileFullPath = Path.Combine([fileEntity.Folder!, _serverConfig.SubFolderForThumbnail, fileEntity.FileName + fileExtension]);
                FileInfo thumbnailFile = new(outputThumbnailFileFullPath);
                if (!thumbnailFile.Exists)
                {
                    await using (FileStream fileStream = new(fileEntity.FilePhysicalFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var codec = SKCodec.Create(fileStream, out var result))
                    {
                        if (codec == null || result != SKCodecResult.Success)
                        {
                            ErrorSKCodec(fileEntity.FilePhysicalFullPath, result);
                            return (null, ReadOnlyMemory<byte>.Empty, null, null);
                        }

                        (newHeight, newWidth, _) = ThumbnailHelper.CalculateResize(
                            actualHeight: codec.Info.Height,
                            actualWidth: codec.Info.Width,
                            maxHeight: (int)_serverConfig.MaxHeightForThumbnails,
                            maxWidth: (int)_serverConfig.MaxWidthForThumbnails);
                    }

                    DirectoryHelper.CreateDirectoryIfNoExists(thumbnailFile.Directory);

                    await using (FileStream fileStream = new(
                        path: fileEntity.FilePhysicalFullPath,
                        mode: FileMode.Open,
                        access: FileAccess.Read,
                        share: FileShare.Read,
                        bufferSize: 64 * 1024,
                        options: FileOptions.SequentialScan | FileOptions.Asynchronous))
                    using (SKBitmap sourceBitmap = SKBitmap.Decode(fileStream))
                    using (SKBitmap resizedBitmap = sourceBitmap.Resize(new SKImageInfo(newWidth, newHeight), samplingOptions))
                    using (SKData data = resizedBitmap.Encode(imageFormat, (int)_serverConfig.QualityForThumbnails))
                    {
                        using (FileStream fileStreamThumbnailFile = new(
                            path: outputThumbnailFileFullPath,
                            mode: FileMode.Create,
                            access: FileAccess.Write,
                            share: FileShare.Read))
                        using (CancellationTokenSource cts = new(TimeSpanValues.TimeMin5))
                        await using (Stream dataStream = data.AsStream())
                        {
                            await dataStream.CopyToAsync(fileStreamThumbnailFile, cts.Token);
                            await fileStreamThumbnailFile.FlushAsync(cts.Token);
                        }
                        DebugCreateThumbnail(outputThumbnailFileFullPath);

                        //do not return thumbnailData from here, it will increase memory overtime
                        //return (outputThumbnailFileFullPath, data.Span.ToArray(), dlnaMimeRequested, dlnaProfileName);
                    }
                }

                var thumbnailData = _serverConfig.StoreThumbnailsForLocalImagesInDatabase
                    ? (await FileService.ReadFileAsync(outputThumbnailFileFullPath) ?? ReadOnlyMemory<byte>.Empty)
                    : ReadOnlyMemory<byte>.Empty;

                return (outputThumbnailFileFullPath, thumbnailData, dlnaMimeRequested, dlnaProfileName);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex, $"File: {fileEntity.FilePhysicalFullPath}");
                return (null, ReadOnlyMemory<byte>.Empty, null, null);
            }
        }
        private static readonly SKSamplingOptions samplingOptions = new(SKFilterMode.Nearest, SKMipmapMode.Nearest);
        private (SKEncodedImageFormat dlnaMime, string fileExtension, string dlnaProfileName) ConvertDlnaMime(DlnaMime dlnaMimeRequested)
        {
            SKEncodedImageFormat imageFormat;
            string fileExtension;
            string dlnaProfileName;

            var fileExtensions = _serverConfig.MediaFileExtensions.Where(e => e.Value.Key == dlnaMimeRequested).ToArray();
            if (fileExtensions.Length > 0)
            {
                imageFormat = ConvertFromDlnaMime(fileExtensions[0].Value.Key);
                fileExtension = fileExtensions[0].Key;
                dlnaProfileName = fileExtensions[0].Value.Value
                    ?? fileExtensions[0].Value.Key.ToMainProfileNameString()
                    ?? throw new NullReferenceException($"No defined DLNA Profile Name for {fileExtensions[0].Value.Key}");
            }
            else
            {
                imageFormat = ConvertFromDlnaMime(dlnaMimeRequested);
                fileExtension = dlnaMimeRequested.DefaultFileExtensions()[0];
                dlnaProfileName = dlnaMimeRequested.ToMainProfileNameString()
                    ?? throw new NullReferenceException($"No defined DLNA Profile Name for {dlnaMimeRequested}");
            }

            return (imageFormat, fileExtension, dlnaProfileName);
        }
        private static SKEncodedImageFormat ConvertFromDlnaMime(DlnaMime dlnaMime)
        {
            return dlnaMime switch
            {
                DlnaMime.ImageBmp => SKEncodedImageFormat.Bmp,
                DlnaMime.ImageGif => SKEncodedImageFormat.Gif,
                DlnaMime.ImageJpeg => SKEncodedImageFormat.Jpeg,
                DlnaMime.ImagePng => SKEncodedImageFormat.Png,
                DlnaMime.ImageWebp => SKEncodedImageFormat.Webp,
                DlnaMime.ImageXIcon => SKEncodedImageFormat.Ico,
                DlnaMime.ImageXWindowsBmp => SKEncodedImageFormat.Wbmp,
                _ => throw new NotImplementedException($"Not defined Mime type = {dlnaMime}"),
            };
        }

        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
    }
}
