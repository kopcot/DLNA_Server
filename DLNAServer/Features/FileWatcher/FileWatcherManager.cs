using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.Cache.Interfaces;
using DLNAServer.Features.FileWatcher.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;

namespace DLNAServer.Features.FileWatcher
{
    public partial class FileWatcherManager : IFileWatcherManager
    {
        private ILogger<FileWatcherManager> _logger;
        private readonly ServerConfig _serverConfig;
        private readonly IFileWatcherHandler _fileWatcherHandler;
        private readonly IFileMemoryCacheManager _fileMemoryCacheManager;
        private readonly IFileRepository _fileRepository;
        private readonly IContentExplorerManager _contentExplorerManager;
        private readonly IMediaProcessingService _mediaProcessingService;
        private readonly IDirectoryRepository _directoryRepository;
        private readonly IThumbnailRepository _thumbnailRepository;
        private static bool _areFileWatcherEventsAdded = false;
        public FileWatcherManager(
            ILogger<FileWatcherManager> logger,
            ServerConfig serverConfig,
            IFileWatcherHandler fileWatcherHandler,
            IFileRepository fileRepository,
            IContentExplorerManager contentExplorerManager,
            IMediaProcessingService mediaProcessingService,
            IFileMemoryCacheManager fileMemoryCacheManager,
            IDirectoryRepository directoryRepository,
            IThumbnailRepository thumbnailRepository)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            _fileWatcherHandler = fileWatcherHandler;
            _fileRepository = fileRepository;
            _contentExplorerManager = contentExplorerManager;
            _mediaProcessingService = mediaProcessingService;
            _fileMemoryCacheManager = fileMemoryCacheManager;
            _directoryRepository = directoryRepository;
            _thumbnailRepository = thumbnailRepository;
        }
        private static ulong _updatesCount = 0;
        public ulong UpdatesCount
        {
            get
            {
                if (_updatesCount >= uint.MaxValue)
                {
                    _updatesCount = uint.MinValue;
                }

                return _updatesCount;
            }
        }

        public async Task InitializeAsync()
        {
            await InitWatchingFilesAtSourceFoldersAsync();
            InformationStartedWatchingSourceFolders();
        }
        public Task TerminateAsync()
        {
            _areFileWatcherEventsAdded = false;
            _updatesCount = 0;

            return Task.CompletedTask;
        }
        private Task InitWatchingFilesAtSourceFoldersAsync()
        {
            if (!_areFileWatcherEventsAdded)
            {
                foreach (var sourceFolder in _serverConfig.SourceFolders)
                {
                    _fileWatcherHandler.WatchPath(sourceFolder);
                }

                _areFileWatcherEventsAdded = true;
            }

            return Task.CompletedTask;
        }
        public async Task HandleFileCreatedChanged(string fileFullPath, WatcherChangeTypes eventAction, DateTime eventTimestamp)
        {
            await HandleFileEvent(async (eventAction, fileInfo, __) =>
            {
                if (!fileInfo.Exists)
                {
                    WarningFileNotExists(eventAction, fileInfo.FullName);
                    await HandleFileRemove(fileInfo.FullName, WatcherChangeTypes.Deleted, eventTimestamp);
                    return;
                }

                var fileEntities = (await _fileRepository.GetAllByPathFullNameAsync(fileInfo.FullName, false)).AsArray();
                if (fileEntities == null || fileEntities.Length == 0)
                {
                    DebugAddToDatabase(eventAction, fileInfo.FullName);

                    var dlnaMime = GetConfiguredDlnaMimeFromFileExtension(fileInfo.Extension);
                    var inputFile = new Dictionary<DlnaMime, IEnumerable<string>> { { dlnaMime, [fileInfo.FullName] } };

                    await _contentExplorerManager.RefreshFoundFilesAsync(inputFile, true);
                }
                else
                {
                    DebugExistsInDatabase(eventAction, fileInfo.FullName);
                }
                if (_serverConfig.GenerateMetadataAndThumbnailsAfterAdding)
                {
                    var newFileEntities = (await _fileRepository.GetAllByPathFullNameAsync(fileInfo.FullName, false)).AsArray() ?? throw new NullReferenceException();
                    if (newFileEntities.Length == 0)
                    {
                        WarningFileRecordNotCreated(eventAction, fileInfo.FullName);
                    }
                    else if (newFileEntities.Length != 1)
                    {
                        WarningFileRecordMultiple(eventAction, fileInfo.FullName);
                    }
                    else
                    {
                        await _mediaProcessingService.FillEmptyInfoAsync(newFileEntities, false);
                    }
                }
            }, eventAction, fileFullPath, null, eventTimestamp);
        }
        public async Task HandleFileRenamed(string newFileFullPath, string oldFileFullPath, WatcherChangeTypes eventAction, DateTime eventTimestamp)
        {
            await HandleFileEvent(async (eventAction, fileInfo, fileInfoOld) =>
            {
                if (!fileInfo.Exists)
                {
                    WarningFileNotExists(eventAction, fileInfo.FullName);
                    await HandleFileRemove(fileInfo.FullName, WatcherChangeTypes.Deleted, eventTimestamp);
                    return;
                }

                var existingNewFiles = (await _fileRepository.GetAllByPathFullNameAsync(fileInfo.FullName, useCachedResult: false)).AsArray();
                if (existingNewFiles.Length != 0)
                {
                    await HandleFileRemove(fileInfo.FullName, WatcherChangeTypes.Deleted, eventTimestamp);
                }

                var existingOldFiles = (await _fileRepository.GetAllByPathFullNameAsync(fileInfoOld!.FullName, useCachedResult: false)).AsArray();
                if (existingOldFiles.Length == 0)
                {
                    var dlnaMime = GetConfiguredDlnaMimeFromFileExtension(fileInfo.Extension);
                    if (dlnaMime == DlnaMime.Undefined)
                    {
                        WarningFileExtensionUndefined(eventAction, fileInfo.Extension, fileInfo.FullName);
                    }
                    Dictionary<DlnaMime, IEnumerable<string>> inputFile = new() { { dlnaMime, [fileInfo.FullName] } };

                    await _contentExplorerManager.RefreshFoundFilesAsync(inputFile, true);
                }
                else
                {
                    await UpdateRenamedFile(existingOldFiles, fileInfo);

                    _fileMemoryCacheManager.EvictSingleFile(fileInfoOld!.FullName);
                }
            }, eventAction, newFileFullPath, oldFileFullPath, eventTimestamp);
        }
        public async Task HandleFileRemove(string fileFullPath, WatcherChangeTypes eventAction, DateTime eventTimestamp)
        {
            await HandleFileEvent(async (eventAction, fileInfo, __) =>
            {
                DebugFileRemove(eventAction, fileInfo.FullName);
                var files = (await _fileRepository.GetAllByPathFullNameAsync(fileInfo.FullName, useCachedResult: false)).AsArray();
                if (files == null || files.Length == 0)
                {
                    return;
                }

                await PrepareToRemoveEntity(_fileRepository, _thumbnailRepository, files);

                _ = await _fileRepository.DeleteRangeAsync(files);
                _ = await _thumbnailRepository.SaveChangesAsync();

                _fileMemoryCacheManager.EvictSingleFile(fileInfo.FullName);

                DebugFileRemoveDone(eventAction, fileInfo.FullName);

            }, eventAction, fileFullPath, null, eventTimestamp);
        }

        private static async Task PrepareToRemoveEntity(IFileRepository fileRepository, IThumbnailRepository thumbnailRepository, IEnumerable<FileEntity> files)
        {
            if (!files.Any())
            {
                return;
            }

            var thumbnailEntitiesIds = files
                .Where(static (f) => f.ThumbnailId.HasValue)
                .Select(static (f) => f.ThumbnailId!.Value)
                .ToArray();

            if (thumbnailEntitiesIds.Length > 0)
            {
                var thumbnailEntities = (await thumbnailRepository.GetAllByIdsAsync(thumbnailEntitiesIds)).AsArray();

                if (thumbnailEntities.Length != 0)
                {
                    DeleteThumbnailsIfExists(ref thumbnailEntities);
                    foreach (var thumbnailEntity in thumbnailEntities)
                    {
                        thumbnailRepository.MarkForDelete(thumbnailEntity.ThumbnailData);
                        thumbnailRepository.MarkForDelete(thumbnailEntity);
                    }
                }
            }

            files.AsParallel()
                .Where(static (nef) => nef.AudioMetadata != null)
                .Select(static (nef) => nef.AudioMetadata!)
                .ForAll(td => fileRepository.MarkForDelete(td));
            files.AsParallel()
                .Where(static (nef) => nef.VideoMetadata != null)
                .Select(static (nef) => nef.VideoMetadata!)
                .ForAll(td => fileRepository.MarkForDelete(td));
            files.AsParallel()
                .Where(static (nef) => nef.SubtitleMetadata != null)
                .Select(static (nef) => nef.SubtitleMetadata!)
                .ForAll(td => fileRepository.MarkForDelete(td));
            files.AsParallel()
                .Where(static (nef) => nef.Thumbnail != null)
                .Select(static (nef) => nef.Thumbnail!)
                .ForAll(td => fileRepository.MarkForDelete(td));

            foreach (var notExistingFile in files)
            {
                if (notExistingFile.Thumbnail?.ThumbnailDataId.HasValue == true)
                {
                    notExistingFile.Thumbnail.ThumbnailData = null;
                }
                notExistingFile.Thumbnail = null;
                notExistingFile.AudioMetadata = null;
                notExistingFile.VideoMetadata = null;
                notExistingFile.SubtitleMetadata = null;
            }
        }
        public async Task HandleDirectoryRemove(string fileFullPath, WatcherChangeTypes eventAction, DateTime eventTimestamp)
        {
            await HandleDirectoryEvent(async (eventAction, directoryInfo, _1) =>
            {
                var directories = (await _directoryRepository.GetAllStartingByPathFullNameAsync(directoryInfo.FullName, false)).AsArray();
                var files = (await _fileRepository.GetAllByParentDirectoryIdsAsync(directories.Select(static (d) => d.Id), [], false)).AsArray();

                await PrepareToRemoveEntity(_fileRepository, _thumbnailRepository, files);
                _ = await _fileRepository.DeleteRangeAsync(files);
                _ = await _directoryRepository.DeleteRangeAsync(directories);
                _ = await _thumbnailRepository.SaveChangesAsync();

            }, eventAction, fileFullPath, null, eventTimestamp);
        }

        public async Task HandleDirectoryRenamed(string newDirectoryFullPath, string oldDirectoryFullPath, WatcherChangeTypes eventAction, DateTime eventTimestamp)
        {
            await HandleDirectoryEvent(async (eventAction, directoryInfo, directoryInfoOld) =>
            {
                if (directoryInfoOld is null
                    || !directoryInfo.Exists)
                {
                    WarningDirectoryNotExists(eventAction, newDirectoryFullPath);
                    return;
                }

                var directories = (await _directoryRepository.GetAllStartingByPathFullNameAsync(directoryInfoOld.FullName, false)).AsArray();
                var files = (await _fileRepository.GetAllByParentDirectoryIdsAsync(directories.Select(static (d) => d.Id), [], false)).AsArray();
                UpdateFilePaths(ref files, directoryInfo.FullName, directoryInfoOld.FullName);
                UpdateDirectoryPaths(ref directories, directoryInfo.FullName, directoryInfoOld.FullName);

                // Save entities into database, as next function are getting dbSet.AsNoTracking() results
                _ = await _directoryRepository.SaveChangesAsync();
                _ = await _fileRepository.SaveChangesAsync();

                var newDirectories = await _contentExplorerManager.GetNewDirectoryEntities(directories.Select(static (d) => d.DirectoryFullPath));
                directories = directories.Concat(newDirectories).Distinct().ToArray();

                var existingDirectoryEntities = (await _directoryRepository.GetAllAsync(useCachedResult: false)).AsArray();

                FillParentDirectoriesAsync(ref existingDirectoryEntities, directories);
                FillParentDirectoriesAsync(ref existingDirectoryEntities, ref files, directories);

                _ = await _directoryRepository.SaveChangesAsync();
                _ = await _fileRepository.SaveChangesAsync();
            }, eventAction, newDirectoryFullPath, oldDirectoryFullPath, eventTimestamp);
        }
        private async Task UpdateRenamedFile(IEnumerable<FileEntity> files, FileInfo fileInfo)
        {
            var file = files.First();
            var filesToRemove = files.Where(f => f != file);

            file.FileName = fileInfo.Name;
            file.Title = fileInfo.Name;
            file.FilePhysicalFullPath = fileInfo.FullName;
            file.Folder = fileInfo.Directory?.FullName;
            file.FileModifiedDate = fileInfo.LastWriteTime;
            file.FileExtension = fileInfo.Extension;

            if (file.AudioMetadata != null)
            {
                file.AudioMetadata.FilePhysicalFullPath = file.FilePhysicalFullPath;
            }

            if (file.VideoMetadata != null)
            {
                file.VideoMetadata.FilePhysicalFullPath = file.FilePhysicalFullPath;
            }

            if (file.SubtitleMetadata != null)
            {
                file.SubtitleMetadata.FilePhysicalFullPath = file.FilePhysicalFullPath;
            }

            if (file.ThumbnailId.HasValue)
            {
                var thumbnailEntity = await _thumbnailRepository.GetByIdAsync(file.ThumbnailId.Value, asNoTracking: true, useCachedResult: true);

                if (thumbnailEntity != null)
                {
                    ThumbnailEntity[] thumbnailEntities = [thumbnailEntity];
                    DeleteThumbnailsIfExists(ref thumbnailEntities);
                    _fileRepository.MarkForDelete(file.Thumbnail);
                    _thumbnailRepository.MarkForDelete(thumbnailEntity.ThumbnailData);
                    file.IsThumbnailChecked = false;
                    file.Thumbnail = null;
                }
            }

            var newDirectoryEntities = await _contentExplorerManager.GetNewDirectoryEntities([fileInfo.Directory!.FullName]);
            var existingDirectoryEntities = (await _directoryRepository.GetAllAsync(useCachedResult: false)).AsArray();

            FileEntity[] fileEntities = [file];
            FillParentDirectoriesAsync(ref existingDirectoryEntities, newDirectoryEntities);
            FillParentDirectoriesAsync(ref existingDirectoryEntities, ref fileEntities, newDirectoryEntities);

            await PrepareToRemoveEntity(_fileRepository, _thumbnailRepository, filesToRemove);

            _ = await _directoryRepository.AddRangeAsync(newDirectoryEntities);
            _ = await _fileRepository.SaveChangesAsync();
            _ = await _thumbnailRepository.SaveChangesAsync();
            _ = await _fileRepository.DeleteRangeAsync(filesToRemove);
        }
        private static void UpdateFilePaths(ref FileEntity[] files, string newPath, string oldPath)
        {
            foreach (var file in files)
            {
                bool isFileInSameDirectory = file.Folder!.Equals(oldPath, StringComparison.InvariantCultureIgnoreCase);
                if (isFileInSameDirectory)
                {
                    file.Folder = file.Folder!.Replace(oldPath, newPath);
                    file.FilePhysicalFullPath = file.FilePhysicalFullPath.Replace(oldPath, newPath);
                    if (file.Thumbnail != null)
                    {
                        file.Thumbnail.FilePhysicalFullPath = file.Thumbnail.FilePhysicalFullPath.Replace(oldPath, newPath);
                    }
                }
                else
                {
                    file.Folder = file.Folder!.Replace(oldPath + Path.DirectorySeparatorChar, newPath + Path.DirectorySeparatorChar);
                    file.FilePhysicalFullPath = file.FilePhysicalFullPath.Replace(oldPath + Path.DirectorySeparatorChar, newPath + Path.DirectorySeparatorChar);
                    if (file.Thumbnail != null)
                    {
                        file.Thumbnail.FilePhysicalFullPath = file.Thumbnail.FilePhysicalFullPath.Replace(oldPath + Path.DirectorySeparatorChar, newPath + Path.DirectorySeparatorChar);
                    }
                }
            }
        }
        private static void UpdateDirectoryPaths(ref DirectoryEntity[] directories, string newPath, string oldPath)
        {
            DirectoryInfo directoryInfo;
            foreach (var directory in directories)
            {
                directory.DirectoryFullPath = directory.DirectoryFullPath.Replace(oldPath, newPath);

                directoryInfo = new(directory.DirectoryFullPath);

                directory.Directory = directoryInfo.Name;
                directory.Depth = GetDirectoryDepth(directory.DirectoryFullPath);
            }
        }
        private DlnaMime GetConfiguredDlnaMimeFromFileExtension(string fileExtension) =>
            _serverConfig
                .MediaFileExtensions
                // Contains() as Linux is adding for example ".jpg[1]" to the end of file, if file is exists there
                .FirstOrDefault(ex => fileExtension.Contains(ex.Key, StringComparison.OrdinalIgnoreCase))
                .Value
                .Key;
        private static void DeleteThumbnailsIfExists(ref ThumbnailEntity[] thumbnails)
        {
            FileInfo thumbnailInfo;
            var thumbnailsPaths = thumbnails
                .Where(static (te) => !string.IsNullOrWhiteSpace(te.ThumbnailFilePhysicalFullPath))
                .Select(static (te) => te.ThumbnailFilePhysicalFullPath);

            foreach (var thumbnail in thumbnailsPaths)
            {
                thumbnailInfo = new(thumbnail);
                var thumbnailDirectory = thumbnailInfo.Directory;
                if (thumbnailInfo.Exists)
                {
                    thumbnailInfo.Delete();
                }
                if (thumbnailDirectory?.Exists == true)
                {
                    bool thumbnailDirectory_SubDirectories = thumbnailDirectory.EnumerateDirectories().Any();
                    bool thumbnailDirectory_SubFiles = thumbnailDirectory.EnumerateFiles().Any();
                    if (!thumbnailDirectory_SubDirectories &&
                        !thumbnailDirectory_SubFiles)
                    {
                        thumbnailDirectory.Delete(true);
                    }
                }
            }
        }
        private async Task HandleFileEvent(Func<WatcherChangeTypes, FileInfo, FileInfo?, Task> fileOperation, WatcherChangeTypes action, string filePath, string? filePathOld, DateTime eventTimestamp)
        {
            Guid guid = Guid.NewGuid();

            try
            {
                DebugEventStarted(action, guid, eventTimestamp);
                FileInfo fileInfo = new(filePath);
                FileInfo? fileInfoOld = !string.IsNullOrWhiteSpace(filePathOld)
                    ? new(filePathOld)
                    : null;
                await fileOperation(action, fileInfo, fileInfoOld);
                DebugEventSuccessful(action, guid, eventTimestamp);
            }
            catch (Exception ex)
            {
                WarningEventFailed(action, guid, eventTimestamp);
                _logger.LogGeneralErrorMessage(ex);
            }

            _ = Interlocked.Increment(ref _updatesCount);
        }
        private async Task HandleDirectoryEvent(Func<WatcherChangeTypes, DirectoryInfo, DirectoryInfo?, Task> directoryOperation, WatcherChangeTypes action, string directoryPath, string? directoryPathOld, DateTime eventTimestamp)
        {
            Guid guid = Guid.NewGuid();

            try
            {
                DebugEventStarted(action, guid, eventTimestamp);
                DirectoryInfo directoryInfo = new(directoryPath);
                DirectoryInfo? directoryInfoOld = !string.IsNullOrWhiteSpace(directoryPathOld)
                    ? new(directoryPathOld)
                    : null;

                await directoryOperation(action, directoryInfo, directoryInfoOld);
                DebugEventSuccessful(action, guid, eventTimestamp);
            }
            catch (Exception ex)
            {
                WarningEventFailed(action, guid, eventTimestamp);
                _logger.LogGeneralErrorMessage(ex);
            }

            _ = Interlocked.Increment(ref _updatesCount);
        }
        private static void FillParentDirectoriesAsync(ref DirectoryEntity[] existingDirectoryEntities, ref FileEntity[] fileEntities, IEnumerable<DirectoryEntity> directoryEntities)
        {
            foreach (var file in fileEntities)
            {
                file.Directory = directoryEntities.FirstOrDefault(d => d.DirectoryFullPath == file.Folder)
                    ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == file.Folder);
            }
        }

        private static void FillParentDirectoriesAsync(ref DirectoryEntity[] existingDirectoryEntities, IEnumerable<DirectoryEntity> directoryEntities)
        {
            foreach (var directoryEntity in directoryEntities)
            {
                var parentDirectory = new DirectoryInfo(directoryEntity.DirectoryFullPath).Parent?.FullName;
                directoryEntity.ParentDirectory = directoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory)
                    ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory);
            }
        }
        private static int GetDirectoryDepth(string? actualFolder)
        {
            if (actualFolder == null)
            {
                return 0;
            }

            DirectoryInfo directoryInfoDepthCount = new(actualFolder);
            int depth = 0;
            while (directoryInfoDepthCount.Parent != null)
            {
                depth++;
                directoryInfoDepthCount = directoryInfoDepthCount.Parent;
            }
            return depth;
        }
    }
}
