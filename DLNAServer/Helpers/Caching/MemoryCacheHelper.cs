using DLNAServer.Common;
using DLNAServer.Helpers.Logger;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace DLNAServer.Helpers.Caching
{
    public static class MemoryCacheHelper
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> EvictionControlTokens = new();

        /// <summary>
        /// Start a <see cref="Task"/> to evict <paramref name="cacheKey"/> from <see cref="IMemoryCache"/>.<br/>
        /// <paramref name="delayEviction"/> should be greater as value <see cref="MemoryCacheEntryOptions.SlidingExpiration"/>.<br/>
        /// If <see cref="MemoryCacheEntryOptions.PostEvictionCallbacks"/> is used, consider potential delays caused by their execution 
        /// and/or added delays there
        /// </summary> 
        /// <param name="memoryCache">The memory cache instance.</param>
        /// <param name="cacheKey">The cache key to be evicted.</param>
        /// <param name="delayEviction">The delay before attempting eviction.</param>
        /// <param name="logger">Logger for error reporting.</param>
        public static void ScheduleCacheKeyEviction(this IMemoryCache memoryCache, string cacheKey, TimeSpan delayEviction, ILogger logger)
        {
            string evictionCacheKey = GetEvictionKey(cacheKey);

            var evictionControlTokenSource = EvictionControlTokens.AddOrUpdate(
                key: evictionCacheKey,
                addValue: new CancellationTokenSource(),
                updateValueFactory: (key, existingCts) =>
                {
                    try
                    {
                        existingCts.Cancel();
                    }
                    catch (Exception ex)
                    {
                        logger.LogGeneralErrorMessage(ex);
                    }
                    return new CancellationTokenSource();
                });

            new Task(async () =>
            { 
                using (evictionControlTokenSource)
                {
                    try
                    {
                        var delay = GetDelay(delayEviction);

                        memoryCache.Remove(evictionCacheKey);

                        await Task.Delay(delay, evictionControlTokenSource.Token);

                        if (!evictionControlTokenSource.Token.IsCancellationRequested)
                        {
                            const object? storeValue = null;
                            _ = memoryCache.Set(
                                key: evictionCacheKey,
                                value: new WeakReference(storeValue),
                                options: memoryCacheEntryOptions);
                        }

                        await Task.Delay(memoryCacheEntryOptions.SlidingExpiration!.Value, evictionControlTokenSource.Token);

                        if (!evictionControlTokenSource.Token.IsCancellationRequested)
                        {
                            memoryCache.Remove(evictionCacheKey);
                        }

                    }
                    catch (ObjectDisposedException)
                    { }
                    catch (TaskCanceledException)
                    { }
                    catch (Exception ex)
                    {
                        logger.LogGeneralErrorMessage(ex);
                    }
                    finally
                    {
                        if (EvictionControlTokens.TryGetValue(evictionCacheKey, out var cts)
                            && cts == evictionControlTokenSource)
                        {
                            _ = EvictionControlTokens.TryRemove(evictionCacheKey, out _);
                        }
                    }
                }
            }, creationOptions: TaskCreationOptions.RunContinuationsAsynchronously).Start();
        }
        public static void CancelCacheKeyEviction(this IMemoryCache memoryCache, string cacheKey, ILogger logger)
        {
            new Task(async () =>
            {
                try
                {
                    string evictionCacheKey = GetEvictionKey(cacheKey);

                    if (EvictionControlTokens.TryGetValue(evictionCacheKey, out var cts))
                    {
                        cts.Cancel();

                        await Task.Delay(TimeSpanValues.TimeSecs1);

                        memoryCache.Remove(evictionCacheKey);
                    }

                }
                catch (ObjectDisposedException)
                { }
                catch (Exception ex)
                {
                    logger.LogGeneralErrorMessage(ex);
                }
            }, creationOptions: TaskCreationOptions.RunContinuationsAsynchronously).Start();
        }
        private static string GetEvictionKey(string cacheKey)
        {
            return string.Format("_{0} {1}", [string.Intern(nameof(ScheduleCacheKeyEviction)), cacheKey]);
        }
        private static TimeSpan GetDelay(TimeSpan cacheDuration)
        {
            const double minAddedSeconds = 2.0;
            const double maxAddedSeconds = 10.0;

            var addedSeconds = Math.Max(Math.Min(cacheDuration.TotalSeconds / 2, maxAddedSeconds), minAddedSeconds);

            return cacheDuration.Add(TimeSpan.FromSeconds(addedSeconds));
        }

        private static readonly MemoryCacheEntryOptions memoryCacheEntryOptions = new()
        {
            Size = 1,
            SlidingExpiration = TimeSpanValues.TimeSecs10,
            AbsoluteExpirationRelativeToNow = TimeSpanValues.TimeMin10,
            Priority = CacheItemPriority.Low
        };
    }
}
