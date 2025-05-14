using System.Net;

namespace DLNAServer.SSDP
{
    public partial class SSDPNotifierService
    {
        [LoggerMessage(1, LogLevel.Information, "Starting SSDP notifiers for {endpoint}...")]
        partial void InformationStarting(IPEndPoint endpoint);
        [LoggerMessage(2, LogLevel.Information, "Stopping SSDP notifiers...")]
        partial void InformationStopping();
        [LoggerMessage(3, LogLevel.Debug, "SSDP stop NOTIFY message sent.")]
        partial void DebugStopNotifySend();
        [LoggerMessage(4, LogLevel.Warning, "Error in Sending message. SSDP Notifier service stopped for {retryDelayInMins,6:0.00} min")]
        partial void WarningStopNotifySend(double retryDelayInMins);
        [LoggerMessage(5, LogLevel.Error, "An error occurred when attempting to access the socket. '{errorMessage}'")]
        partial void ErrorSocketException(string errorMessage);

    }
}
