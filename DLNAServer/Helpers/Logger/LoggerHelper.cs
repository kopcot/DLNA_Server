using System.Net;
using System.Runtime.CompilerServices;

namespace DLNAServer.Helpers.Logger
{
    /// <summary>
    /// Provides extension methods for logging general messages at various log levels and standalone methods for common messages.<br />
    /// Use the <see cref="LoggerMessage"/> delegates instead of calling <see cref="LoggerExtensions"/>
    /// </summary>
    public static class LoggerHelper
    {
        #region General messages - Extension methods
        private static readonly Action<ILogger, string, Exception?> _logTraceMessage =
        LoggerMessage.Define<string>(
            LogLevel.Trace,
            new EventId((int)LogLevel.Trace * -1, "GeneralTrace"),
            "{Message}");
        public static void LogGeneralTraceMessage(this ILogger logger, string message)
        {
            _logTraceMessage(logger, message, null);
        }

        private static readonly Action<ILogger, string, Exception?> _logDebugMessage =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId((int)LogLevel.Debug * -1, "GeneralDebug"),
            "{Message}");
        public static void LogGeneralDebugMessage(this ILogger logger, string message)
        {
            _logDebugMessage(logger, message, null);
        }

        private static readonly Action<ILogger, string, Exception?> _logInformationMessage =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId((int)LogLevel.Information * -1, "GeneralInformation"),
            "{Message}");
        public static void LogGeneralInformationMessage(this ILogger logger, string message)
        {
            _logInformationMessage(logger, message, null);
        }

        private static readonly Action<ILogger, string, Exception?> _logWarningMessage =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId((int)LogLevel.Warning * -1, "GeneralWarning"),
            "{Message}");
        public static void LogGeneralWarningMessage(this ILogger logger, string message)
        {
            _logWarningMessage(logger, message, null);
        }

        private static readonly Action<ILogger, string, string?, string, int, string?, Exception?> _logErrorMessageEx =
        LoggerMessage.Define<string, string?, string, int, string?>(
            LogLevel.Error,
            new EventId((int)LogLevel.Error * -1, "GeneralError"),
            "An error occurred: {Message}\n{AdditionalMessage}\n{MethodName}:{LineNumber}\n{StackTrace}");
        private static readonly Action<ILogger, string, string, int, string?, Exception?> _logErrorMessage =
        LoggerMessage.Define<string, string, int, string?>(
            LogLevel.Error,
            new EventId((int)LogLevel.Error * -1, "GeneralError"),
            "An error occurred: {Message}\n{MethodName}:{LineNumber}\n{StackTrace}");
        public static void LogGeneralErrorMessage(this ILogger logger, Exception ex, string? additionalMessage = null, [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string methodName = "")
        {
            if (string.IsNullOrWhiteSpace(additionalMessage))
            {
                _logErrorMessage(logger, ex.Message, methodName, lineNumber, ex.StackTrace, ex);
            }
            else
            {
                _logErrorMessageEx(logger, ex.Message, additionalMessage, methodName, lineNumber, ex.StackTrace, ex);
            }
        }

        private static readonly Action<ILogger, string, Exception?> _logCriticalMessage =
        LoggerMessage.Define<string>(
            LogLevel.Critical,
            new EventId((int)LogLevel.Critical * -1, "GeneralCritical"),
            "{Message}");
        public static void LogGeneralCriticalMessage(this ILogger logger, string message)
        {
            _logCriticalMessage(logger, message, null);
        }
        #endregion General messages
        #region Common messages - Standalone method
        private static readonly Action<ILogger, string, string, string, string?, string?, Exception?> _logConnectionInformation =
        LoggerMessage.Define<string, string, string, string?, string?>(
            LogLevel.Debug,
            new EventId(1, "ConnectionInformation"),
            "{action}, remote address {remoteIpAddress}, local address {localIpAddress}, path: '{path}',  method: '{method}'");
        public static void LogDebugConnectionInformation(
            ILogger logger,
            string action,
            IPAddress? remoteIpAddress,
            int? remotePort,
            IPAddress? localIpAddress,
            int? localPort,
            string? path = "",
            string? method = "")
        {
            _logConnectionInformation(logger, action, $"{remoteIpAddress}:{remotePort}", $"{localIpAddress}:{localPort}", path, method, null);
        }

        private static readonly Action<ILogger, string, string, string, string?, string, Exception?> _logWarningFallbackError =
        LoggerMessage.Define<string, string, string, string?, string>(
            LogLevel.Warning,
            new EventId(2, "ConnectionInformation"),
            "FallbackError: {action}, remote address {remoteIpAddress}, local address {localIpAddress}, path: '{path}',  method: '{method}'");
        public static void LogWarningFallbackError(
            ILogger logger,
            string action,
            IPAddress? remoteIpAddress,
            int remotePort,
            IPAddress? localIpAddress,
            int localPort,
            string? path = "",
            string method = "")
        {
            _logWarningFallbackError(logger, action, $"{remoteIpAddress}:{remotePort}", $"{localIpAddress}:{localPort}", path, method, null);
        }

        private static readonly Action<ILogger, Exception?> _logWarningOperationCanceled =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3, "OperationCanceled"),
            "Operation canceled");
        public static void LogWarningOperationCanceled(
            ILogger logger)
        {
            _logWarningOperationCanceled(logger, null);
        }

        private static readonly Action<ILogger, Exception?> _logWarningTaskCanceled =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(4, "TaskCanceled"),
            "Task canceled");
        public static void LogWarningTaskCanceled(
            ILogger logger)
        {
            _logWarningTaskCanceled(logger, null);
        }

        private static readonly Action<ILogger, Exception?> _logInformationRestartCommand =
        LoggerMessage.Define(
            LogLevel.Information,
            new EventId(5, "RestartCommand"),
            "Sending restart command");
        public static void InformationSendingRestart(
            ILogger logger)
        {
            _logInformationRestartCommand(logger, null);
        }

        #endregion Common messages
    }
}
