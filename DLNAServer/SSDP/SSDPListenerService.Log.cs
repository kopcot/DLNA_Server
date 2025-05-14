using System.Net;

namespace DLNAServer.SSDP
{
    public partial class SSDPListenerService
    {
        [LoggerMessage(1, LogLevel.Information, "Starting SSDP listeners...")]
        partial void InformationStarting();
        [LoggerMessage(2, LogLevel.Information, "Stopping SSDP listeners...")]
        partial void InformationStopping();
        [LoggerMessage(3, LogLevel.Debug, "Listening for M-SEARCH requests...")]
        partial void DebugStartedListening();
        [LoggerMessage(4, LogLevel.Debug, "Listening for M-SEARCH requests... (Cancellation requested)")]
        partial void DebugListeningCancelationRequest();
        [LoggerMessage(5, LogLevel.Debug, "Sent SSDP response to {remoteAddress}:{remotePort}; {uri}; {usn}")]
        partial void DebugSendResponse(IPAddress remoteAddress, int remotePort, Uri uri, string usn);
        [LoggerMessage(6, LogLevel.Warning, "Error in Sending message. SSDP Listener service stopped for {retryDelayInMins,6:0.00} min")]
        partial void WarningStopNotifySend(double retryDelayInMins);
        [LoggerMessage(7, LogLevel.Error, "An error occurred when attempting to access the socket. '{errorMessage}'")]
        partial void ErrorSocketException(string errorMessage);
    }
}
