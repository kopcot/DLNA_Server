namespace DLNAServer.Features.MediaProcessors
{
    public partial class FFmpegService
    {
        [LoggerMessage(1, LogLevel.Error, "{message}")]
        partial void LogErrorFFmpegGetMediaInfo(string message);
    }
}
