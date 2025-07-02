using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IPeppolAs4Signer
    {
        void SignEnvelope(XDocument envelopeXml, X509Certificate2 signingCert, string bstId, string messagingId, string bodyId, string attachmentCid = null, byte[] encryptedAttachment = null);
        bool VerifyMessageSignature(XDocument soapEnvelope, X509Certificate2 senderCertificate);
        bool VerifyTimestamp(XDocument soapEnvelope);
    }
} 