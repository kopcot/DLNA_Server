
using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Helpers.Logger;
using System.Collections.Concurrent;

namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherService : BackgroundService
    {
        private readonly ILogger<FileWatcherService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ServerConfig _serverConfig;
        private readonly IFileWatcherHandler _fileWatcherHandler;
        private readonly static ConcurrentDictionary<string, (int Count, SemaphoreSlim Semaphore)> _fileEventsInProgress = new();

        public FileWatcherService(
            ILogger<FileWatcherService> logger,
            ServerConfig serverConfig,
            IServiceScopeFactory serviceScopeFactory,
            IFileWatcherHandler fileWatcherHandler
            )
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _serverConfig = serverConfig;
            _fileWatcherHandler = fileWatcherHandler;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                InformationStarting();

                while (!stoppingToken.IsCancellationRequested)
                {
                    while (!stoppingToken.IsCancellationRequested // needed for stopping application, when events are still pending
                        && !_fileWatcherHandler.FileEventQueue.IsEmpty)
                    {
                        if (_fileWatcherHandler.FileEventQueue.TryDequeue(out var fileEvent))
                        {
                            var eventStartedTime = DateTime.UtcNow - fileEvent.eventTimeUTC;
                            if (eventStartedTime < TimeSpanValues.TimeSecs5)
                            {
                                await Task.Delay(eventStartedTime.Add(TimeSpanValues.TimeSecs1), stoppingToken);
                            }
                            await ExecuteEventHandlerAsync(fileEvent.fileFullPath, fileEvent.fileFullPathOld, fileEvent.changeType);
                        }
                        else
                        {
                            WarningUnableToDequeueEvent(_fileWatcherHandler.FileEventQueue.Count);
                        }
                    }

                    await Task.Delay(TimeSpanValues.TimeSecs5, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }

        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _fileEventsInProgress.Clear();
                await base.StopAsync(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private bool ShouldExcludeByThumbnailPath(string fullPath)
        {
            return fullPath.Contains(_serverConfig.SubFolderForThumbnail, StringComparison.InvariantCultureIgnoreCase);
        }
        private bool ShouldExcludeByExcludeFoldersPath(string fullPath)
        {
            return _serverConfig.ExcludeFolders.Any(exclude => fullPath.Contains(exclude, StringComparison.InvariantCultureIgnoreCase));
        }
        private bool IsFileExtensionMatch(string fullPath)
        {
            string fileExtension = new FileInfo(fullPath).Extension;
            return _serverConfig.MediaFileExtensions.Any(extension => fileExtension.EndsWith(extension.Key, StringComparison.InvariantCultureIgnoreCase));
        }
        private static bool IsDirectory(string fullPath)
        {
            return Directory.Exists(fullPath);
        }
        private async Task ExecuteEventHandlerAsync(
            string fullPath,
            string? fullPathOld,
            WatcherChangeTypes changeType
            )
        {
            DateTime eventTimestamp = DateTime.Now;

            if (CheckPathForExclude(changeType, fullPath))
            {
                return;
            }

            Guid guid = Guid.NewGuid();

            try
            {
                DebugEventStarted(changeType, fullPath, guid);

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var fileWatcherManager = scope.ServiceProvider.GetRequiredService<IFileWatcherManager>();

                    if (IsFileExtensionMatch(fullPath))
                    {
                        switch (changeType)
                        {
                            case WatcherChangeTypes.Created:
                            case WatcherChangeTypes.Changed:
                                if (!ShouldExcludeByExcludeFoldersPath(fullPath))
                                {
                                    await fileWatcherManager.HandleFileCreatedChanged(fullPath, changeType, eventTimestamp);
                                }
                                break;
                            case WatcherChangeTypes.Renamed:
                                await fileWatcherManager.HandleFileRenamed(fullPath, fullPathOld!, changeType, eventTimestamp);
                                break;
                            case WatcherChangeTypes.Deleted:
                                await fileWatcherManager.HandleFileRemove(fullPath, changeType, eventTimestamp);
                                break;
                        }
                    }
                    else if (IsDirectory(fullPath))
                    {
                        switch (changeType)
                        {
                            case WatcherChangeTypes.Renamed:
                                await fileWatcherManager.HandleDirectoryRenamed(fullPath, fullPathOld!, changeType, eventTimestamp);
                                break;
                            case WatcherChangeTypes.Deleted:
                                await fileWatcherManager.HandleDirectoryRemove(fullPath, changeType, eventTimestamp);
                                break;
                        }
                    }
                }
                DebugEventDone(changeType, fullPath, guid);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private bool CheckPathForExclude(WatcherChangeTypes changeType, string fullPath)
        {
            if (ShouldExcludeByThumbnailPath(fullPath))
            {
                DebugEventFilteredForThumbnailSubfolder(changeType, fullPath);
                return true;
            }

            //if (ShouldExcludeByExcludeFoldersPath(fullPath))
            //{
            //    DebugEventFilteredForExcludeDirectories(changeType, fullPath);
            //    return true;
            //}

            if (!IsFileExtensionMatch(fullPath) && !IsDirectory(fullPath))
            {
                DebugEventFilteredForExtensionOrNotDirectory(changeType, fullPath);
                return true;
            }

            return false;
        }
        public static Task TerminateAsync()
        {
            foreach (var fileLocks in _fileEventsInProgress)
            {
                try
                {
                    _ = (fileLocks.Value.Semaphore?.Release());
                }
                catch
                {
                    // no exeption during stopping appllication
                }
            }
            _fileEventsInProgress.Clear();

            return Task.CompletedTask;
        }
    }
}
