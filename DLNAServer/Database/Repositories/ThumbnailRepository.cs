using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Helpers.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class ThumbnailRepository : BaseRepository<ThumbnailEntity>, IThumbnailRepository
    {
        public ThumbnailRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<ThumbnailRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(ThumbnailRepository))
        {
            defaultCacheDuration = TimeSpanValues.TimeMin5;
            defaultCacheAbsoluteDuration = TimeSpanValues.TimeHours1;
            DefaultOrderBy = static (entities) => entities
                .OrderBy(static (f) => f.LC_ThumbnailFilePhysicalFullPath)
                .ThenByDescending(static (f) => f.CreatedInDB);
            DefaultInclude = static (entities) => entities
                .Include(static (t) => t.ThumbnailData);
        }
        public Task<ReadOnlyMemory<ThumbnailEntity>> GetAllByPathFullNameAsync(string pathFullName, bool useCachedResult = true)
        {
            pathFullName = pathFullName.ToLower(culture: System.Globalization.CultureInfo.InvariantCulture);
            var memoryDataResult = GetAllWithCacheAsync(
                queryAction: DbSet
                    .OrderEntitiesByDefault(DefaultOrderBy)
                    .IncludeChildEntities(DefaultInclude)
                    .Where(t => t.LC_ThumbnailFilePhysicalFullPath != null
                        && t.LC_ThumbnailFilePhysicalFullPath.Equals(pathFullName)),
                cacheKey: GetCacheKey<ThumbnailEntity[]>([pathFullName]),
                cacheDuration: defaultCacheDuration,
                useCachedResult: useCachedResult
                );
            return memoryDataResult;
        }
    }
}
