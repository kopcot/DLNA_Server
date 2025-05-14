using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Caching;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using System.Linq.Expressions;

namespace DLNAServer.Database.Repositories
{
    public abstract class BaseRepository<T> : IBaseRepository<T> where T : BaseEntity
    {
        protected readonly DlnaDbContext DbContext;
        protected DbSet<T> DbSet => DbContext.Set<T>();
        protected readonly IMemoryCache MemoryCache;
        private readonly ILogger _logger;
        protected readonly string _repositoryName;
        protected BaseRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger logger, string repositoryName)
        {
            DbContext = dbContext;
            MemoryCache = memoryCache;
            _logger = logger;
            _repositoryName = repositoryName;
        }
        protected TimeSpan defaultCacheDuration = TimeSpanValues.TimeSecs5;
        protected TimeSpan defaultCacheAbsoluteDuration = TimeSpanValues.TimeMin5;
        protected virtual Func<IQueryable<T>, IOrderedQueryable<T>> DefaultOrderBy { get; set; } = static (query) => query.OrderByDescending(static (e) => e.CreatedInDB);
        protected virtual Func<IQueryable<T>, IQueryable<T>> DefaultInclude { get; set; } = static (query) => query;
        DlnaDbContext IBaseRepository<T>.DbContext => DbContext;

        public Task<int> SaveChangesAsync()
        {
            return DbContext.SaveChangesAsync();
        }
        public async Task DatabaseShrinkMemoryAsync(CancellationToken cancellationToken = default)
        {
            using (var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken))
            {
                await DbContext.Database.ExecuteSqlRawAsync("PRAGMA shrink_memory; ", cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        public void DabataseClearChangeTracker()
        {
            DbContext.ChangeTracker.Clear();
        }

        public void MarkForDelete<T1>(T1 entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            _ = DbContext.Remove(entity);
        }
        public void MarkForUpdate<T1>(T1 entity)
        {
            ArgumentNullException.ThrowIfNull(entity);

            _ = DbContext.Update(entity);
        }
        public async Task<bool> DeleteAllAsync()
        {
            using (var transaction = await DbContext.Database.BeginTransactionAsync())
            {
                _ = await DbSet.ExecuteDeleteAsync();
                _ = await DbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            return true;
        }
        public async Task<bool> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, bool reloadTrackedEntities = false)
        {
            try
            {
                _ = await DbSet.Where(predicate).ExecuteDeleteAsync();

                if (reloadTrackedEntities)
                {
                    await ReloadTrackedEntitiesAsync(predicate);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }
        public async Task<bool> ExecuteUpdateAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> setPropertyCalls,
            bool reloadTrackedEntities = false)
        {
            try
            {
                _ = await DbSet.Where(predicate).ExecuteUpdateAsync(setPropertyCalls);

                if (reloadTrackedEntities)
                {
                    await ReloadTrackedEntitiesAsync(predicate);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                throw;
            }
        }
        public Task<bool> DeleteAsync(T entity)
        {
            _ = DbSet.Remove(entity);
            return DbContext.SaveChangesAsync().ContinueWith(static (e) => e.Result == 1);
        }
        public async Task<bool> DeleteByGuidAsync(string guid)
        {
            var entities = await GetByIdAsync(guid, asNoTracking: true, useCachedResult: false);
            if (entities != null)
            {
                return await DeleteAsync(entities);
            }
            return false;
        }
        public async Task<bool> DeleteRangeAsync(IEnumerable<T> entities)
        {
            using (var transaction = await DbContext.Database.BeginTransactionAsync())
            {
                DbSet.RemoveRange(entities);
                _ = await DbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            return true;
        }
        public async Task<bool> DeleteRangeByGuidsAsync(IEnumerable<Guid> guids)
        {
            using (var transaction = await DbContext.Database.BeginTransactionAsync())
            {
                var entities = await GetAllByIdsAsync(guids, false);
                DbSet.RemoveRange(entities.AsArray());
                _ = await DbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            return true;
        }
        public Task<bool> DeleteRangeByGuidsAsync(IEnumerable<string> guids)
        {
            List<Guid> guidsParsed = [];
            foreach (var guid in guids)
            {
                if (Guid.TryParse(guid, out var dbGuid))
                {
                    guidsParsed.Add(dbGuid);
                }
            }
            return DeleteRangeByGuidsAsync(guidsParsed);
        }
        public Task<ReadOnlyMemory<T>> GetAllAsync(bool withIncludes = true, bool asNoTracking = false, bool useCachedResult = true)
        {
            var query = DbSet
                .OrderEntitiesByDefault(DefaultOrderBy);
            query = withIncludes
                ? query.IncludeChildEntities(DefaultInclude)
                : query;
            query = asNoTracking
                ? query.AsNoTracking()
                : query;
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: query,
                cacheKey: GetCacheKey<T[]>(additionalArgs: [withIncludes.ToString(), asNoTracking.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<T>> GetAllAsync(int skip, int take, bool withIncludes = true, bool asNoTracking = false, bool useCachedResult = true)
        {
            var query = DbSet
                .OrderEntitiesByDefault(DefaultOrderBy);
            query = withIncludes
                ? query.IncludeChildEntities(DefaultInclude)
                : query;
            query = asNoTracking
                ? query.AsNoTracking()
                : query;
            query = query
                .Skip(skip)
                .Take(take);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: query,
                cacheKey: GetCacheKey<T[]>(additionalArgs: [withIncludes.ToString(), asNoTracking.ToString(), skip.ToString(), take.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<long> GetCountAsync(bool useCachedResult = true)
        {
            return GetSingleWithCacheAsync(
                queryAction: DbSet.AsNoTracking().LongCountAsync(),
                cacheKey: GetCacheKey<long>(),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
        }
        public Task<ReadOnlyMemory<T>> GetAllByIdsAsync(IEnumerable<Guid> guids, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                        .OrderEntitiesByDefault(DefaultOrderBy)
                        .IncludeChildEntities(DefaultInclude)
                        .Where(e => guids.Any(guid => guid == e.Id)),
                cacheKey: GetCacheKey<T[]>(guids.Select(static (g) => g.ToString())),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<T?> GetByIdAsync(Guid guid, bool asNoTracking = false, bool useCachedResult = true)
        {
            return GetSingleWithCacheAsync(
                queryAction: asNoTracking
                        ? DbSet
                            .OrderEntitiesByDefault(DefaultOrderBy)
                            .IncludeChildEntities(DefaultInclude)
                            .AsNoTracking()
                            .FirstOrDefaultAsync(e => e.Id == guid)
                        : DbSet
                            .OrderEntitiesByDefault(DefaultOrderBy)
                            .IncludeChildEntities(DefaultInclude)
                            .FirstOrDefaultAsync(e => e.Id == guid),
                cacheKey: GetCacheKey<T>([asNoTracking.ToString(), guid.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
        }
        public Task<T?> GetByIdAsync(string guid, bool asNoTracking = false, bool useCachedResult = true)
        {
            return Guid.TryParse(guid, out var dbGuid) ? GetByIdAsync(dbGuid, asNoTracking, useCachedResult) : Task.FromResult<T?>(null);
        }
        public Task<bool> AddAsync(T entity)
        {
            T[] entities = [entity];
            return AddRangeAsync(entities);
        }
        public async Task<bool> AddRangeAsync(IEnumerable<T> entities)
        {
            using (var transaction = await DbContext.Database.BeginTransactionAsync())
            {
                await DbSet.AddRangeAsync(entities);
                _ = await DbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            return true;
        }
        public Task<bool> IsAnyItemAsync()
        {
            return DbSet
                .AsNoTracking()
                .AnyAsync();
        }

        #region Helpers 
        protected async Task<ReadOnlyMemory<TResult>> GetAllWithCacheAsync<TResult>(
            IQueryable<TResult> queryAction,
            string cacheKey,
            TimeSpan cacheDuration,
            bool useCachedResult = true)
        {
            if (useCachedResult)
            {
                var resultAsMemory = await MemoryCache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        var data = await queryAction.ToArrayAsync();

                        entry.Value = data.AsReadOnly();
                        entry.SlidingExpiration = cacheDuration;
                        entry.AbsoluteExpirationRelativeToNow = defaultCacheAbsoluteDuration;
                        entry.Size = 1; // size is not important for entities vs physical cached file size

                        _ = entry.RegisterPostEvictionCallback((_, _, _, _) =>
                        {
                            MemoryCache.CancelCacheKeyEviction(cacheKey, _logger);
                        });

                        return data;
                    });

                MemoryCache.ScheduleCacheKeyEviction(cacheKey, cacheDuration, _logger);

                return resultAsMemory;
            }
            else
            {
                MemoryCache.Remove(cacheKey);

                return await queryAction.ToArrayAsync();
            }
        }
        protected async Task<TResult?> GetSingleWithCacheAsync<TResult>(
            Task<TResult?> queryAction,
            string cacheKey,
            TimeSpan cacheDuration,
            bool useCachedResult = true)
        {
            if (useCachedResult)
            {
                var result = await MemoryCache.GetOrCreateAsync(
                    cacheKey,
                    async entry =>
                    {
                        var resultSingle = (await queryAction);

                        entry.Value = resultSingle;
                        entry.SlidingExpiration = cacheDuration;
                        entry.AbsoluteExpirationRelativeToNow = defaultCacheAbsoluteDuration;
                        entry.Size = 1; // size is not important for entities vs physical cached file size

                        _ = entry.RegisterPostEvictionCallback((_, _, _, _) =>
                        {
                            MemoryCache.CancelCacheKeyEviction(cacheKey, _logger);
                        });

                        return resultSingle;
                    });

                MemoryCache.ScheduleCacheKeyEviction(cacheKey, cacheDuration, _logger);

                return result;
            }
            else
            {
                MemoryCache.Remove(cacheKey);

                return await queryAction;
            }
        }

        protected string GetCacheKey<TResult>(IEnumerable<string>? additionalArgs = null, [System.Runtime.CompilerServices.CallerMemberName] string methodName = "")
        {
            if (additionalArgs == null || !additionalArgs.Any())
            {
                return string.Format("{0} {1} {2}", [_repositoryName, methodName, typeof(TResult).Name]);
            }
            return string.Format("{0} {1} {2} {3}", [_repositoryName, methodName, typeof(TResult).Name, string.Join(';', additionalArgs)]);
        }
        public Task ReloadTrackedEntitiesAsync(Expression<Func<T, bool>> predicate)
        {
            var compiledPredicate = predicate.Compile();

            var entries = DbContext.ChangeTracker.Entries<T>()
                .AsParallel()
                .Where(e => e.State != EntityState.Detached
                    && compiledPredicate(e.Entity))
                .ToList();

            return Task.WhenAll(entries.Select(static (entry) => entry.ReloadAsync()));
        }
        #endregion
    }
}
