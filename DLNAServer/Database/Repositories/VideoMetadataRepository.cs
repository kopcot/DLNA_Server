using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class VideoMetadataRepository : BaseRepository<MediaVideoEntity>, IVideoMetadataRepository
    {
        public VideoMetadataRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<VideoMetadataRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(VideoMetadataRepository))
        {
        }
    }
}
