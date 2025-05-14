using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{

    public class DirectoryRepository : BaseRepository<DirectoryEntity>, IDirectoryRepository
    {
        public DirectoryRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<DirectoryRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(DirectoryRepository))
        {
            DefaultOrderBy = static (entities) => entities
                .OrderBy(static (d) => d.LC_DirectoryFullPath)
                .ThenByDescending(static (d) => d.CreatedInDB);
            DefaultInclude = static (entities) => entities
                .Include(static (d) => d.ParentDirectory);
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.ParentDirectory != null
                        //&& expectedDirectories.Any(guid => guid == d.ParentDirectory.Id)
                        && expectedDirectories.Contains(d.ParentDirectory.Id)
                        && exclude.All(ef => !d.LC_DirectoryFullPath.Contains(ef))),
                cacheKey: GetCacheKey<DirectoryEntity[]>(expectedDirectories.Select(static (e) => e.ToString())),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            return GetAllByParentDirectoryIdsAsync(expectedDirectories.Select(static (ed) => Guid.TryParse(ed, out var dbGuid) ? dbGuid : Guid.Empty), excludeFolders, useCachedResult);
        }
        public Task<ReadOnlyMemory<string>> GetAllDirectoryFullNamesAsync(bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .AsNoTracking()
                    .Select(static (d) => d.DirectoryFullPath),
                cacheKey: GetCacheKey<string[]>(),
                cacheDuration: TimeSpanValues.TimeMin5,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.Depth == depth),
                cacheKey: GetCacheKey<DirectoryEntity[]>([depth.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;

        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.Depth == depth),
                cacheKey: GetCacheKey<DirectoryEntity[]>([depth.ToString(), skip.ToString(), take.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNameAsync(string pathFullName, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.LC_DirectoryFullPath == pathFullName
                        || d.LC_DirectoryFullPath.StartsWith(pathFullName + Path.DirectorySeparatorChar)),
                cacheKey: GetCacheKey<DirectoryEntity[]>([pathFullName]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool useCachedResult = true)
        {
            pathFullNames = pathFullNames.Select(static (p) => p.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath)
                        || pathFullNames.Any(p => d.LC_DirectoryFullPath.StartsWith(p + Path.DirectorySeparatorChar))),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool asNoTracking = false, bool useCachedResult = true)
        {
            pathFullNames = pathFullNames.Select(static (p) => p.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: asNoTracking
                    ? DbSet
                        .OrderEntitiesByDefault(DefaultOrderBy)
                        .IncludeChildEntities(DefaultInclude)
                        .AsNoTracking()
                        .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath))
                    : DbSet
                        .OrderEntitiesByDefault(DefaultOrderBy)
                        .IncludeChildEntities(DefaultInclude)
                        .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath)),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
