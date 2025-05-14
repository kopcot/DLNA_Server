using DLNAServer.Common;
using DLNAServer.Features.PhysicalFile.Interfaces;
using DLNAServer.Helpers.Logger;

namespace DLNAServer.Features.PhysicalFile
{
    public partial class FileService : IFileService
    {
        private readonly ILogger<FileService> _logger;
        public FileService(
            ILogger<FileService> logger)
        {
            _logger = logger;
        }
        public async Task<ReadOnlyMemory<byte>?> ReadFileAsync(string filePath, long maxSizeOfFile = long.MaxValue)
        {
            //const int bufferSize = 64 * 1_024; // less as 85,000 bytes in size for not need to use Large Object Heap (LOH) 
            try
            {
                FileInfo fileInfo = new(filePath);
                if (!fileInfo.Exists)
                {
                    return null;
                }
                LogCheckFileSize();
                if (fileInfo.Length > int.MaxValue ||
                    fileInfo.Length > maxSizeOfFile ||
                    fileInfo.Length == 0)
                {
                    LogFileSizeIncorrect(
                        fileInfo.Length,
                        int.MaxValue,
                        maxSizeOfFile,
                        filePath
                    );
                    return null;
                }

                using (CancellationTokenSource cts = new(TimeSpanValues.TimeMin10))
                {
                    return await File.ReadAllBytesAsync(filePath, cts.Token);
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return null;
            }
        }
    }
}
