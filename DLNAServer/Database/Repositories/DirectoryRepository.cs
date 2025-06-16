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
            var expectedDirectorySet = expectedDirectories as HashSet<Guid> ?? expectedDirectories.ToHashSet();
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();

            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .Where(d => d.ParentDirectoryId != null
                        && expectedDirectorySet.Contains(d.ParentDirectoryId.Value)
                        && exclude.All(ef => !EF.Functions.Collate(d.LC_DirectoryFullPath, "NOCASE").Contains(ef)))
                    .IncludeChildEntities(DefaultInclude)
                    .OrderEntitiesByDefault(DefaultOrderBy),
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
                    .AsNoTracking()
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .Select(static (d) => d.DirectoryFullPath),
                cacheKey: GetCacheKey<string[]>(),
                cacheDuration: TimeSpanValues.TimeMin5,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllWithEmptyParentDirectoryIdsAsync(string pathFullName, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .IncludeChildEntities(DefaultInclude)
                    .Where(fe => fe.ParentDirectoryId == null
                        && fe.LC_DirectoryFullPath.StartsWith(pathFullName)
                        && exclude.All(ef => !EF.Functions.Collate(fe.LC_DirectoryFullPath, "NOCASE").Contains(ef)))
                    .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>(excludeFolders.Select(static (ed) => ed.ToString()).Union([pathFullName])),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.Depth == depth)
                    .OrderEntitiesByDefault(DefaultOrderBy),
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
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.Depth == depth)
                    .OrderEntitiesByDefault(DefaultOrderBy),
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
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => d.LC_DirectoryFullPath == pathFullName
                        || d.LC_DirectoryFullPath.StartsWith(pathFullName + Path.DirectorySeparatorChar))
                    .OrderEntitiesByDefault(DefaultOrderBy),
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
                    .IncludeChildEntities(DefaultInclude)
                    .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath)
                        || pathFullNames.Any(p => d.LC_DirectoryFullPath.StartsWith(p + Path.DirectorySeparatorChar)))
                    .OrderEntitiesByDefault(DefaultOrderBy),
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
                        .IncludeChildEntities(DefaultInclude)
                        .AsNoTracking()
                        .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath))
                        .OrderEntitiesByDefault(DefaultOrderBy)
                    : DbSet
                        .IncludeChildEntities(DefaultInclude)
                        .Where(d => pathFullNames.Any(p => p == d.LC_DirectoryFullPath))
                        .OrderEntitiesByDefault(DefaultOrderBy),
                cacheKey: GetCacheKey<DirectoryEntity[]>(pathFullNames),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
