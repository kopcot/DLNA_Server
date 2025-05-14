namespace DLNAServer.Features.MediaContent
{
    public partial class ContentExplorerManager
    {
        [LoggerMessage(1, LogLevel.Information, "Refreshed {directoriesCount} directories and {filesCount} files.")]
        partial void InformationRefreshedInfo(int directoriesCount, int filesCount);
        [LoggerMessage(2, LogLevel.Warning, "Directory not exists: {sourceFolder}")]
        partial void WarningDirectoryNotExists(string sourceFolder);
        [LoggerMessage(3, LogLevel.Information, "Total adding {directoriesCount} directory(ies) and {filesCount} file(s)")]
        partial void InformationTotalAdding(int directoriesCount, int filesCount);
        [LoggerMessage(4, LogLevel.Information, "Directories:\n{directories}{moreDirectories}")]
        partial void InformationDirectoriesCount(string directories, string moreDirectories);
        [LoggerMessage(5, LogLevel.Information, "Files:\n{files}{moreFiles}")]
        partial void InformationFilesCount(string files, string moreFiles);
        [LoggerMessage(6, LogLevel.Information, "Directory '{directory}' is without parent")]
        partial void DebugDirectoryWithoutParent(string directory);
        [LoggerMessage(7, LogLevel.Information, "File missing {file}")]
        partial void InformationFileMissing(string file);
        [LoggerMessage(8, LogLevel.Information, "Directory missing {directory}")]
        partial void InformationDirectoryMissing(string directory);
        [LoggerMessage(9, LogLevel.Information, "Cached keys:\n{keys}")]
        partial void InformationCachedKeys(string keys);
        [LoggerMessage(10, LogLevel.Warning, "Object Id: '{objectID}'. Some object was removed for directory {directoryFullPath}. " +
            "Object in database = {countBeforeCheck}. " +
            "Objects after check = {countAfterCheck}")]
        partial void WarningObjectsRemovedFromDirectory(string objectID, string? directoryFullPath, int countBeforeCheck, int countAfterCheck);
        [LoggerMessage(11, LogLevel.Information, "Object Id: '{objectID}'. " +
            "Start: {startTime:HH:mm:ss:fff}, " +
            "End: {endTime:HH:mm:ss:fff}, " +
            "Get directory by ObjectID: {getDirectory,6:0.00}(ms), " +
            "Get files in directory: {getFilesInDirectory,6:0.00}(ms), " +
            "Refresh found files from directory: {refreshFoundFilesInDirectory,6:0.00}(ms), " +
            "Get data from database: {getDataFromDatabase,6:0.00}(ms), " +
            "Add additional data from database: {addAdditionalDataFromDatabase,6:0.00}(ms), " +
            "Filter data: {filterData,6:0.00}(ms), " +
            "Check files: {checkFiles,6:0.00}(ms), " +
            "Fill empty data: {fillEmptyData,6:0.00}(ms), " +
            "Total duration (ms): {totalDuration,6:0.00}(ms), " +
            "Directory: {directory}")]
        partial void InformationBrowseDetailInfo(
            string objectID,
            DateTime startTime,
            DateTime endTime,
            double getDirectory,
            double getFilesInDirectory,
            double refreshFoundFilesInDirectory,
            double getDataFromDatabase,
            double addAdditionalDataFromDatabase,
            double filterData,
            double checkFiles,
            double fillEmptyData,
            double totalDuration,
            string? directory);
        [LoggerMessage(10, LogLevel.Warning, "No any thumbnail file stored path full name in database. No thumbnail file will be deleted. File in database checked: {fileEntityCount}")]
        partial void WarningNoAnyThumbnailFile(int fileEntityCount);
    }
}
