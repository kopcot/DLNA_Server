using DLNAServer.SOAP.Constants;
using DLNAServer.SOAP.Endpoints.Responses.AVTransport;
using System.ServiceModel;

namespace DLNAServer.SOAP.Endpoints.Interfaces
{
    [ServiceContract(Namespace = Services.ServiceType.AVTransport)]
    public interface IAVTransportService
    {
        [OperationContract(Name = "SetAVTransportURI")]
        SetAVTransportURI SetAVTransportURI(int InstanceID, string CurrentURI, string CurrentURIMetaData);
        [OperationContract(Name = "Play")]
        Play Play(int InstanceID, string Speed);
        [OperationContract(Name = "Pause")]
        Pause Pause(int InstanceID);
        [OperationContract(Name = "Stop")]
        Stop Stop(int InstanceID);
        [OperationContract(Name = "Seek")]
        Seek Seek(int InstanceID, string SeekMode, string Target);
        [OperationContract(Name = "GetTransportInfo")]
        GetTransportInfo GetTransportInfo(int InstanceID);
        [OperationContract(Name = "GetPositionInfo")]
        GetPositionInfo GetPositionInfo(int InstanceID);

    }
}
