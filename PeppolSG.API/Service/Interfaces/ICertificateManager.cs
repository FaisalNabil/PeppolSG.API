using System.Security.Cryptography.X509Certificates;

namespace PeppolSG.API.Service.Interfaces
{
    public interface ICertificateManager
    {
        X509Certificate2 LoadSigningCertificate();
        bool ValidateCertificate(X509Certificate2 certificate);
    }
} 