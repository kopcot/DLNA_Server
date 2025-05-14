using DLNAServer.Features.FileWatcher.Interfaces;
using System.Collections.Concurrent;

namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherHandler : IFileWatcherHandler
    {
        private readonly ILogger<FileWatcherHandler> _logger;
        private readonly static ConcurrentDictionary<string, FileSystemWatcher> _fileSystemWatchers = new();
        private readonly static ConcurrentQueue<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> _fileEventQueue = new();
        public FileWatcherHandler(
            ILogger<FileWatcherHandler> logger
            )
        {
            _logger = logger;
        }
        public void WatchPath(string pathToWatch)
        {
            if (_fileSystemWatchers.ContainsKey(pathToWatch))
            {
                WarningPathAlreadyWatching(pathToWatch);
                return;
            }

            if (!Directory.Exists(pathToWatch))
            {
                WarningDirectoryNotExists(pathToWatch);
                return;
            }

            FileSystemWatcher watcher = new(pathToWatch)
            {
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    //| NotifyFilters.Attributes 
                    //| NotifyFilters.Size
                    | NotifyFilters.LastWrite
                    //| NotifyFilters.LastAccess 
                    //| NotifyFilters.CreationTime
                    //| NotifyFilters.Security
                    ,
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                InternalBufferSize = 1_024 * 1_024,
            };

            // cannot be done, for Linux it is different file if it is .jpg, .JPG or .Jpg
            //ServerConfig.Extensions.ToList().ForEach(ex => watcher.Filters.Add("*" + ex.Key)); 


            watcher.Created += static (sender, args) => _fileEventQueue.Enqueue((
                fileFullPath: args.FullPath,
                fileFullPathOld: null,
                changeType: WatcherChangeTypes.Created,
                eventTimeUTC: DateTime.UtcNow
                ));
            watcher.Changed += static (sender, args) => _fileEventQueue.Enqueue((
                fileFullPath: args.FullPath,
                fileFullPathOld: null,
                changeType: WatcherChangeTypes.Changed,
                eventTimeUTC: DateTime.UtcNow
                ));
            watcher.Renamed += static (sender, args) => _fileEventQueue.Enqueue((
                fileFullPath: args.FullPath,
                fileFullPathOld: args.OldFullPath,
                changeType: WatcherChangeTypes.Renamed,
                eventTimeUTC: DateTime.UtcNow
                ));
            watcher.Deleted += static (sender, args) => _fileEventQueue.Enqueue((
                fileFullPath: args.FullPath,
                fileFullPathOld: null,
                changeType: WatcherChangeTypes.Deleted,
                eventTimeUTC: DateTime.UtcNow
                ));

            _ = _fileSystemWatchers.TryAdd(pathToWatch, watcher);

            DebugStartedWatchingPath(pathToWatch);
        }
        public void EnableRaisingEvents(bool enable)
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                watcher.Value.EnableRaisingEvents = enable;
            }
        }

        private static void UnwatchPath(string pathToWatch)
        {
            if (_fileSystemWatchers.TryRemove(pathToWatch, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
        }
        public Task TerminateAsync()
        {
            foreach (var watcher in _fileSystemWatchers)
            {
                UnwatchPath(watcher.Key);
            }

            _fileSystemWatchers.Clear();

            return Task.CompletedTask;
        }

        public ConcurrentQueue<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> FileEventQueue => _fileEventQueue;
    }
}
