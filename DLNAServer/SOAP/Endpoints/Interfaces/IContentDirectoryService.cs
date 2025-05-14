using DLNAServer.SOAP.Constants;
using DLNAServer.SOAP.Endpoints.Responses.ContentDirectory;
using System.ServiceModel;

namespace DLNAServer.SOAP.Endpoints.Interfaces
{
    [ServiceContract(Namespace = Services.ServiceType.ContentDirectory)]
    public interface IContentDirectoryService
    {
        [OperationContract(Name = "Browse")]
        Task<Browse> Browse(string ObjectID, string BrowseFlag, string Filter, int StartingIndex, int RequestedCount, string SortCriteria);

        [OperationContract(Name = "GetSearchCapabilities", AsyncPattern = true)]
        GetSearchCapabilities GetSearchCapabilities();

        [OperationContract(Name = "GetSortCapabilities")]
        GetSortCapabilities GetSortCapabilities();

        [OperationContract(Name = "IsAuthorized")]
        IsAuthorized IsAuthorized(string DeviceID);

        [OperationContract(Name = "GetSystemUpdateID")]
        GetSystemUpdateID GetSystemUpdateID();

        [OperationContract(Name = "X_GetFeatureList")]
        X_GetFeatureList X_GetFeatureList();

        [OperationContract(Name = "X_SetBookmark")]
        X_SetBookmark X_SetBookmark(int CategoryType, int RID, string ObjectID, int PosSecond);
    }
}
