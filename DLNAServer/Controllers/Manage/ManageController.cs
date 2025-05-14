using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.ApiBlocking.Interfaces;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Features.Subscriptions.Data;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Diagnostics;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Runtime;

namespace DLNAServer.Controllers.Manage
{
    [Route("[controller]")]
    [ApiController]
    public partial class ManageController : Controller
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ManageController> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IMemoryCache MemoryCache;
        private readonly IApiBlockerService ApiBlockerService;
        private readonly IFileWatcherHandler FileWatcherHandler;
        private readonly Lazy<IFileRepository> _fileRepositoryLazy;
        private readonly Lazy<IDirectoryRepository> _directoryRepositoryLazy;
        private readonly Lazy<IVideoMetadataRepository> _videoMetadataRepositoryLazy;
        private readonly Lazy<IThumbnailRepository> _thumbnailRepositoryLazy;
        private readonly Lazy<IThumbnailDataRepository> _thumbnailDataRepositoryLazy;
        private readonly Lazy<IContentExplorerManager> _contentExplorerManagerLazy;
        private readonly Lazy<IMediaProcessingService> _mediaProcessingServiceLazy;

        private IFileRepository FileRepository => _fileRepositoryLazy.Value;
        private IDirectoryRepository DirectoryRepository => _directoryRepositoryLazy.Value;
        private IVideoMetadataRepository VideoMetadataRepository => _videoMetadataRepositoryLazy.Value;
        private IThumbnailRepository ThumbnailRepository => _thumbnailRepositoryLazy.Value;
        private IThumbnailDataRepository ThumbnailDataRepository => _thumbnailDataRepositoryLazy.Value;
        private IContentExplorerManager ContentExplorerManager => _contentExplorerManagerLazy.Value;
        private IMediaProcessingService MediaProcessingService => _mediaProcessingServiceLazy.Value;
        public ManageController(
            IServiceScopeFactory serviceScopeFactory,
            ServerConfig serverConfig,
            IMemoryCache memoryCache,
            IApiBlockerService apiBlockerService,
            IFileWatcherHandler fileWatcherHandler,
            Lazy<IFileRepository> fileRepositoryLazy,
            Lazy<IDirectoryRepository> directoryRepositoryLazy,
            Lazy<IVideoMetadataRepository> videoMetadataRepositoryLazy,
            Lazy<IThumbnailRepository> thumbnailRepositoryLazy,
            Lazy<IThumbnailDataRepository> thumbnailDataRepositoryLazy,
            Lazy<IContentExplorerManager> contentExplorerManagerLazy,
            Lazy<IMediaProcessingService> mediaProcessingServiceLazy,
            ILogger<ManageController> logger)
        {
            _serverConfig = serverConfig;
            MemoryCache = memoryCache;
            ApiBlockerService = apiBlockerService;
            FileWatcherHandler = fileWatcherHandler;
            _serviceScopeFactory = serviceScopeFactory;
            _fileRepositoryLazy = fileRepositoryLazy;
            _directoryRepositoryLazy = directoryRepositoryLazy;
            _videoMetadataRepositoryLazy = videoMetadataRepositoryLazy;
            _thumbnailRepositoryLazy = thumbnailRepositoryLazy;
            _thumbnailDataRepositoryLazy = thumbnailDataRepositoryLazy;
            _contentExplorerManagerLazy = contentExplorerManagerLazy;
            _mediaProcessingServiceLazy = mediaProcessingServiceLazy;
            _logger = logger;
        }
        [HttpGet("configuration")]
        public ActionResult<ServerConfig> GetServerConfigAsync()
        {
            return Ok(_serverConfig);
        }
        [HttpGet("database")]
        public async Task<ActionResult<IEnumerable<FileEntity>>> GetRowCountsAsync()
        {
            var rowCounts = await FileRepository.DbContext.GetAllTablesRowCountAsync();

            return Ok(rowCounts);
        }
        [HttpGet("file")]
        public async Task<ActionResult<IEnumerable<FileEntity>>> GetAllFilesAsync()
        {
            var files = (await FileRepository.GetAllAsync(asNoTracking: true, useCachedResult: false)).AsArray();

            return Ok(files);
        }
        [HttpGet("file/{guid}")]
        public async Task<ActionResult<FileEntity>> GetFileByIdAsync([FromRoute] string guid)
        {
            var files = await FileRepository.GetByIdAsync(guid, asNoTracking: true, useCachedResult: false);

            return Ok(files);
        }
        [HttpGet("fileLast")]
        public async Task<ActionResult<IEnumerable<FileEntity>>> GetLastFilesAsync()
        {
            var files = (await FileRepository.GetAllByAddedToDbAsync((int)_serverConfig.CountOfFilesByLastAddedToDb, _serverConfig.ExcludeFolders, useCachedResult: false)).AsArray();

            return Ok(files);
        }
        [HttpGet("directory")]
        public async Task<ActionResult<IEnumerable<DirectoryEntity>>> GetAllDirectoriesAsync()
        {
            var directories = (await DirectoryRepository.GetAllAsync(asNoTracking: true, useCachedResult: false)).AsArray();

            return Ok(directories);
        }
        [HttpGet("directory/{guid}")]
        public async Task<ActionResult<DirectoryEntity>> GetDirectoryByIdAsync([FromRoute] string guid)
        {
            var directories = await DirectoryRepository.GetByIdAsync(guid, asNoTracking: true, useCachedResult: false);

            return Ok(directories);
        }
        [HttpGet("videoMetadata")]
        public async Task<ActionResult<IEnumerable<MediaVideoEntity>>> GetAllVideoMetadataAsync()
        {
            var videoMetadata = (await VideoMetadataRepository.GetAllAsync(asNoTracking: true, useCachedResult: false)).AsArray();

            return Ok(videoMetadata);
        }
        [HttpGet("thumbnail")]
        public async Task<ActionResult<IEnumerable<ThumbnailEntity>>> GetAllThumbnailAsync()
        {
            var thumbnails = (await ThumbnailRepository.GetAllAsync(asNoTracking: true, useCachedResult: false)).AsArray();

            return Ok(thumbnails);
        }
        [HttpGet("thumbnail/{guid}")]
        public async Task<ActionResult<ThumbnailEntity>> GetThumbnailByGuidAsync([FromRoute] string guid)
        {
            var thumbnail = await ThumbnailRepository.GetByIdAsync(guid, asNoTracking: true, useCachedResult: false);

            return Ok(thumbnail);
        }
        [HttpGet("thumbnailData")]
        public async Task<ActionResult<IEnumerable<ThumbnailDataEntity>>> GetAllThumbnailDataAsync()
        {
            var thumbnailData = (await ThumbnailDataRepository.GetAllAsync(asNoTracking: true, useCachedResult: false)).AsArray();

            return Ok(thumbnailData);
        }
        [HttpGet("dlnaMime")]
        public IActionResult GetAllDlnaMimes()
        {
            var dlnaMimes = Enum.GetValues(typeof(DlnaMime)).Cast<DlnaMime>();

            var result = dlnaMimes?.Select(static (m) =>
            {
                try
                {
                    return new
                    {
                        Id = (int)m,
                        DlnaMime = $"{m}",
                        ContentType = m.ToMimeString(),
                        MimeDescription = m.ToMimeDescription(),
                        DlnaMedia = $"{m.ToDlnaMedia()}",
                        DefaultDlnaItemClass = $"{m.ToDefaultDlnaItemClass()}",
                        MainProfileName = m.ToMainProfileNameString() ?? string.Empty,
                        ProfileNames = m.ToProfileNameString(),
                        Extensions = m.DefaultFileExtensions(),
                        IsError = false,
                        Error = string.Empty
                    };
                }
                catch (Exception ex)
                {
                    return new
                    {
                        Id = (int)m,
                        DlnaMime = $"{m}",
                        ContentType = string.Empty,
                        MimeDescription = string.Empty,
                        DlnaMedia = string.Empty,
                        DefaultDlnaItemClass = string.Empty,
                        MainProfileName = string.Empty,
                        ProfileNames = Array.Empty<string>(),
                        Extensions = Array.Empty<string>(),
                        IsError = true,
                        Error = ex.Message
                    };
                }
            }
            ).ToList() ?? [];

            return Ok(result);
        }
        [HttpGet("subscriptions")]
        public ActionResult<IEnumerable<Subscription>> GetAllSubscriptionAsync()
        {
            return Ok();
        }
        [HttpGet("memoryCache")]
        public IActionResult GetMemoryCacheInfo()
        {
            Dictionary<string, string?> memoryCacheInfo = [];
            if (MemoryCache is MemoryCache memoryCache)
            {
                memoryCacheInfo.Add("Count", $"{memoryCache.Count}");
                if (MemoryCache.GetCurrentStatistics() is MemoryCacheStatistics memoryCacheStatistics)
                {
                    memoryCacheInfo.Add("Current_estimated_size", $"{memoryCacheStatistics.CurrentEstimatedSize}");
                }
                var keys = memoryCache
                    .Keys
                    .OrderBy(static (k) => k is not string)
                        .ThenBy(static (k) => k.ToString())
                    .Select(static (k, i) => new KeyValuePair<string, string?>($"Key_{i}", $"{k}"))
                    .ToList();
                foreach (var key in keys)
                {
                    memoryCacheInfo.Add(key.Key, key.Value);
                }
            }
            return Ok(memoryCacheInfo);
        }
        [HttpGet("memoryCacheClear")]
        public IActionResult GetMemoryCacheClear()
        {
            if (MemoryCache is MemoryCache memoryCache)
            {
                try
                {
                    foreach (var key in memoryCache.Keys)
                    {
                        MemoryCache.Remove(key);
                    }
                }
                catch (Exception ex)
                {
                    memoryCache.Clear();

                    _logger.LogGeneralErrorMessage(ex);
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect();

                return Ok($"Cleared. Actual count: {memoryCache.Count}");
            }

            return BadRequest($"MemoryCache is not {typeof(MemoryCache).FullName}");

        }
        [HttpGet("stop")]
        public IActionResult GetExitApplication()
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var hostApplicationLifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
                hostApplicationLifetime.StopApplication();
            }
            return Ok("stopping");
        }
        [HttpGet("restart")]
        public IActionResult GetRestartApplication()
        {
            ServerConfig.DlnaServerRestart = true;

            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var hostApplicationLifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

                LoggerHelper.InformationSendingRestart(_logger);

                hostApplicationLifetime.StopApplication();

                return Ok("restarting");
            }
        }
        [HttpGet("clearAllMetadata")]
        public async Task<IActionResult> GetClearAllMetadataAsync()
        {
            await ContentExplorerManager.ClearAllMetadataAsync();

            return Ok("cleared");
        }
        [HttpGet("clearAllThumbnails")]
        public async Task<IActionResult> GetClearAllThumbnailsAsync()
        {
            await ContentExplorerManager.ClearAllThumbnailsAsync();

            return Ok("cleared");
        }
        private static bool IsGetRecreateAllFilesInfoAsyncActive;
        [HttpGet("recreateAllFilesInfo")]
        public async Task<IActionResult> GetRecreateAllFilesInfoAsync([FromQuery] bool? deleteThumbnailFile)
        {
            if (IsGetRecreateAllFilesInfoAsyncActive)
            {
                return Ok("Recreating is in progress from another request.");
            }

            IsGetRecreateAllFilesInfoAsyncActive = true;
            ApiBlockerService.BlockApi(true, $"Recreate all file info");

            try
            {
                using (ScopeRecreatingFilesInfo(_logger))
                {
                    FileWatcherHandler.EnableRaisingEvents(false);

                    const int maxChunkSize = 50;
                    long fileCountAll;

                    MemoryInfo.LogMemoryInfo(_logger);
                    {
                        fileCountAll = await FileRepository.GetCountAsync();

                        MemoryInfo.LogMemoryInfo(_logger);
                        await ContentExplorerManager.CheckAllFilesExistingAsync();
                        MemoryInfo.LogMemoryInfo(_logger);
                        await ContentExplorerManager.CheckAllDirectoriesExistingAsync();
                        MemoryInfo.LogMemoryInfo(_logger);

                        InformationDoneCheckingFilesAndDirectories();

                        await ContentExplorerManager.ClearAllMetadataAsync();
                        await ContentExplorerManager.ClearAllThumbnailsAsync(deleteThumbnailFile ?? true);

                        InformationDoneClearingInfo();
                    }

                    int chunksCount = (int)Math.Round((double)fileCountAll / maxChunkSize, 0, MidpointRounding.ToPositiveInfinity);

                    MemoryInfo.LogMemoryInfo(_logger);
                    FileEntity[] files;
                    Stopwatch stopwatch = new();

                    for (int chunkIndex = 0; chunkIndex < chunksCount; chunkIndex++)
                    {
                        stopwatch.Restart();

                        InformationStartRefreshingInfoChunk(chunkIndex + 1, chunksCount, fileCountAll);

                        ApiBlockerService.BlockApi(true, string.Format("Recreate all file info. Progress {0} from {1}.", [chunkIndex + 1, chunksCount]));

                        using (ScopeRecreatingFilesInfoChunk(_logger))
                        {
                            DebugStartRecreatingInfoChunk(chunkIndex + 1, chunksCount, fileCountAll);

                            files = (await FileRepository
                                .GetAllAsync(chunkIndex * maxChunkSize, maxChunkSize, withIncludes: false, useCachedResult: false))
                                .AsArray();

                            await MediaProcessingService.FillEmptyInfoAsync(files, setCheckedForFailed: false);

                            FileRepository.DabataseClearChangeTracker();
                            await FileRepository.DatabaseShrinkMemoryAsync();


                            DebugDoneRecreatingInfoChunk(chunkIndex + 1, chunksCount);
                        }
                        stopwatch.Stop();
                        InformationDoneRefreshingInfoChunk(chunkIndex + 1, chunksCount, files.Length, stopwatch.Elapsed.TotalSeconds, fileCountAll);

                        Array.Clear(files);

                        await Task.Delay(TimeSpanValues.TimeSecs1); // 1sec delay for cool down hardware system resources
                        MemoryInfo.LogMemoryInfo(_logger);
                    }

                    InformationDoneRecreatingInfo();

                    await ContentExplorerManager.InitializeAsync();

                    await FileRepository.DatabaseShrinkMemoryAsync();
                    MemoryInfo.LogMemoryInfo(_logger);
                    ApiBlockerService.BlockApi(false);

                    return Ok("recreated");
                }
            }
            catch (Exception ex)
            {
                ApiBlockerService.BlockApi(false);
                return BadRequest(ex.Message);
            }
            finally
            {
                FileWatcherHandler.EnableRaisingEvents(true);
                IsGetRecreateAllFilesInfoAsyncActive = false;
                HttpContext.Response.RegisterForDispose(this);
                MemoryInfo.LogMemoryInfo(_logger);
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
                GC.Collect();
                MemoryInfo.LogMemoryInfo(_logger);
            }
        }
        [HttpGet("recreateFilesInfo/{guid}")]
        public async Task<IActionResult> GetRecreateFilesInfoAsync([FromRoute] string guid)
        {
            var file = await FileRepository.GetByIdAsync(guid, useCachedResult: false);
            if (file == null)
            {
                return BadRequest("File not found");
            }

            await ContentExplorerManager.ClearMetadataAsync([file]);
            await ContentExplorerManager.ClearThumbnailsAsync([file]);

            await MediaProcessingService.FillEmptyInfoAsync([file], setCheckedForFailed: false);

            file = await FileRepository.GetByIdAsync(guid, useCachedResult: false);

            return Ok(file);
        }
        [HttpGet("memory")]
        public IActionResult GetMemoryInfo()
        {
            var data = MemoryInfo.ProcessMemoryInfo();

            return Ok(data);
        }
    }
}
