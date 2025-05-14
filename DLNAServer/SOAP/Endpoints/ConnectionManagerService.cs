using DLNAServer.Helpers.Logger;
using DLNAServer.SOAP.Endpoints.Interfaces;
using DLNAServer.SOAP.Endpoints.Responses.ConnectionManager;

namespace DLNAServer.SOAP.Endpoints
{
    public partial class ConnectionManagerService : IConnectionManagerService
    {
        private readonly ILogger<ConnectionManagerService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        public ConnectionManagerService(
            ILogger<ConnectionManagerService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        public ConnectionComplete ConnectionComplete(int connectionID)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(ConnectionComplete),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            WarningConnectionCompleteRequestInfo(nameof(ConnectionComplete), connectionID);

            return new() { };
        }
        public GetCurrentConnectionIDs GetCurrentConnectionIDs()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetCurrentConnectionIDs),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(GetCurrentConnectionIDs));

            return new()
            {
                ConnectionID = "0"
            };
        }

        public GetCurrentConnectionInfo GetCurrentConnectionInfo(int connectionID)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetCurrentConnectionInfo),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            WarningGetCurrentConnectionInfoRequestInfo(nameof(GetCurrentConnectionInfo), connectionID);

            return new()
            {
                AVTransportID = 0,
                Direction = "0",
                RcsID = 0,
                PeerConnectionID = "0",
                PeerConnectionManager = 0,
                ProtocolInfo = "0",
                Status = "0",
            };
        }

        public GetProtocolInfo GetProtocolInfo()
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(GetProtocolInfo),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            _logger.LogGeneralWarningMessage(nameof(GetProtocolInfo));

            return new()
            {
                Sink = "",
                Source = ""
            };
        }

        public PrepareForConnection PrepareForConnection(string remoteProtocolInfo, string peerConnectionManager, int peerConnectionID, string direction)
        {
            var connection = _httpContextAccessor.HttpContext?.Connection;
            LoggerHelper.LogDebugConnectionInformation(
                _logger,
                nameof(PrepareForConnection),
                connection?.RemoteIpAddress,
                connection?.RemotePort,
                connection?.LocalIpAddress,
                connection?.LocalPort,
                _httpContextAccessor.HttpContext?.Request.Path.Value,
                _httpContextAccessor.HttpContext?.Request.Method);
            // not found operation in real usage
            WarningGetCurrentConnectionInfoRequestInfo(
                nameof(PrepareForConnection),
                remoteProtocolInfo,
                peerConnectionManager,
                peerConnectionID,
                direction
                );

            return new()
            {
                AVTransportID = "0",
                ConnectionID = "0",
                RcsID = "0"
            };
        }
    }
}
