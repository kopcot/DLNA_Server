using CommunityToolkit.HighPerformance;
using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Database.Entities;
using DLNAServer.Database.Repositories.Interfaces;
using DLNAServer.Features.MediaContent.Interfaces;
using DLNAServer.Features.MediaProcessors.Interfaces;
using DLNAServer.Helpers.Database;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.DLNA;
using Microsoft.EntityFrameworkCore;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace DLNAServer.Features.MediaContent
{
    public partial class ContentExplorerManager : IContentExplorerManager
    {
        private readonly ILogger<ContentExplorerManager> _logger;
        private readonly ServerConfig _serverConfig;

        private readonly Lazy<IFileRepository> _fileRepositoryLazy;
        private readonly Lazy<IDirectoryRepository> _directoryRepositoryLazy;
        private readonly Lazy<IThumbnailDataRepository> _thumbnailDataRepositoryLazy;
        private readonly Lazy<IMediaProcessingService> _mediaProcessingServiceLazy;
        private readonly Lazy<IAudioMetadataRepository> _audioMetadataRepositoryLazy;
        private readonly Lazy<IVideoMetadataRepository> _videoMetadataRepositoryLazy;
        private readonly Lazy<ISubtitleMetadataRepository> _subtitleMetadataRepositoryLazy;
        private readonly Lazy<IThumbnailRepository> _thumbnailRepositoryLazy;

        private IFileRepository FileRepository => _fileRepositoryLazy.Value;
        private IDirectoryRepository DirectoryRepository => _directoryRepositoryLazy.Value;
        private IThumbnailDataRepository ThumbnailDataRepository => _thumbnailDataRepositoryLazy.Value;
        private IMediaProcessingService MediaProcessingService => _mediaProcessingServiceLazy.Value;
        private IAudioMetadataRepository AudioMetadataRepository => _audioMetadataRepositoryLazy.Value;
        private IVideoMetadataRepository VideoMetadataRepository => _videoMetadataRepositoryLazy.Value;
        private ISubtitleMetadataRepository SubtitleMetadataRepository => _subtitleMetadataRepositoryLazy.Value;
        private IThumbnailRepository ThumbnailRepository => _thumbnailRepositoryLazy.Value;
        private readonly ArrayPool<FileEntity> poolFileEntity = ArrayPool<FileEntity>.Shared;
        private readonly ArrayPool<DirectoryEntity> poolDirectoryEntity = ArrayPool<DirectoryEntity>.Shared;
        public ContentExplorerManager(
            ILogger<ContentExplorerManager> logger,
            ServerConfig serverConfig,
            Lazy<IFileRepository> fileRepositoryLazy,
            Lazy<IDirectoryRepository> directoryRepositoryLazy,
            Lazy<IThumbnailDataRepository> thumbnailDataRepositoryLazy,
            Lazy<IMediaProcessingService> mediaProcessingServiceLazy,
            Lazy<IAudioMetadataRepository> audioMetadataRepositoryLazy,
            Lazy<IVideoMetadataRepository> videoMetadataRepositoryLazy,
            Lazy<ISubtitleMetadataRepository> subtitleMetadataRepositoryLazy,
            Lazy<IThumbnailRepository> thumbnailRepositoryLazy)
        {
            _logger = logger;
            _serverConfig = serverConfig;
            _fileRepositoryLazy = fileRepositoryLazy;
            _directoryRepositoryLazy = directoryRepositoryLazy;
            _thumbnailDataRepositoryLazy = thumbnailDataRepositoryLazy;
            _mediaProcessingServiceLazy = mediaProcessingServiceLazy;
            _audioMetadataRepositoryLazy = audioMetadataRepositoryLazy;
            _videoMetadataRepositoryLazy = videoMetadataRepositoryLazy;
            _subtitleMetadataRepositoryLazy = subtitleMetadataRepositoryLazy;
            _thumbnailRepositoryLazy = thumbnailRepositoryLazy;
        }

        public async Task InitializeAsync()
        {
            var inputFiles = GetAllFilesInFolders(_serverConfig.SourceFolders, true);
            await RefreshFoundFilesAsync(inputFiles, true);

            await CheckAllFilesExistingAsync();
            await CheckAllDirectoriesExistingAsync();

            var filesInDbCount = await FileRepository.GetCountAsync();
            var directoriesInDbCount = await DirectoryRepository.GetCountAsync();
            InformationRefreshedInfo((int)directoriesInDbCount, (int)filesInDbCount);

        }
        public Task TerminateAsync()
        {
            return Task.CompletedTask;
        }
        private Dictionary<DlnaMime, IEnumerable<string>> GetAllFilesInFolders(IEnumerable<string> sourceFolders, bool withSubdirectories)
        {
            ConcurrentBag<string> filesInSourceFolders = [];

            foreach (var sourceFolder in sourceFolders)
            {
                var directory = new DirectoryInfo(sourceFolder);
                if (!directory.Exists)
                {
                    WarningDirectoryNotExists(sourceFolder);
                    continue;
                }
                // unable to use search patters from ServerConfig.Extensions,
                // as for Linux it is different between .jpg, .JPG, .Jpg
                // 'MatchCasing = MatchCasing.CaseInsensitive' is not helpful  

                var enumOptions = enumerationOptionsDefault;
                enumOptions.ReturnSpecialDirectories = withSubdirectories;
                 
                Partitioner.Create(directory.EnumerateFiles("*.*", enumOptions))
                    .AsParallel()
                    .Select(static (f) => f.FullName)
                    .ForAll(filesInSourceFolders.Add);
            }

            var excludeFolders = new HashSet<string>(_serverConfig.ExcludeFolders, StringComparer.InvariantCultureIgnoreCase);
            var mediaFileExtensions = _serverConfig.MediaFileExtensions
                .ToDictionary(
                    keySelector: static (kvp) => kvp.Key,
                    elementSelector: static (kvp) => kvp.Value.Key,
                    comparer: StringComparer.InvariantCultureIgnoreCase);
             
            Dictionary<DlnaMime, IEnumerable<string>> foundFiles = Partitioner.Create(filesInSourceFolders)
                .AsParallel()
                .Where(f => !excludeFolders.Any(skip => f.Contains(skip, StringComparison.InvariantCultureIgnoreCase)))
                .GroupBy(f =>
                {
                    var extension = mediaFileExtensions.FirstOrDefault(e => f.EndsWith(e.Key, StringComparison.InvariantCultureIgnoreCase));
                    return extension.Value;
                })
                .Where(static (g) => g.Key != DlnaMime.Undefined)
                .ToDictionary(
                    keySelector: static (g) => g.Key,
                    elementSelector: static (g) => g.OrderBy(static (f) => f).AsEnumerable());

            return foundFiles;

        }

        private static readonly EnumerationOptions enumerationOptionsDefault = new()
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.Hidden
                             | FileAttributes.System
                             | FileAttributes.Temporary
                             | FileAttributes.SparseFile
                             | FileAttributes.ReparsePoint
                             | FileAttributes.Compressed
                             ,
            //BufferSize = 1_024_000,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
            MaxRecursionDepth = int.MaxValue,
            ReturnSpecialDirectories = true,
        };

        private static readonly SemaphoreSlim semaphoreRefreshFoundFiles = new(1, 1);
        /// <param name="inputFiles">Files to check and add to database</param>
        /// <param name="shouldBeAdded"><see langword="true"/> if <paramref name="inputFiles"/> should not exists in the database</param>
        /// <returns></returns>
        public async Task RefreshFoundFilesAsync(Dictionary<DlnaMime, IEnumerable<string>> inputFiles, bool shouldBeAdded)
        {
            try
            {
                _ = await semaphoreRefreshFoundFiles.WaitAsync(TimeSpanValues.TimeMin5);

                ConcurrentBag<FileEntity> fileEntities = [];

                var existingFiles = (await FileRepository.GetAllFileFullNamesAsync(useCachedResult: !shouldBeAdded)).AsArray();
                var existingFilesHash = new HashSet<string>(existingFiles);

                foreach (var mimeGroup in inputFiles)
                {
                    var fileExtensionConfiguration = _serverConfig.MediaFileExtensions.FirstOrDefault(e => e.Value.Key == mimeGroup.Key);
                    var fileExtension = string.Intern(fileExtensionConfiguration.Key.ToUpperInvariant());
                    var fileDlnaProfileName = fileExtensionConfiguration.Value.Value != null
                        ? string.Intern(fileExtensionConfiguration.Value.Value)
                        : mimeGroup.Key.ToMainProfileNameString();
                    var upnpClass = mimeGroup.Key.ToDefaultDlnaItemClass();
                     
                    Partitioner.Create(mimeGroup.Value)
                        .AsParallel()
                        .WithDegreeOfParallelism((int)_serverConfig.ServerMaxDegreeOfParallelism)
                        .WithMergeOptions(ParallelMergeOptions.NotBuffered)
                        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                        .Where(file => !existingFilesHash.Contains(file)
                            && File.Exists(file))
                        .Select(file =>
                        {
                            FileInfo fileInfo = new(file);
                            return new FileEntity()
                            {
                                FileCreateDate = fileInfo.CreationTime,
                                FileModifiedDate = fileInfo.LastWriteTime,
                                FileName = fileInfo.Name,
                                FileExtension = fileExtension,
                                Folder = fileInfo.DirectoryName != null ? string.Intern(fileInfo.DirectoryName) : null,
                                FilePhysicalFullPath = fileInfo.FullName,
                                Title = fileInfo.Name,
                                FileSizeInBytes = fileInfo.Length,
                                FileDlnaMime = mimeGroup.Key,
                                FileDlnaProfileName = fileDlnaProfileName,
                                UpnpClass = upnpClass,
                            };
                        })
                        .ForAll(fileEntities.Add);
                }

                if (fileEntities.IsEmpty)
                {
                    return;
                }

                var folders = fileEntities
                    .Select(static (f) => f.Folder)
                    .DistinctBy(static (f) => f)
                    .Where(static (f) => !string.IsNullOrWhiteSpace(f))
                    .ToArray();

                IEnumerable<DirectoryEntity> directoryEntities = await GetNewDirectoryEntities(folders);
                // Fill parent directory after creation of all directories
                await FillParentDirectoriesAsync(fileEntities, directoryEntities);

                if (directoryEntities.Any() || !fileEntities.IsEmpty)
                {
                    InformationTotalAdding(directoryEntities.Count(), fileEntities.Count);

                    const int maxShownCount = 10;
                    if (directoryEntities.Any())
                    {
                        InformationDirectoriesCount(
                            string.Join(Environment.NewLine, directoryEntities.Select(static (fe) => fe.DirectoryFullPath).Take(maxShownCount)),
                            directoryEntities.Count() > maxShownCount ? $"{Environment.NewLine}..." : string.Empty);

                        _ = await DirectoryRepository.AddRangeAsync(directoryEntities);
                    }
                    if (!fileEntities.IsEmpty)
                    {
                        InformationFilesCount(
                            string.Join(Environment.NewLine, fileEntities.Select(static (fe) => fe.FilePhysicalFullPath).Take(maxShownCount)),
                            directoryEntities.Count() > maxShownCount ? $"{Environment.NewLine}..." : string.Empty);

                        _ = await FileRepository.AddRangeAsync(fileEntities);
                    }
                    // to refresh cached value
                    // cached at first lines of FillParentDirectoriesAsync method
                    _ = await DirectoryRepository.GetAllAsync(useCachedResult: false);
                    // to refresh cached value
                    // cached at first lines of this method
                    _ = await FileRepository.GetAllFileFullNamesAsync(useCachedResult: false);
                }
            }
            finally
            {
                _ = semaphoreRefreshFoundFiles.Release();
            }
        }

        private async Task FillParentDirectoriesAsync(IEnumerable<FileEntity> fileEntities, IEnumerable<DirectoryEntity> directoryEntities)
        {
            var existingDirectoryEntities = (await DirectoryRepository.GetAllAsync(useCachedResult: true)).AsArray();

            foreach (var directoryEntity in directoryEntities)
            {
                if (new DirectoryInfo(directoryEntity.DirectoryFullPath).Parent is DirectoryInfo parentDirectory)
                {
                    directoryEntity.ParentDirectory = directoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory.FullName)
                        ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == parentDirectory.FullName)
                        ?? throw new ApplicationException($"Parent directory not found for directory '{parentDirectory.FullName}'");
                }
                else
                {
                    DebugDirectoryWithoutParent(directoryEntity.DirectoryFullPath);
                }
            }
            foreach (var file in fileEntities)
            {
                file.Directory = directoryEntities.FirstOrDefault(d => d.DirectoryFullPath == file.Folder)
                    ?? existingDirectoryEntities.FirstOrDefault(de => de.DirectoryFullPath == file.Folder)
                    ?? throw new ApplicationException($"Parent directory not found for file '{file.Folder}'");
            }
        }
        public async Task<List<DirectoryEntity>> GetNewDirectoryEntities(IEnumerable<string?> folders)
        {
            var existingDirectories = (await DirectoryRepository
                .GetAllDirectoryFullNamesAsync(useCachedResult: false))
                .AsArray();

            List<DirectoryEntity> directoryEntities = [];
            foreach (var folder in folders)
            {
                DirectoryInfo? directoryInfo = new(folder!);
                while (directoryInfo != null &&
                    directoryInfo.Exists)
                {
                    if (!existingDirectories.Contains(directoryInfo.FullName)
                        && !directoryEntities.Any(d => d.DirectoryFullPath == directoryInfo.FullName))
                    {
                        DirectoryEntity directoryEntity = new()
                        {
                            Directory = directoryInfo.Name,
                            DirectoryFullPath = directoryInfo.FullName,
                            ParentDirectory = null,
                            Depth = GetDirectoryDepth(directoryInfo.FullName),
                        };
                        directoryEntities.Add(directoryEntity);
                    }
                    else
                    {
                        break;
                    }

                    directoryInfo = directoryInfo.Parent;
                }
            }

            return directoryEntities;
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

        private async Task<(ReadOnlyMemory<FileEntity> files, ReadOnlyMemory<DirectoryEntity> directories)> GetFilesAndDirectoriesAsync(string objectID)
        {
            var directory = await DirectoryRepository.GetByIdAsync(objectID, asNoTracking: true, useCachedResult: true);
            ReadOnlyMemory<DirectoryEntity> directoryContainers;
            ReadOnlyMemory<FileEntity> filesItems;

            if (directory is DirectoryEntity)
            {
                // not possible to take cached result because some files can be removed during server offline time
                // and in next parts, there is check for existing file
                filesItems = await FileRepository
                    .GetAllByParentDirectoryIdsAsync([directory.Id], _serverConfig.ExcludeFolders, useCachedResult: false);
                directoryContainers = await DirectoryRepository
                    .GetAllByParentDirectoryIdsAsync([directory.Id], _serverConfig.ExcludeFolders, useCachedResult: false);
            }
            else
            {
                filesItems = ReadOnlyMemory<FileEntity>.Empty;
                directoryContainers = await DirectoryRepository
                    .GetAllByPathFullNamesAsync(_serverConfig.SourceFolders, useCachedResult: true);
            }

            return (files: filesItems, directories: directoryContainers);
        }
        private Task<(ReadOnlyMemory<FileEntity> files, ReadOnlyMemory<DirectoryEntity> directories)> GetFilesByLastAddedToDbAsync(uint numberOfFiles)
        {
            return FileRepository
                .GetAllByAddedToDbAsync((int)numberOfFiles, _serverConfig.ExcludeFolders, useCachedResult: false)
                .ContinueWith(static (fe) => (fe.Result, ReadOnlyMemory<DirectoryEntity>.Empty));
        }
        public async Task CheckAllFilesExistingAsync()
        {
            FileRepository.DabataseClearChangeTracker();
            const int batchSize = 1000;
            int offset = 0;
            ReadOnlyMemory<FileEntity> fileEntities;
            while (true)
            {
                fileEntities = await FileRepository.GetAllAsync(offset, batchSize, withIncludes: false, useCachedResult: false);
                if (fileEntities.IsEmpty)
                {
                    break;
                }

                _ = await CheckFilesExistingAsync(fileEntities);
                offset += fileEntities.Length;
            }
            FileRepository.DabataseClearChangeTracker();
        }
        public ValueTask<ReadOnlyMemory<FileEntity>> CheckFilesExistingAsync(ReadOnlyMemory<FileEntity> fileEntities)
        {
            if (fileEntities.IsEmpty)
            {
                return new(fileEntities);
            }

            var length = fileEntities.Length;
             
            FileEntity[] existingFiles = poolFileEntity.Rent(length);
            int existingFilesIndex = 0;
             
            FileEntity[] notExistingFiles = poolFileEntity.Rent(length);
            int notExistingFilesIndex = 0;

            try
            {

                int maxDegreeOfParallelism = Math.Max(Math.Min(fileEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                _ = Parallel.For(
                    0,
                    fileEntities.Length,
                    parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    (index) =>
                    {
                        var file = fileEntities.Span[index];
                        if (File.Exists(file.FilePhysicalFullPath))
                        {
                            existingFiles[Interlocked.Increment(ref existingFilesIndex) - 1] = file;
                        }
                        else
                        {
                            InformationFileMissing(file.FilePhysicalFullPath);
                            notExistingFiles[Interlocked.Increment(ref notExistingFilesIndex) - 1] = file;
                        }
                    }); 

                if (notExistingFilesIndex == 0)
                {
                    return new(fileEntities);
                }
                else
                {
                    return new(CheckFilesExistingAsyncCore(existingFiles, existingFilesIndex, notExistingFiles, notExistingFilesIndex));
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return new(fileEntities);
            }
            finally
            { 
                poolFileEntity.Return(existingFiles, clearArray: true);
                poolFileEntity.Return(notExistingFiles, clearArray: true);
            }
        }
        private async Task<ReadOnlyMemory<FileEntity>> CheckFilesExistingAsyncCore(
            FileEntity[] existingFiles,
            int existingFilesCount,
            FileEntity[] notExistingFiles,
            int notExistingFilesCount)
        {
            var actualNotExisting = notExistingFiles.AsSpan(0, notExistingFilesCount).ToArray();

            await ClearMetadataAsync(actualNotExisting);
            await ClearThumbnailsAsync(actualNotExisting, true);
            _ = await FileRepository.DeleteRangeAsync(actualNotExisting);

            return existingFiles.AsMemory(0, existingFilesCount);
        }
        public async Task CheckAllDirectoriesExistingAsync()
        {
            DirectoryRepository.DabataseClearChangeTracker();

            const int batchSize = 1000;
            int offset = 0;
            ReadOnlyMemory<DirectoryEntity> directoryEntities;
            while (true)
            {
                directoryEntities = await DirectoryRepository.GetAllAsync(offset, batchSize, withIncludes: false, useCachedResult: false);
                if (directoryEntities.IsEmpty)
                {
                    break;
                }

                _ = await CheckDirectoriesExistingAsync(directoryEntities);
                offset += directoryEntities.Length;
            }

            DirectoryRepository.DabataseClearChangeTracker();
        }
        public ValueTask<ReadOnlyMemory<DirectoryEntity>> CheckDirectoriesExistingAsync(ReadOnlyMemory<DirectoryEntity> directoryEntities)
        {
            if (directoryEntities.IsEmpty)
            {
                return new(directoryEntities);
            }

            var length = directoryEntities.Length;
             
            DirectoryEntity[] existingDirectories = poolDirectoryEntity.Rent(length);
            int existingDirectoriesIndex = 0;
             
            DirectoryEntity[] notExistingDirectories = poolDirectoryEntity.Rent(length);
            int notExistingDirectoriesIndex = 0;

            try
            {
                if (directoryEntities.IsEmpty)
                {
                    return new(directoryEntities);
                }

                var maxDegreeOfParallelism = Math.Max(Math.Min(directoryEntities.Length, (int)_serverConfig.ServerMaxDegreeOfParallelism), 1);

                _ = Parallel.For(
                    0,
                    directoryEntities.Length,
                    parallelOptions: new() { MaxDegreeOfParallelism = maxDegreeOfParallelism },
                    (index) =>
                    {
                        var directory = directoryEntities.Span[index];
                        if (Directory.Exists(directory.DirectoryFullPath))
                        {
                            existingDirectories[Interlocked.Increment(ref existingDirectoriesIndex) - 1] = directory;
                        }
                        else
                        {
                            InformationDirectoryMissing(directory.DirectoryFullPath);
                            notExistingDirectories[Interlocked.Increment(ref notExistingDirectoriesIndex) - 1] = directory;
                        }
                    }); 

                if (notExistingDirectoriesIndex == 0)
                {
                    return new(directoryEntities);
                }
                else
                {
                    return new(CheckDirectoriesExistingAsyncCore(existingDirectories, existingDirectoriesIndex, notExistingDirectories, notExistingDirectoriesIndex));
                }
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return new(directoryEntities);
            }
            finally
            { 
                poolDirectoryEntity.Return(existingDirectories, clearArray: true);
                poolDirectoryEntity.Return(notExistingDirectories, clearArray: true);
            }
        }
        private async Task<ReadOnlyMemory<DirectoryEntity>> CheckDirectoriesExistingAsyncCore(
            DirectoryEntity[] existingDirectories,
            int existingDirectoriesCount,
            DirectoryEntity[] notExistingDirectories,
            int notExistingDirectoriesCount)
        {
            List<DirectoryEntity> actualNotExisting = [.. notExistingDirectories[..notExistingDirectoriesCount]];

            var notExistingSubdirectories = (await DirectoryRepository
                .GetAllStartingByPathFullNamesAsync(
                    pathFullNames: actualNotExisting.Select(static (ned) => ned.DirectoryFullPath),
                    useCachedResult: false))
                .AsArray();
            if (notExistingSubdirectories.Length != 0)
            {
                foreach (var item in notExistingSubdirectories)
                {
                    actualNotExisting.Add(item);
                }
            }

            var removeFiles = (await FileRepository
                .GetAllByParentDirectoryIdsAsync(actualNotExisting.Select(static (ned) => ned.Id), [], useCachedResult: false))
                .AsArray();
            if (removeFiles.Length != 0)
            {
                _ = await CheckFilesExistingAsync(removeFiles);
            }

            _ = await DirectoryRepository.DeleteRangeAsync(actualNotExisting);

            return existingDirectories.AsMemory(0, existingDirectoriesCount);
        }
        public async Task<(ReadOnlyMemory<FileEntity> fileEntities, ReadOnlyMemory<DirectoryEntity> directoryEntities, bool isRootFolder, uint totalMatches)> GetBrowseResultItems(
            string objectID,
            int startingIndex,
            int requestedCount
            )
        {
            var startTime = DateTime.Now;
            var stopwatch = Stopwatch.StartNew();

            var directoryStartObject = await DirectoryRepository.GetByIdAsync(objectID, asNoTracking: true, useCachedResult: true);
            var getDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;

            bool isRootFolder = directoryStartObject == null;
            var getAllFilesInDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;
            var refreshFoundFilesTime = stopwatch.Elapsed.TotalMilliseconds;
            if (!isRootFolder)
            {
                // refresh directory for added files
                var inputFiles = GetAllFilesInFolders([directoryStartObject!.DirectoryFullPath], true);
                getAllFilesInDirectoryTime = stopwatch.Elapsed.TotalMilliseconds;
                await RefreshFoundFilesAsync(inputFiles, false);
                refreshFoundFilesTime = stopwatch.Elapsed.TotalMilliseconds;
            }


            // not possible to pagination for possibility of removed files / directories
            (var fileEntities, var directoryEntities) = await GetFilesAndDirectoriesAsync(objectID);
            var getEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            // sorting before checking root folder, as for sorting additional files in root folder is by timestamps
            fileEntities = fileEntities.AsArray().OrderBy(static (f) => f.LC_Title).ToArray();
            directoryEntities = directoryEntities.AsArray().OrderBy(static (d) => d.LC_Directory).ToArray();

            if (isRootFolder)
            {
                (var fileEntitiesAdded, var directoryEntitiesAdded) = await GetFilesByLastAddedToDbAsync(_serverConfig.CountOfFilesByLastAddedToDb);

                fileEntities = fileEntities.AsArray().Concat(fileEntitiesAdded.AsArray()).ToArray();
                directoryEntities = directoryEntities.AsArray().Concat(directoryEntitiesAdded.AsArray()).ToArray();
            }
            var addAdditionalEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            uint totalMatches = _serverConfig.ServerIgnoreRequestedCountAttributeFromRequest
                ? (uint)(fileEntities.Length + directoryEntities.Length)
                : FilterEntities(startingIndex, requestedCount, ref fileEntities, ref directoryEntities);
            var filterEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            // possible to return less objects with this checking, but client will request rest of them in next request
            var countBeforeCheck = fileEntities.Length + directoryEntities.Length;

            fileEntities = await CheckFilesExistingAsync(fileEntities);
            directoryEntities = await CheckDirectoriesExistingAsync(directoryEntities);
            var checkEntitiesTime = stopwatch.Elapsed.TotalMilliseconds;

            await MediaProcessingService.FillEmptyInfoAsync(fileEntities.AsArray(), setCheckedForFailed: true);
            var fillEmptyData = stopwatch.Elapsed.TotalMilliseconds;
            var endTime = DateTime.Now;

            if (countBeforeCheck != (fileEntities.Length + directoryEntities.Length))
            {
                WarningObjectsRemovedFromDirectory(
                    objectID,
                    directoryStartObject?.DirectoryFullPath,
                    countBeforeCheck,
                    fileEntities.Length + directoryEntities.Length);
            }

            if (_serverConfig.ServerShowDurationDetailsBrowseRequest)
            {
                InformationBrowseDetailInfo(
                    objectID: objectID,
                    startTime: startTime,
                    endTime: endTime,
                    getDirectory: getDirectoryTime,
                    getFilesInDirectory: getAllFilesInDirectoryTime - getDirectoryTime,
                    refreshFoundFilesInDirectory: refreshFoundFilesTime - getAllFilesInDirectoryTime,
                    getDataFromDatabase: getEntitiesTime - refreshFoundFilesTime,
                    addAdditionalDataFromDatabase: addAdditionalEntitiesTime - getEntitiesTime,
                    filterData: filterEntitiesTime - addAdditionalEntitiesTime,
                    checkFiles: checkEntitiesTime - filterEntitiesTime,
                    fillEmptyData: fillEmptyData - checkEntitiesTime,
                    totalDuration: fillEmptyData,
                    directory: directoryStartObject?.DirectoryFullPath
                    );
            }

            return (fileEntities.ToArray(), directoryEntities.ToArray(), isRootFolder, totalMatches);
        }

        private static uint FilterEntities(int startingIndex, int requestedCount, ref ReadOnlyMemory<FileEntity> fileEntities, ref ReadOnlyMemory<DirectoryEntity> directoryEntities)
        {
            var directoryCount = directoryEntities.Length;
            var fileCount = fileEntities.Length;

            uint totalMatches = (uint)(directoryCount + fileCount);

            directoryEntities = directoryEntities.AsArray().Skip(startingIndex).Take(requestedCount).ToArray();
            if (directoryCount == 0)
            {
                fileEntities = fileEntities.AsArray().Skip(startingIndex).Take(requestedCount).ToArray();
            }
            else if (directoryEntities.Length < (requestedCount))
            {
                fileEntities = fileEntities.AsArray().Skip(startingIndex - directoryCount).Take(requestedCount - directoryEntities.Length).ToArray();
            }
            else
            {
                fileEntities = ReadOnlyMemory<FileEntity>.Empty;
            }

            return totalMatches;
        }
        public async Task ClearAllThumbnailsAsync(bool deleteThumbnailFile = true)
        {
            const int maxChunkSize = 1000;

            int offset = 0;

            FileEntity[] files;
            while (true)
            {
                files = (await FileRepository.GetAllAsync(offset, maxChunkSize, withIncludes: false, useCachedResult: false)).AsArray();
                if (files.Length == 0)
                {
                    break; // Stop if no more files
                }

                await ClearThumbnailsAsync(files, deleteThumbnailFile);
                Array.Clear(files);
                offset += files.Length;
            }

            FileRepository.DabataseClearChangeTracker();
        }
        public async Task ClearThumbnailsAsync(IEnumerable<FileEntity> files, bool deleteThumbnailFile = true)
        {
            if (deleteThumbnailFile)
            { 
                Partitioner.Create(files)
                    .AsParallel()
                    .Where(static (f) => f != null
                        && !string.IsNullOrEmpty(f.Thumbnail?.ThumbnailFilePhysicalFullPath))
                    .Select(static (f) => f.Thumbnail!.ThumbnailFilePhysicalFullPath)
                    .Where(File.Exists)
                    .ForAll(File.Delete);
            }

            var filesProperty = files.Select(static (f) => f.FilePhysicalFullPath);

            _ = await ThumbnailRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.ThumbnailDataId, static (fp) => null),
                reloadTrackedEntities: false);
            _ = await FileRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.IsThumbnailChecked, static (fp) => false)
                    .SetProperty(static (fp) => fp.ThumbnailId, static (fp) => null),
                reloadTrackedEntities: false);

            _ = await ThumbnailDataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await ThumbnailRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);

            files.AsParallel().ForAll(static (fe) =>
            {
                fe.IsThumbnailChecked = false;
                fe.ThumbnailId = null;
                fe.Thumbnail = null;
            });

            _ = await FileRepository.DbContext.Database.ExecuteSqlRawAsync("VACUUM;");
        }
        public async Task ClearAllMetadataAsync()
        {
            const int maxChunkSize = 1000;

            int offset = 0;
            FileEntity[] files;
            while (true)
            {
                files = (await FileRepository.GetAllAsync(offset, maxChunkSize, withIncludes: false, useCachedResult: false)).AsArray();
                if (files.Length == 0)
                {
                    break; // Stop if no more files
                }

                await ClearMetadataAsync(files);
                Array.Clear(files);
                offset += files.Length;
            }

            FileRepository.DabataseClearChangeTracker();
        }
        public async Task ClearMetadataAsync(IEnumerable<FileEntity> files)
        {
            var filesProperty = files.Select(static (f) => f.FilePhysicalFullPath);

            _ = await FileRepository.ExecuteUpdateAsync(
                predicate: fe => filesProperty.Contains(fe.FilePhysicalFullPath),
                setPropertyCalls: static (fe) => fe
                    .SetProperty(static (fp) => fp.IsMetadataChecked, static (fp) => false)
                    .SetProperty(static (fp) => fp.AudioMetadataId, static (fp) => null)
                    .SetProperty(static (fp) => fp.VideoMetadataId, static (fp) => null)
                    .SetProperty(static (fp) => fp.SubtitleMetadataId, static (fp) => null),
                reloadTrackedEntities: false);

            _ = await AudioMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await SubtitleMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);
            _ = await VideoMetadataRepository.ExecuteDeleteAsync(
                predicate: md => filesProperty.Contains(md.FilePhysicalFullPath),
                reloadTrackedEntities: false);

            files.AsParallel().ForAll(static (fe) =>
            {
                fe.IsMetadataChecked = false;
                fe.AudioMetadataId = null;
                fe.VideoMetadataId = null;
                fe.SubtitleMetadataId = null;
                fe.AudioMetadata = null;
                fe.VideoMetadata = null;
                fe.SubtitleMetadata = null;
            });

            _ = await FileRepository.DbContext.Database.ExecuteSqlRawAsync("VACUUM;");
        }
    }
}
