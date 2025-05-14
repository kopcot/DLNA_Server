using DLNAServer.SOAP.Constants;
using DLNAServer.SOAP.Endpoints.Responses.ConnectionManager;
using System.ServiceModel;

namespace DLNAServer.SOAP.Endpoints.Interfaces
{
    [ServiceContract(Namespace = Services.ServiceType.ConnectionManager)]
    public interface IConnectionManagerService
    {
        [OperationContract(Name = "GetProtocolInfo")]
        GetProtocolInfo GetProtocolInfo();
        [OperationContract(Name = "GetCurrentConnectionIDs")]
        GetCurrentConnectionIDs GetCurrentConnectionIDs();
        [OperationContract(Name = "PrepareForConnection")]
        PrepareForConnection PrepareForConnection(string remoteProtocolInfo, string peerConnectionManager, int peerConnectionID, string direction);
        [OperationContract(Name = "GetCurrentConnectionInfo")]
        GetCurrentConnectionInfo GetCurrentConnectionInfo(int connectionID);
        [OperationContract(Name = "ConnectionComplete")]
        ConnectionComplete ConnectionComplete(int connectionID);
    }
}
