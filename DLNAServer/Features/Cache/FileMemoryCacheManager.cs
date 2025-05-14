using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.Cache.Interfaces;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Helpers.Caching;
using DLNAServer.Helpers.Logger;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Runtime;

namespace DLNAServer.Features.Cache
{
    public partial class FileMemoryCacheManager : IFileMemoryCacheManager
    {
        private readonly ILogger<FileMemoryCacheManager> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IMemoryCache MemoryCache;
        private readonly IFileService FileService;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly static ConcurrentDictionary<string, SemaphoreSlim> cachingFilesInProgress = new();
        private readonly static SemaphoreSlim postEvictionCallbackInProgress = new(1, 1);
        private readonly static TimeSpan defaultExpiration = TimeSpanValues.TimeMin1;

        public FileMemoryCacheManager(
            ServerConfig serverConfig,
            IMemoryCache memoryCache,
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FileMemoryCacheManager> logger,
            IFileService fileService)
        {
            MemoryCache = memoryCache;
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _serverConfig = serverConfig;
            FileService = fileService;
        }
        public void CacheFileInBackground(FileEntity file, TimeSpan slidingExpiration)
        {
            Task backgroundCaching = new(async () =>
            {
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    slidingExpiration = slidingExpiration > defaultExpiration
                        ? slidingExpiration
                        : defaultExpiration;

                    (var isCachedSuccessful, _) = await CacheFileAndReturnAsync(file.FilePhysicalFullPath, slidingExpiration, true);

                    if (file.FileUnableToCache != !isCachedSuccessful)
                    {
                        var fileRepository = scope.ServiceProvider.GetRequiredService<IFileRepository>();

                        var fileCached = await fileRepository.GetByIdAsync(file.Id, asNoTracking: false, useCachedResult: true);
                        fileCached!.FileUnableToCache = !isCachedSuccessful;
                        _ = await fileRepository.SaveChangesAsync();
                    }

                    DebugBackgroundFileCachedDone(file!.FilePhysicalFullPath);
                }
            }, creationOptions: TaskCreationOptions.RunContinuationsAsynchronously);
            backgroundCaching.Start();
        }
        public async Task<(bool isCachedSuccessful, ReadOnlyMemory<byte> file)> CacheFileAndReturnAsync(
            string filePath,
            TimeSpan slidingExpiration,
            bool checkExistingInCache = true)
        {
            var fileLock = cachingFilesInProgress.GetOrAdd(filePath, new SemaphoreSlim(1, 1));

            DebugFileCacheStarted(filePath);
            _ = await fileLock.WaitAsync(TimeSpanValues.TimeMin30);

            try
            {
                if (checkExistingInCache)
                {
                    (bool isCached, ReadOnlyMemory<byte> file) = GetCheckCachedFile(filePath, slidingExpiration);
                    if (isCached)
                    {
                        DebugFileCacheBefore(filePath);
                        return (isCached, file);
                    }
                }

                FileInfo fileInfo = new(filePath);
                if (!fileInfo.Exists || fileInfo.Length == 0)
                {
                    return (false, ReadOnlyMemory<byte>.Empty);
                }
                var cachedData = await FileService.ReadFileAsync(filePath, (long)_serverConfig.MaxSizeOfFileForUseMemoryCacheInMBytes * (1024 * 1024));
                if (cachedData == null)
                {
                    return (false, ReadOnlyMemory<byte>.Empty);
                }
                GC.AddMemoryPressure(cachedData.Value.Length);

                CacheFileData(filePath, slidingExpiration, cachedData.Value);

                return (true, cachedData.Value);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return (false, ReadOnlyMemory<byte>.Empty);
            }
            finally
            {
                _ = fileLock.Release();

                DebugFileCacheFinished(filePath);

                _ = cachingFilesInProgress.Remove(filePath, out _);
            }
        }
        private void CacheFileData(string filePath, TimeSpan slidingExpiration, ReadOnlyMemory<byte> cachedData)
        {
            try
            {
                _ = MemoryCache.Set(GetFileCachedKey(filePath), cachedData, EntryOptions(cachedData.Length, slidingExpiration));

                MemoryCache.ScheduleCacheKeyEviction(
                    GetFileCachedKey(filePath),
                    // doubled slidingExpiration for streaming file
                    slidingExpiration.Add(slidingExpiration),
                    _logger);

                DebugFileCacheDone(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        public (bool isCached, ReadOnlyMemory<byte> file) GetCheckCachedFile(string filePath, TimeSpan slidingExpiration)
        {
            try
            {
                if (MemoryCache.TryGetValue(GetFileCachedKey(filePath), out ReadOnlyMemory<byte>? fileMemoryByte)
                    && fileMemoryByte != null
                    && fileMemoryByte.HasValue)
                {
                    MemoryCache.ScheduleCacheKeyEviction(
                        GetFileCachedKey(filePath),
                        // doubled slidingExpiration for streaming file
                        slidingExpiration.Add(slidingExpiration),
                        _logger);

                    return (true, fileMemoryByte.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
            return (false, ReadOnlyMemory<byte>.Empty);
        }

        private MemoryCacheEntryOptions EntryOptions(long size, TimeSpan slidingExpiration)
        {
            return new MemoryCacheEntryOptions()
            {
                Size = size,
                SlidingExpiration = slidingExpiration,
                AbsoluteExpirationRelativeToNow = TimeSpanValues.TimeHours12,
                Priority = CacheItemPriority.Low,
            }
            .RegisterPostEvictionCallback((key, value, reason, state) =>
            {
                MemoryCache.CancelCacheKeyEviction((string)key, _logger);

                Task clearMemory = new(async () =>
                {
                    await Task.Delay(TimeSpanValues.TimeSecs30);

                    _ = await postEvictionCallbackInProgress.WaitAsync(TimeSpanValues.TimeMin30);

                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.RemoveMemoryPressure(size);
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    await Task.Delay(TimeSpanValues.TimeSecs1);

                    _ = postEvictionCallbackInProgress.Release();

                });
                clearMemory.Start();
            });
        } 
        public void EvictSingleFile(string filePath)
        {
            try
            {
                string cachedKey = GetFileCachedKey(filePath);

                MemoryCache.Remove(cachedKey);

                MemoryCache.CancelCacheKeyEviction(cachedKey, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        private static string GetFileCachedKey(string filePath)
        {
            return string.Format("{0} {1} {2} {3}", [nameof(FileMemoryCacheManager), nameof(CacheFileData), typeof(byte[]).Name, filePath]);
        }
        public Task TerminateAsync()
        {
            cachingFilesInProgress.Clear();

            if (MemoryCache is MemoryCache memoryCache)
            {
                memoryCache.Compact(100);
                memoryCache.Clear();
            }
            MemoryCache.Dispose();

            return Task.CompletedTask;
        }
    }
}
