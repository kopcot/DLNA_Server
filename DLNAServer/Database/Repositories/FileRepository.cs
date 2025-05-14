using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class FileRepository : BaseRepository<FileEntity>, IFileRepository
    {
        public FileRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<FileRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(FileRepository))
        {
            DefaultOrderBy = static (entities) => entities
                .OrderBy(static (f) => f.LC_FilePhysicalFullPath)
                .ThenByDescending(static (f) => f.CreatedInDB);
            DefaultInclude = static (entities) => entities
                        .Include(static (f) => f.Directory)
                        .Include(static (f) => f.AudioMetadata)
                        .Include(static (f) => f.VideoMetadata)
                        .Include(static (f) => f.SubtitleMetadata)
                        .Include(static (f) => f.Thumbnail);
        }
        public new Task<bool> AddAsync(FileEntity entity)
        {
            return AddRangeAsync([entity]);
        }
        public new Task<bool> AddRangeAsync(IEnumerable<FileEntity> entities)
        {
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.Directory != null)
                    .Select(static (e) => e.Directory!)
                    .ToArray());
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.AudioMetadata != null)
                    .Select(static (e) => e.AudioMetadata!)
                    .ToArray());
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.VideoMetadata != null)
                    .Select(static (e) => e.VideoMetadata!)
                    .ToArray());
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.SubtitleMetadata != null)
                    .Select(static (e) => e.SubtitleMetadata!)
                    .ToArray());
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.Thumbnail != null)
                    .Select(static (e) => e.Thumbnail!)
                    .ToArray());
            DbContext.AttachRange(
                entities
                    .Where(static (e) => e.Thumbnail?.ThumbnailData != null)
                    .Select(static (e) => e.Thumbnail!.ThumbnailData!)
                    .ToArray());

            return base.AddRangeAsync(entities);
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByAddedToDbAsync(int takeNumber, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderByDescending(static (f) => f.CreatedInDB)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(fe => exclude.All(ef => !fe.LC_FilePhysicalFullPath.Contains(ef)))
                    .Take(takeNumber),
                cacheKey: GetCacheKey<FileEntity[]>([takeNumber.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            var exclude = excludeFolders.Select(static (ef) => ef.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(fe => fe.Directory != null
                        //&& expectedDirectories.Any(guid => guid == fe.Directory.Id)
                        && expectedDirectories.Contains(fe.Directory.Id)
                        && exclude.All(ef => !fe.LC_FilePhysicalFullPath.Contains(ef))),
                cacheKey: GetCacheKey<FileEntity[]>(expectedDirectories.Select(static (ed) => ed.ToString())),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true)
        {
            return GetAllByParentDirectoryIdsAsync(expectedDirectories.Select(static (ed) => Guid.TryParse(ed, out var dbGuid) ? dbGuid : Guid.Empty), excludeFolders, useCachedResult);
        }
        public Task<ReadOnlyMemory<string>> GetAllFileFullNamesAsync(bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .AsNoTracking()
                    .Select(static (f) => f.FilePhysicalFullPath),
                cacheKey: GetCacheKey<string[]>(methodName: nameof(GetAllFileFullNamesAsync)),
                cacheDuration: TimeSpanValues.TimeMin5,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public async Task<(bool ok, int? minDepth)> GetMinimalDepthAsync(bool useCachedResult = true)
        {
            var minDepth = await GetSingleWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .MinAsync(static (f) => f.Directory != null ? f.Directory.Depth : short.MaxValue),
                cacheKey: GetCacheKey<int>(methodName: nameof(GetMinimalDepthAsync)),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return minDepth == short.MaxValue ? (false, null) : (true, minDepth);
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .Where(f => f.Directory != null
                        && f.Directory.Depth == depth),
                cacheKey: GetCacheKey<FileEntity[]>([depth.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true)
        {
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .AsNoTracking()
                    .Include(static (f) => f.Directory)
                    .Where(f => f.Directory != null
                        && f.Directory.Depth == depth)
                    .Skip(skip)
                    .Take(take),
                cacheKey: GetCacheKey<FileEntity[]>([depth.ToString(), skip.ToString(), take.ToString()]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
        public Task<ReadOnlyMemory<FileEntity>> GetAllByPathFullNameAsync(string pathFullName, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(f => f.LC_FilePhysicalFullPath.Equals(pathFullName)),
                cacheKey: GetCacheKey<FileEntity[]>([pathFullName]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
