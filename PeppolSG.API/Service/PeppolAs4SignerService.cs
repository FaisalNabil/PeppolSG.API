using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using PeppolSG.API.Service.Interfaces;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Service implementation of IPeppolAs4Signer that wraps the static PeppolAs4Signer methods
    /// Provides dependency injection support and consistent signing behavior
    /// </summary>
    public class PeppolAs4SignerService : IPeppolAs4Signer
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(PeppolAs4SignerService));

        /// <summary>
        /// Signs the SOAP envelope using the provided certificate and parameters
        /// Enhanced with proper CID URI resolution for attachments
        /// </summary>
        public void SignEnvelope(XDocument envelopeXml, X509Certificate2 signingCert, string bstId, 
            string messagingId, string bodyId, string attachmentCid = null, byte[] encryptedAttachment = null)
        {
            try
            {
                log.Debug($"Signing envelope with certificate: {signingCert.Subject}");
                
                // Use the enhanced static method with proper attachment resolution
                PeppolAs4Signer.SignEnvelopeWithCertificate(
                    envelopeXml, 
                    signingCert, 
                    bstId, 
                    messagingId, 
                    bodyId, 
                    attachmentCid, 
                    encryptedAttachment);
                
                log.Info("Envelope signed successfully");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to sign envelope: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Verifies the message signature using the sender's certificate
        /// </summary>
        public bool VerifyMessageSignature(XDocument soapEnvelope, X509Certificate2 senderCertificate)
        {
            try
            {
                log.Debug($"Verifying message signature with certificate: {senderCertificate.Subject}");
                
                var result = PeppolAs4Signer.VerifyMessageSignature(soapEnvelope, senderCertificate);
                
                log.Debug($"Message signature verification result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to verify message signature: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Verifies the timestamp in the SOAP envelope
        /// </summary>
        public bool VerifyTimestamp(XDocument soapEnvelope)
        {
            try
            {
                log.Debug("Verifying timestamp");
                
                // Convert XDocument to XmlDocument for static method compatibility
                var xmlDoc = new System.Xml.XmlDocument { PreserveWhitespace = true };
                using (var reader = soapEnvelope.CreateReader())
                    xmlDoc.Load(reader);
                
                var result = PeppolAs4Signer.VerifyTimestamp(xmlDoc);
                
                log.Debug($"Timestamp verification result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to verify timestamp: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Builds WS-Security header for the given messaging element
        /// </summary>
        public XElement BuildWsSecurityHeader(XElement messaging, X509Certificate2 signingCert, string timestamp, string messagingId)
        {
            try
            {
                log.Debug($"Building WS-Security header for messaging ID: {messagingId}");
                
                var result = PeppolAs4Signer.BuildWsSecurityHeader(messaging, signingCert, timestamp, messagingId);
                
                log.Debug("WS-Security header built successfully");
                return result;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to build WS-Security header: {ex.Message}", ex);
                throw;
            }
        }
    }
} 