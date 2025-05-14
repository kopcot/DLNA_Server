using DLNAServer.Common;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class ThumbnailDataRepository : BaseRepository<ThumbnailDataEntity>, IThumbnailDataRepository
    {
        public ThumbnailDataRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<ThumbnailDataRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(ThumbnailDataRepository))
        {
            defaultCacheDuration = TimeSpanValues.TimeMin5;
            defaultCacheAbsoluteDuration = TimeSpanValues.TimeHours1;
        }
    }
}
