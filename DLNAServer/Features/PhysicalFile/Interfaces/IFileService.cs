namespace DLNAServer.Features.PhysicalFile.Interfaces
{
    public interface IFileService
    {
        Task<ReadOnlyMemory<byte>?> ReadFileAsync(string filePath, long maxSizeOfFile = long.MaxValue);
    }
}
