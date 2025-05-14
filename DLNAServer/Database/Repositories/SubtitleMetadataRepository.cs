using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class SubtitleMetadataRepository : BaseRepository<MediaSubtitleEntity>, ISubtitleMetadataRepository
    {
        public SubtitleMetadataRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<SubtitleMetadataRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(SubtitleMetadataRepository))
        {
        }
    }
}
