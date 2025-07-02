using PeppolSG.API.Models;
using System.Threading.Tasks;

namespace PeppolSG.API.Service.Interfaces
{
    public interface ISmkSmpLookupService
    {
        Task<SmpEndpoint> LookupEndpointMetadata(string participantId, string participantScheme, string documentTypeId, string processId);
    }
} 