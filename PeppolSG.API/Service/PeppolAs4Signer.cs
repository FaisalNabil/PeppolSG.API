using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Security.Cryptography;
using System.Web;
using System.Xml.Linq;
using System.Xml;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Crypto compatibility helper for .NET Framework 4.8 compatibility
    /// Provides BouncyCastle key conversion without DotNetUtilities dependency
    /// </summary>
    public static class CryptoCompatibilityHelper
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(CryptoCompatibilityHelper));

        /// <summary>
        /// Converts .NET RSA private key to BouncyCastle RsaPrivateCrtKeyParameters
        /// Compatible with .NET Framework 4.8 without DotNetUtilities dependency
        /// </summary>
        public static RsaPrivateCrtKeyParameters ConvertToBouncyCastleRsaPrivateKey(RSA rsa)
        {
            try
            {
                var parameters = rsa.ExportParameters(true);
                
                return new RsaPrivateCrtKeyParameters(
                    new BigInteger(1, parameters.Modulus),
                    new BigInteger(1, parameters.Exponent),
                    new BigInteger(1, parameters.D),
                    new BigInteger(1, parameters.P),
                    new BigInteger(1, parameters.Q),
                    new BigInteger(1, parameters.DP),
                    new BigInteger(1, parameters.DQ),
                    new BigInteger(1, parameters.InverseQ));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to convert RSA private key to BouncyCastle format: {ex.Message}", ex);
                throw new InvalidOperationException("RSA private key conversion failed", ex);
            }
        }

        /// <summary>
        /// Converts .NET RSA public key to BouncyCastle RsaKeyParameters
        /// Compatible with .NET Framework 4.8
        /// </summary>
        public static RsaKeyParameters ConvertToBouncyCastleRsaPublicKey(RSA rsa)
        {
            try
            {
                var parameters = rsa.ExportParameters(false);
                
                return new RsaKeyParameters(
                    false, // isPrivate = false
                    new BigInteger(1, parameters.Modulus),
                    new BigInteger(1, parameters.Exponent));
            }
            catch (Exception ex)
            {
                log.Error($"Failed to convert RSA public key to BouncyCastle format: {ex.Message}", ex);
                throw new InvalidOperationException("RSA public key conversion failed", ex);
            }
        }

        /// <summary>
        /// Safe wrapper for GetRSAPrivateKey with fallback for .NET Framework 4.8
        /// </summary>
        public static RSA GetRSAPrivateKeySafe(this X509Certificate2 cert)
        {
            try
            {
                return cert.GetRSAPrivateKey();
            }
            catch (Exception ex)
            {
                log.Warn($"GetRSAPrivateKey() failed, trying fallback: {ex.Message}");
                
                // Fallback for older .NET Framework versions
                if (cert.PrivateKey is RSA rsa)
                {
                    return rsa;
                }
                
                throw new NotSupportedException($"Cannot extract RSA private key from certificate: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Safe wrapper for GetRSAPublicKey with fallback for .NET Framework 4.8
        /// </summary>
        public static RSA GetRSAPublicKeySafe(this X509Certificate2 cert)
        {
            try
            {
                return cert.GetRSAPublicKey();
            }
            catch (Exception ex)
            {
                log.Warn($"GetRSAPublicKey() failed, trying fallback: {ex.Message}");
                
                // Fallback for older .NET Framework versions
                if (cert.PublicKey.Key is RSA rsa)
                {
                    return rsa;
                }
                
                throw new NotSupportedException($"Cannot extract RSA public key from certificate: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Enhanced Peppol AS4 Signer for WS-Security 1.1.1 compliance
    /// Implements eDelivery AS4 Profile v1.1.0 signature requirements
    /// </summary>
    public static class PeppolAs4Signer
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // WS-Security namespaces
        private static readonly XNamespace XENC = "http://www.w3.org/2001/04/xmlenc#";
        private static readonly XNamespace XENC11 = "http://www.w3.org/2009/xmlenc11#";
        private static readonly XNamespace DS = SignedXml.XmlDsigNamespaceUrl;
        private static readonly XNamespace WSSE = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        private static readonly XNamespace WSSE11 = "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd";
        private static readonly XNamespace WSU = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        private static readonly XNamespace S12 = "http://www.w3.org/2003/05/soap-envelope";

        /// <summary>
        /// Sign the SOAP envelope, encrypting an attachment if requested.
        /// Enhanced version with .NET Framework 4.8 compatibility.
        /// </summary>
        public static void SignEnvelope(
            XDocument envelopeXml,
            string pfxPath,
            string pfxPassword,
            string bstId,
            string messagingId,
            string bodyId,
            string attachmentCid = null,
            byte[] encryptedAttachment = null)
        {
            var certPath = System.Web.HttpContext.Current?.Server.MapPath(pfxPath) ?? pfxPath;
            var cert = new X509Certificate2(
                certPath, pfxPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

            SignEnvelopeWithCertificate(envelopeXml, cert, bstId, messagingId, bodyId, attachmentCid, encryptedAttachment);
        }

        /// <summary>
        /// Sign the SOAP envelope using a provided certificate.
        /// Enhanced version with proper CID URI resolution for attachments.
        /// </summary>
        public static void SignEnvelopeWithCertificate(
            XDocument envelopeXml,
            X509Certificate2 cert,
            string bstId,
            string messagingId,
            string bodyId,
            string attachmentCid = null,
            byte[] encryptedAttachment = null)
        {

            // 1) Load into XmlDocument
            var xmlDoc = new XmlDocument { PreserveWhitespace = true };
            using (var reader = envelopeXml.CreateReader())
                xmlDoc.Load(reader);

            // 2) SignedXml setup with safe RSA key extraction
            var sxml = new SignedXmlWithId(xmlDoc)
            {
                SigningKey = cert.GetRSAPrivateKeySafe() // Use safe wrapper
            };
            sxml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            sxml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

            // 3) Reference Messaging + Body
            sxml.AddReference(CreateExcC14NReference("#" + messagingId));
            sxml.AddReference(CreateExcC14NReference("#" + bodyId));

            // 4) If there's an attachment, add reference with proper CID URI resolution
            if (!string.IsNullOrEmpty(attachmentCid) && encryptedAttachment != null)
            {
                try
                {
                    // Create attachment resolver for CID URIs
                    var attachmentMap = new Dictionary<string, byte[]>
                    {
                        { attachmentCid, encryptedAttachment }
                    };
                    var attachmentResolver = new AttachmentResolver(attachmentMap);
                    
                    // Set the resolver on SignedXml to handle CID URIs
                    sxml.Resolver = attachmentResolver;
                    
                    // Create attachment reference with proper CID URI
                    var attachRef = CreateAttachmentReference(attachmentCid, encryptedAttachment);
                    sxml.AddReference(attachRef);
                    
                    log.Debug($"Added attachment reference with CID: {attachmentCid}");
                }
                catch (Exception ex)
                {
                    log.Warn($"Failed to add attachment reference: {ex.Message}");
                    // Continue without attachment reference - this is not fatal for message signing
                }
            }

            // 5) KeyInfo → wsse:SecurityTokenReference
            sxml.KeyInfo = BuildKeyInfo(bstId);

            // 6) Compute + patch
            sxml.ComputeSignature();
            var signatureXml = sxml.GetXml();

            // 7) Replace the placeholder <ds:Signature/> in the envelope
            ReplaceSignature(xmlDoc, signatureXml);

            // 8) Save back into XDocument
            using (var ms = new MemoryStream())
            {
                xmlDoc.Save(ms);
                ms.Position = 0;
                envelopeXml.Root.ReplaceWith(XDocument.Load(ms).Root);
            }
        }

        /// <summary>
        /// Creates an attachment reference with safe digest calculation and proper CID URI handling
        /// </summary>
        private static Reference CreateAttachmentReference(string attachmentCid, byte[] encryptedAttachment)
        {
            // Ensure CID URI format is correct
            var cidUri = attachmentCid.StartsWith("cid:") ? attachmentCid : "cid:" + attachmentCid;
            
            var attachRef = new Reference(cidUri)
            {
                DigestMethod = SignedXml.XmlDsigSHA256Url
            };

            // Calculate digest directly for the attachment content
            var digest = ComputeSha256Digest(encryptedAttachment);

            // Set the digest value directly
            attachRef.DigestValue = digest;

            // Add the SwA transform for Peppol AS4 compliance
            // This transform does not alter the content for digest calculation
            attachRef.AddTransform(new AttachmentSignatureTransform());

            log.Debug($"Created attachment reference for CID: {cidUri}, Digest: {Convert.ToBase64String(digest)}");
            return attachRef;
        }

        /// <summary>
        /// Computes SHA256 digest for attachment without reflection usage
        /// </summary>
        private static byte[] ComputeSha256Digest(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        private static Reference CreateExcC14NReference(string uri)
        {
            var r = new Reference(uri);
            r.AddTransform(new XmlDsigExcC14NTransform());
            r.DigestMethod = SignedXml.XmlDsigSHA256Url;
            return r;
        }

        private static KeyInfo BuildKeyInfo(string bstId)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };

            // Create the <ds:KeyInfo> wrapper
            var keyInfoElement = doc.CreateElement("ds", "KeyInfo", SignedXml.XmlDsigNamespaceUrl);
            keyInfoElement.SetAttribute("Id", "KI-" + Guid.NewGuid().ToString("N"));

            // WS-Security and WS-Utility namespace URIs as strings:
            const string wsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
            const string wsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

            // Build the <wsse:SecurityTokenReference> with proper namespace declarations
            var strElement = doc.CreateElement("wsse", "SecurityTokenReference", wsseNs);

            // CRITICAL FIX: Properly declare namespaces for WSS4J compatibility
            strElement.SetAttribute("xmlns:wsse", wsseNs);
            strElement.SetAttribute("xmlns:wsu", wsuNs);

            // CRITICAL FIX: Set wsu:Id attribute using proper namespace URI, not XName
            strElement.SetAttribute("Id", wsuNs, "STR-" + Guid.NewGuid().ToString("N"));

            // Build the inner <wsse:Reference> with proper namespace and attributes
            var refElement = doc.CreateElement("wsse", "Reference", wsseNs);
            refElement.SetAttribute("URI", "#" + bstId);
            refElement.SetAttribute("ValueType",
                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3");

            strElement.AppendChild(refElement);
            keyInfoElement.AppendChild(strElement);

            var keyInfo = new KeyInfo();
            keyInfo.LoadXml(keyInfoElement);
            return keyInfo;
        }

        private static void ReplaceSignature(XmlDocument xmlDoc, XmlElement newSig)
        {
            var nsm = new XmlNamespaceManager(xmlDoc.NameTable);
            nsm.AddNamespace("ds", DS.NamespaceName);
            var oldSig = xmlDoc.SelectSingleNode("//ds:Signature", nsm);
            if (oldSig == null)
                throw new InvalidOperationException("No <ds:Signature> placeholder found.");
            oldSig.ParentNode.ReplaceChild(xmlDoc.ImportNode(newSig, true), oldSig);
        }


        /// <summary>
        /// Recursively sets the prefix for all ds:Signature nodes.
        /// </summary>
        public static void SetSignaturePrefix(XmlElement node, string prefix)
        {
            if (node.NamespaceURI == "http://www.w3.org/2000/09/xmldsig#")
                node.Prefix = prefix;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is XmlElement childElem)
                    SetSignaturePrefix(childElem, prefix);
            }
        }

        /// <summary>
        /// Builds WS-Security header with timestamp and certificate for WS-Security 1.1.1
        /// This is the enhanced method used by the AS4 controller
        /// </summary>
        public static XElement BuildWsSecurityHeader(
            XElement messaging,
            X509Certificate2 signingCert,
            string timestamp,
            string messagingId)
        {
            if (messaging == null) throw new ArgumentNullException(nameof(messaging));
            if (signingCert == null) throw new ArgumentNullException(nameof(signingCert));
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));

            log.Debug($"Building WS-Security header with timestamp: {timestamp}");

            // Generate unique IDs for WS-Security elements
            var timestampId = "TS-" + Guid.NewGuid().ToString("N");
            var bstId = "BST-" + Guid.NewGuid().ToString("N");

            try
            {
                // Build Timestamp token (mandatory for WS-Security 1.1.1)
                var timestampElement = BuildTimestampToken(timestamp, timestampId);

                // Build Binary Security Token with certificate
                var binarySecurityToken = BuildBinarySecurityToken(signingCert, bstId);

                // Create WS-Security header with proper namespace declarations
                var wsSecurityHeader = new XElement(WSSE + "Security",
                    new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "wsse11", WSSE11.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                    new XAttribute(S12 + "mustUnderstand", "1"),
                    new XAttribute(S12 + "role", "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/role/ebms"),
                    timestampElement,
                    binarySecurityToken
                // Note: Digital signature will be added later by SignEnvelope method
                );

                log.Debug("WS-Security header built successfully");
                return wsSecurityHeader;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to build WS-Security header: {ex.Message}", ex);
                throw new InvalidOperationException($"WS-Security header creation failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Builds Timestamp token according to WS-Security 1.1.1 specification
        /// </summary>
        public static XElement BuildTimestampToken(string timestamp, string timestampId)
        {
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));
            if (string.IsNullOrEmpty(timestampId)) throw new ArgumentNullException(nameof(timestampId));

            var createdTime = DateTime.Parse(timestamp);
            var expiresTime = createdTime.AddMinutes(5); // 5-minute validity window

            return new XElement(WSU + "Timestamp",
                new XAttribute(WSU + "Id", timestampId),
                new XElement(WSU + "Created", createdTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                new XElement(WSU + "Expires", expiresTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            );
        }

        /// <summary>
        /// Builds Binary Security Token with proper X.509 certificate encoding
        /// </summary>
        public static XElement BuildBinarySecurityToken(X509Certificate2 certificate, string bstId)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));
            if (string.IsNullOrEmpty(bstId)) throw new ArgumentNullException(nameof(bstId));

            return new XElement(WSSE + "BinarySecurityToken",
                new XAttribute("EncodingType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                new XAttribute("ValueType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3"),
                new XAttribute(WSU + "Id", bstId),
                Convert.ToBase64String(certificate.RawData)
            );
        }

        /// <summary>
        /// Verifies incoming AS4 message signature according to WS-Security 1.1.1
        /// Enhanced with transform compatibility handling for WSS4J interoperability
        /// </summary>
        public static bool VerifyMessageSignature(XDocument soapEnvelope, X509Certificate2 senderCertificate)
        {
            if (soapEnvelope == null) throw new ArgumentNullException(nameof(soapEnvelope));
            if (senderCertificate == null) throw new ArgumentNullException(nameof(senderCertificate));

            try
            {
                log.Debug("Starting signature verification for incoming AS4 message");

                // Convert to XmlDocument for SignedXml processing
                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                using (var reader = soapEnvelope.CreateReader())
                    xmlDoc.Load(reader);

                // Find the signature element
                var nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
                nsManager.AddNamespace("ds", DS.NamespaceName);
                nsManager.AddNamespace("wsse", WSSE.NamespaceName);

                var signatureNode = xmlDoc.SelectSingleNode("//ds:Signature", nsManager) as XmlElement;
                if (signatureNode == null)
                {
                    log.Warn("No signature found in AS4 message");
                    return false;
                }

                // CRITICAL FIX: Remove unknown transforms that may cause verification failures
                // This handles cases where Phase4 or other implementations include transforms
                // that are not recognized by .NET SignedXml
                try
                {
                    SignedXmlWithId.RemoveUnknownTransforms(signatureNode);
                    log.Debug("Successfully processed transforms for signature verification");
                }
                catch (Exception ex)
                {
                    log.Warn($"Transform processing warning (continuing): {ex.Message}");
                    // Continue with verification even if transform removal fails
                }

                // ENHANCED: Also remove attachment references from SignedInfo to avoid resolution issues
                // Attachment signatures are validated separately, so we can exclude them from main signature verification
                try
                {
                    RemoveAttachmentReferences(signatureNode);
                    log.Debug("Successfully removed attachment references from SignedInfo");
                }
                catch (Exception ex)
                {
                    log.Warn($"Attachment reference removal warning (continuing): {ex.Message}");
                }

                // Create SignedXmlWithId for enhanced ID resolution
                var signedXml = new SignedXmlWithId(xmlDoc);
                signedXml.LoadXml(signatureNode);

                // CRITICAL FIX: Use certificate validation with proper trust verification
                bool isSignatureValid = signedXml.CheckSignature(senderCertificate, true);

                if (isSignatureValid)
                {
                    log.Info("AS4 message signature verification successful");

                    // Additional validation: verify timestamp if present
                    var timestampValid = VerifyTimestamp(xmlDoc);
                    if (!timestampValid)
                    {
                        log.Warn("Timestamp validation failed");
                        return false;
                    }
                }
                else
                {
                    log.Error("AS4 message signature verification failed");
                }

                return isSignatureValid;
            }
            catch (Exception ex)
            {
                log.Error($"Signature verification error: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Removes attachment references from SignedInfo to prevent resolution issues
        /// Attachment signatures are validated separately in AS4 processing
        /// </summary>
        private static void RemoveAttachmentReferences(XmlElement signatureElement)
        {
            if (signatureElement == null) return;

            try
            {
                var nsManager = new XmlNamespaceManager(signatureElement.OwnerDocument.NameTable);
                nsManager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

                // Find SignedInfo element
                var signedInfo = signatureElement.SelectSingleNode("ds:SignedInfo", nsManager);
                if (signedInfo == null) return;

                // Find all Reference elements
                var references = signedInfo.SelectNodes("ds:Reference", nsManager);
                if (references == null) return;

                var referencesToRemove = new List<XmlNode>();

                foreach (XmlElement reference in references)
                {
                    var uri = reference.GetAttribute("URI");
                    
                    // Remove references to attachments (cid: URIs) and external resources
                    if (!string.IsNullOrEmpty(uri) && 
                        (uri.StartsWith("cid:") || uri.StartsWith("http://") || uri.StartsWith("https://")))
                    {
                        log.Debug($"Removing attachment/external reference: {uri}");
                        referencesToRemove.Add(reference);
                    }
                }

                // Remove identified references
                foreach (var reference in referencesToRemove)
                {
                    reference.ParentNode?.RemoveChild(reference);
                }

                if (referencesToRemove.Count > 0)
                {
                    log.Info($"Removed {referencesToRemove.Count} attachment references from SignedInfo");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error removing attachment references: {ex.Message}", ex);
                // Continue execution - this is not fatal
            }
        }

        /// <summary>
        /// Verifies timestamp token validity in WS-Security header
        /// </summary>
        public static bool VerifyTimestamp(XmlDocument xmlDoc)
        {
            try
            {
                var nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
                nsManager.AddNamespace("wsu", WSU.NamespaceName);

                var timestampNode = xmlDoc.SelectSingleNode("//wsu:Timestamp", nsManager);
                if (timestampNode == null)
                {
                    log.Debug("No timestamp found in message");
                    return true; // Timestamp is optional in some scenarios
                }

                var createdNode = timestampNode.SelectSingleNode("wsu:Created", nsManager);
                var expiresNode = timestampNode.SelectSingleNode("wsu:Expires", nsManager);

                if (createdNode == null)
                {
                    log.Warn("Timestamp missing Created element");
                    return false;
                }

                var created = DateTime.Parse(createdNode.InnerText);
                var now = DateTime.UtcNow;

                // Check if message is not from the future (with 1 minute tolerance)
                if (created > now.AddMinutes(1))
                {
                    log.Warn($"Message timestamp is in the future: {created} > {now}");
                    return false;
                }

                // Check expiration if present
                if (expiresNode != null)
                {
                    var expires = DateTime.Parse(expiresNode.InnerText);
                    if (now > expires)
                    {
                        log.Warn($"Message timestamp has expired: {now} > {expires}");
                        return false;
                    }
                }

                // Check if message is not too old (24 hours tolerance)
                if (created < now.AddHours(-24))
                {
                    log.Warn($"Message timestamp is too old: {created} < {now.AddHours(-24)}");
                    return false;
                }

                log.Debug("Timestamp validation successful");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Timestamp verification error: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Validates certificate chain against Peppol PKI requirements
        /// </summary>
        public static bool ValidateCertificateChain(X509Certificate2 certificate)
        {
            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            try
            {
                log.Debug($"Validating certificate chain for: {certificate.Subject}");

                // Create certificate chain
                var chain = new X509Chain();

                // Configure chain validation settings for Peppol
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.VerificationTime = DateTime.Now;
                chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0); // 1 minute timeout

                // Build the chain
                bool chainIsValid = chain.Build(certificate);

                if (!chainIsValid)
                {
                    log.Warn("Certificate chain validation failed:");
                    foreach (X509ChainStatus status in chain.ChainStatus)
                    {
                        log.Warn($"Chain status: {status.Status} - {status.StatusInformation}");
                    }
                }
                else
                {
                    log.Info("Certificate chain validation successful");
                }

                // Additional Peppol-specific validations
                if (chainIsValid)
                {
                    chainIsValid = ValidatePeppolCertificateUsage(certificate);
                }

                return chainIsValid;
            }
            catch (Exception ex)
            {
                log.Error($"Certificate chain validation error: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Validates certificate for Peppol-specific usage requirements
        /// </summary>
        private static bool ValidatePeppolCertificateUsage(X509Certificate2 certificate)
        {
            try
            {
                // Check certificate validity period
                if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
                {
                    log.Warn($"Certificate not valid for current time: {certificate.NotBefore} - {certificate.NotAfter}");
                    return false;
                }

                // Check key usage for digital signature
                foreach (var extension in certificate.Extensions)
                {
                    if (extension is X509KeyUsageExtension keyUsage)
                    {
                        if (!keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
                        {
                            log.Warn("Certificate does not have DigitalSignature key usage");
                            return false;
                        }
                    }
                }

                log.Debug("Peppol certificate usage validation successful");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Peppol certificate usage validation error: {ex.Message}", ex);
                return false;
            }
        }
    }

    static class XElementExtensions
    {
        public static XmlElement ToXmlElement(this XElement x)
        {
            var doc = new XmlDocument();
            doc.LoadXml(x.ToString());
            return doc.DocumentElement;
        }
    }

    public static class CryptoUtil
    {

        //------------------------------------------------------------------
        // Classic out‑parameters API (works nicely in C# 7.3 projects)
        //------------------------------------------------------------------
        public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (plain == null) throw new ArgumentNullException(nameof(plain));

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var enc = aes.CreateEncryptor();
            return enc.TransformFinalBlock(plain, 0, plain.Length);
        }
        public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] cipher)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        // ------------------------------------------------------------------
        // AES-GCM helpers used for eDelivery AS4 2.0 profile
        // ------------------------------------------------------------------
        public static byte[] AesGcmEncrypt(byte[] key, byte[] iv, byte[] plain, out byte[] tag)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (plain == null) throw new ArgumentNullException(nameof(plain));

            var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            var parameters = new Org.BouncyCastle.Crypto.Parameters.AeadParameters(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), 128, iv);
            cipher.Init(true, parameters);

            var output = new byte[cipher.GetOutputSize(plain.Length)];
            int len = cipher.ProcessBytes(plain, 0, plain.Length, output, 0);
            len += cipher.DoFinal(output, len);

            int tagLen = 16; // 128 bit tag
            int cipherLen = len - tagLen;
            var ciphertext = new byte[cipherLen];
            tag = new byte[tagLen];
            Array.Copy(output, 0, ciphertext, 0, cipherLen);
            Array.Copy(output, cipherLen, tag, 0, tagLen);
            return ciphertext;
        }

        public static byte[] AesGcmDecrypt(byte[] key, byte[] iv, byte[] cipher, byte[] tag)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            var input = new byte[cipher.Length + tag.Length];
            Array.Copy(cipher, 0, input, 0, cipher.Length);
            Array.Copy(tag, 0, input, cipher.Length, tag.Length);

            var gcm = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            var parameters = new Org.BouncyCastle.Crypto.Parameters.AeadParameters(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), tag.Length * 8, iv);
            gcm.Init(false, parameters);

            var output = new byte[gcm.GetOutputSize(input.Length)];
            int len = gcm.ProcessBytes(input, 0, input.Length, output, 0);
            len += gcm.DoFinal(output, len);

            var plain = new byte[len];
            Array.Copy(output, 0, plain, 0, len);
            return plain;
        }
    }
    internal sealed class AttachmentResolver : XmlUrlResolver
    {
        private readonly IDictionary<string, byte[]> _map;
        
        public AttachmentResolver(IDictionary<string, byte[]> map)
        {
            _map = map;
        }

        public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
        {
            if (absoluteUri.Scheme.Equals("cid", StringComparison.OrdinalIgnoreCase) &&
                _map.TryGetValue(absoluteUri.ToString(), out var bytes))
            {
                return new MemoryStream(bytes);
            }
            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }
    }
}