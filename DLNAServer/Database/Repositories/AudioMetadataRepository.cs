using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace DLNAServer.Database.Repositories
{
    public class AudioMetadataRepository : BaseRepository<MediaAudioEntity>, IAudioMetadataRepository
    {
        public AudioMetadataRepository(DlnaDbContext dbContext, IMemoryCache memoryCache, ILogger<AudioMetadataRepository> logger)
            : base(dbContext, memoryCache, logger, nameof(AudioMetadataRepository))
        {
        }
    }
}
