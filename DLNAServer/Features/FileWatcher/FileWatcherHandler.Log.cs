namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherHandler
    {
        [LoggerMessage(1, LogLevel.Warning, "Path is Already watching - '{pathToWatch}'")]
        partial void WarningPathAlreadyWatching(string pathToWatch);
        [LoggerMessage(2, LogLevel.Warning, "Directory not exists: {sourceFolder}")]
        partial void WarningDirectoryNotExists(string sourceFolder);
        [LoggerMessage(3, LogLevel.Debug, "Started watching path - '{pathToWatch}'")]
        partial void DebugStartedWatchingPath(string pathToWatch);
    }
}
