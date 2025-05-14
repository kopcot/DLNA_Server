namespace DLNAServer.Controllers.Manage
{
    public partial class ManageController
    {
        [LoggerMessage(1, LogLevel.Information, "Starting to refresh information for chunk {indexChunk} of {totalChunks}. Total files to process: {fileCountAll}.")]
        partial void InformationStartRefreshingInfoChunk(int indexChunk, int totalChunks, long fileCountAll);
        [LoggerMessage(2, LogLevel.Information, "Checking files and directories existence completed.")]
        partial void InformationDoneCheckingFilesAndDirectories();
        [LoggerMessage(3, LogLevel.Debug, "Starting to recreate information for chunk {indexChunk} of {totalChunks}. Total files to recreate: {fileCountAll}.")]
        partial void DebugStartRecreatingInfoChunk(int indexChunk, int totalChunks, long fileCountAll);
        [LoggerMessage(4, LogLevel.Information, "Clearing thumbnails and metadata information completed.")]
        partial void InformationDoneClearingInfo();
        [LoggerMessage(5, LogLevel.Debug, "Recreating information completed for chunk {indexChunk} of {totalChunks}.")]
        partial void DebugDoneRecreatingInfoChunk(int indexChunk, int totalChunks);
        [LoggerMessage(6, LogLevel.Information, "Recreate info done")]
        partial void InformationDoneRecreatingInfo();
        [LoggerMessage(7, LogLevel.Information, "Completed refreshing information for chunk {indexChunk} out of {totalChunks}. Duration: {durationInSec,6:0.00} seconds. Processed {fileCountChunk} files in this chunk, with a total of {fileCountAll} files processed.")]
        partial void InformationDoneRefreshingInfoChunk(int indexChunk, int totalChunks, int fileCountChunk, double durationInSec, long fileCountAll);

        private static readonly Func<ILogger, IDisposable?> _logScopeRecreatingFilesInfo =
            LoggerMessage.DefineScope(
                "Recreating all files info");
        public static IDisposable? ScopeRecreatingFilesInfo(
            ILogger logger)
        {
            return _logScopeRecreatingFilesInfo(logger);
        }

        private static readonly Func<ILogger, IDisposable?> _logScopeRecreatingFilesInfoChunk =
            LoggerMessage.DefineScope(
                "Recreating files info - chunk");
        public static IDisposable? ScopeRecreatingFilesInfoChunk(
            ILogger logger)
        {
            return _logScopeRecreatingFilesInfoChunk(logger);
        }
    }
}
