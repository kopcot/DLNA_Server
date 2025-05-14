using DLNAServer.Database.Entities;

namespace DLNAServer.Database.Repositories.Interfaces
{
    public interface IDirectoryRepository : IBaseRepository<DirectoryEntity>
    {
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<Guid> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllByParentDirectoryIdsAsync(IEnumerable<string> expectedDirectories, IEnumerable<string> excludeFolders, bool useCachedResult = true);
        Task<ReadOnlyMemory<string>> GetAllDirectoryFullNamesAsync(bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNameAsync(string pathFullName, bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllStartingByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllByDirectoryDepthAsync(int depth, int skip, int take, bool useCachedResult = true);
        Task<ReadOnlyMemory<DirectoryEntity>> GetAllByPathFullNamesAsync(IEnumerable<string> pathFullNames, bool asNoTracking = false, bool useCachedResult = true);
    }
}
