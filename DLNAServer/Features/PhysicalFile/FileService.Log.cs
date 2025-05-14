namespace DLNAServer.Features.PhysicalFile
{
    public partial class FileService
    {
        [LoggerMessage(1, LogLevel.Debug, "Check file size.")]
        partial void LogCheckFileSize();

        [LoggerMessage(2, LogLevel.Debug, "File size '{fileSize}' incorrect for caching, max. config size {maxFileSize}, max. possible value {maxPossibleSize}, file path = {filePath}")]
        partial void LogFileSizeIncorrect(
            long fileSize,
            long maxFileSize,
            long maxPossibleSize,
            string filePath);
    }
}
