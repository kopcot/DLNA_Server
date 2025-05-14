using DLNAServer.Helpers.Interfaces;
using System.Collections.Concurrent;

namespace DLNAServer.Features.FileWatcher.Interfaces
{
    public interface IFileWatcherHandler : ITerminateAble
    {
        void WatchPath(string pathToWatch);
        void EnableRaisingEvents(bool enable);
        ConcurrentQueue<(string fileFullPath, string? fileFullPathOld, WatcherChangeTypes changeType, DateTime eventTimeUTC)> FileEventQueue { get; }
    }
}
