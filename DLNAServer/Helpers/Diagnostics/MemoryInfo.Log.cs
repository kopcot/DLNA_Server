namespace DLNAServer.Helpers.Diagnostics
{
    public partial class MemoryInfo
    {
        private static readonly Action<ILogger, string, int, double, double, double, double, Exception?> _logMemoryInfo =
        LoggerMessage.Define<string, int, double, double, double, double>(
            LogLevel.Information,
            new EventId(1, "FFmpegGetMediaInfo"),
            "Memory log [{methodName}:{lineNumber}]\n" +
            "Allocated: {allocatedInMB:0.00} MB\n" +
            "Heap size bytes: {heapSizeBytesInMB:0.00} MB\n" +
            "Working set 64-bit: {workingSet64InMB:0.00} MB\n" +
            "Private memory size 64-bit: {privateMemorySize64InMB:0.00} MB");
        private static void LogMemoryInfo(
            ILogger logger,
            string methodName,
            int lineNumber,
            double allocatedInMB,
            double heapSizeBytesInMB,
            double workingSet64InMB,
            double privateMemorySize64InMB)
        {
            _logMemoryInfo(logger, methodName, lineNumber, allocatedInMB, heapSizeBytesInMB, workingSet64InMB, privateMemorySize64InMB, null);
        }
    }
}
