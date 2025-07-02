using System.Security.Cryptography.X509Certificates;
using PeppolSG.API.Service;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IPeppolConfigurationService
    {
        string PeppolP12FilePath { get; }
        string PeppolP12Password { get; }
        string PeppolCertificateThumbprint { get; }
        string PeppolDomain { get; }
        string PeppolAccessPointId { get; }
        string PeppolPartyType { get; }
        string SmpDomain { get; }
        string SmlDomain { get; }
        bool UsePeppolTestNetwork { get; }
        string OneWayMEP { get; }
        string PushMEPBinding { get; }
        string PeppolAgreement { get; }
        string InitiatorRole { get; }
        string ResponderRole { get; }
        string DefaultMPC { get; }
        string SignatureAlgorithm { get; }
        string HashFunction { get; }
        string EncryptionAlgorithm { get; }
        string KeyTransportAlgorithm { get; }
        bool EnableSignature { get; }
        bool EnableEncryption { get; }
        bool ReceptionAwareness { get; }
        int RetryCount { get; }
        int RetryInterval { get; }
        bool DuplicateDetection { get; }
        X509Certificate2 LoadSigningCertificate();
        PeppolPModeConfiguration GetPModeConfiguration(string service, string action, string fromParty, string toParty);
        string GetPeppolDomain();
        string GetSigningCertificatePath();
        string GetSigningCertificatePassword();
        bool IsDebugMode();
    }
} 