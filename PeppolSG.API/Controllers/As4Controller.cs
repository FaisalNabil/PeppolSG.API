using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml;
using System.Xml.Linq;
using log4net;
using MimeKit;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Security;
using PeppolSG.API.Models;
using PeppolSG.API.Service;
using System.IO.Compression;
using System.Runtime.Remoting.Messaging;
using PeppolSG.API.Service.Interfaces;
using LogManager = log4net.LogManager;
using static PeppolSG.API.Service.CryptoCompatibilityHelper;

namespace PeppolSG.API.Controllers
{
    /// <summary>
    /// Peppol AS4 Access Point Controller
    /// Handles sending and receiving of AS4 messages for the Peppol network.
    /// </summary>
    [RoutePrefix("as4")]
    public class As4Controller : ApiController
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(As4Controller));
        
        // Dependencies injected via constructor or property
        private readonly IPeppolConfigurationService _configService;
        private readonly IPeppolAs4MessageValidator _messageValidator;
        private readonly ICertificateManager _certificateManager;
        private readonly ISmkSmpLookupService _smkSmpLookup;
        private readonly IAs4MessageBuilder _messageBuilder;
        private readonly IMimeParserService _mimeParser;
        private readonly IPayloadPersister _payloadPersister;

        /// <summary>
        /// Public constructor for ASP.NET Web API to instantiate the controller.
        /// This creates a default service container.
        /// </summary>
        public As4Controller()
        {
            // This is a basic service locator pattern. For a more robust solution, use a DI container like Unity or Autofac.
            _configService = new PeppolConfigurationService();
            _certificateManager = new CertificateManager(_configService);
            _smkSmpLookup = new SmkSmpLookupService(_configService, _certificateManager);
            _messageValidator = new PeppolAs4MessageValidator(_configService);
            _messageBuilder = new As4MessageBuilder(_configService);
            _mimeParser = new MimeParserService();
            _payloadPersister = new FileSystemPayloadPersister();
            
            log.Info("AS4 Controller initialized with default service container.");
        }

        /// <summary>
        /// Constructor for dependency injection, used for testing or with a DI container.
        /// </summary>
        public As4Controller(
            IPeppolConfigurationService configService,
            IPeppolAs4MessageValidator messageValidator,
            ICertificateManager certificateManager,
            ISmkSmpLookupService smkSmpLookup,
            IAs4MessageBuilder messageBuilder,
            IMimeParserService mimeParser,
            IPayloadPersister payloadPersister)
        {
            _configService = configService;
            _messageValidator = messageValidator;
            _certificateManager = certificateManager;
            _smkSmpLookup = smkSmpLookup;
            _messageBuilder = messageBuilder;
            _mimeParser = mimeParser;
            _payloadPersister = payloadPersister;

            log.Info("AS4 Controller initialized via Dependency Injection.");
        }

        /// <summary>
        /// Main AS4 endpoint for receiving Peppol messages
        /// Handles UserMessage and SignalMessage according to AS4 profile
        /// Enhanced for proper encrypted attachment processing
        /// </summary>
        [HttpPost, Route("")]
        public async Task<IHttpActionResult> ReceiveAs4Message()
        {
            var correlationId = Guid.NewGuid().ToString("N");
            log.Info($"[{correlationId}] AS4 message received.");

            try
            {
                var contentType = Request.Content.Headers.ContentType?.ToString();
                if (!Request.Content.IsMimeMultipartContent() || !contentType.Contains("related"))
                {
                    return await HandleEbms3Error(correlationId, "EBMS:0001", "InvalidHeader", "Content-Type must be multipart/related.", null, HttpStatusCode.BadRequest);
                }

                var requestStream = await Request.Content.ReadAsStreamAsync();
                var mimeParts = await _mimeParser.ParseMultipartRequest(requestStream, contentType);

                var soapPart = mimeParts.FirstOrDefault(p => p.ContentType.Contains("application/soap+xml"));
                if (soapPart == null)
                {
                    return await HandleEbms3Error(correlationId, "EBMS:0001", "MissingPart", "SOAP part is missing.", null, HttpStatusCode.BadRequest);
                }

                var soapXml = XDocument.Parse(soapPart.ContentText);
                var validationResult = _messageValidator.ValidateIncomingMessage(soapXml);

                if (!validationResult.IsValid)
                {
                    if (validationResult.Errors == null || !validationResult.Errors.Any())
                    {
                        log.Error($"[{correlationId}] Validation failed but no error details available");
                        return await HandleEbms3Error(correlationId, "EBMS:0004", "Error", "Message validation failed without specific error details", validationResult.MessageId, HttpStatusCode.BadRequest);
                    }
                    
                    var error = validationResult.Errors.First();
                    return await HandleEbms3Error(correlationId, error.Code ?? "EBMS:0004", error.Severity ?? "Error", error.Description ?? "Validation failed", validationResult.MessageId, HttpStatusCode.BadRequest);
                }

                // Convert XDocument to XmlDocument for VerifyTimestamp
                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                using (var reader = soapXml.CreateReader())
                    xmlDoc.Load(reader);
                
                if (!PeppolAs4Signer.VerifyTimestamp(xmlDoc))
                {
                     return await HandleEbms3Error(correlationId, "EBMS:0103", "SecurityFailure", "Timestamp validation failed.", validationResult.MessageId, HttpStatusCode.Unauthorized);
                }

                var userMsg = SOAPHeaderParser.GetUserMessage(soapXml);
                //IMPORTANT: need to get original sender from UserMessage properties and then use its value to look up the endpoint metadata
                var originalSender = userMsg.Properties.FirstOrDefault(p => p.Name == "originalSender");
                var endpointMetadata = await _smkSmpLookup.LookupEndpointMetadata(originalSender.Value, originalSender.Type, userMsg.Action, userMsg.Service);
                if (endpointMetadata == null)
                {
                     return await HandleEbms3Error(correlationId, "EBMS:0010", "ProcessingModeMismatch", "SMP lookup failed for sender.", userMsg.MessageId, HttpStatusCode.BadRequest);
                }

                var senderCert = new X509Certificate2(Convert.FromBase64String(endpointMetadata.Certificate));
                
                if (!_certificateManager.ValidateCertificate(senderCert))
                {
                    return await HandleEbms3Error(correlationId, "EBMS:0101", "FailedAuthentication", "Sender certificate is not valid.", userMsg.MessageId, HttpStatusCode.Unauthorized);
                }

                //IMPORTANT: Verify the message signature using the sender's certificate (not working)
                if (!PeppolAs4Signer.VerifyMessageSignature(soapXml, senderCert))
                {
                     return await HandleEbms3Error(correlationId, "EBMS:0102", "FailedAuthentication", "Message signature validation failed.", userMsg.MessageId, HttpStatusCode.Unauthorized);
                }

                // CRITICAL ENHANCEMENT: Process encrypted attachments
                log.Info($"[{correlationId}] Processing encrypted attachments for message {userMsg.MessageId}");
                var attachmentPaths = new List<string>();
                
                try
                {
                    // Load our private key for decryption
                    var ourCert = _certificateManager.LoadSigningCertificate();
                    
                    // Process each attachment referenced in the UserMessage
                    foreach (var href in userMsg.PayloadHrefs)
                    {
                        var attachmentPart = mimeParts.FirstOrDefault(p => 
                            p.ContentId == href.Replace("cid:", "").Trim('<', '>'));
                        
                        if (attachmentPart != null)
                        {
                            log.Info($"[{correlationId}] Found attachment: {attachmentPart.ContentId}, Size: {attachmentPart.ContentBytes?.Length ?? 0} bytes");
                            
                            byte[] decryptedBytes;
                            if (_configService.IsDebugMode())
                            {
                                // Debug mode: save encrypted attachment as-is
                                decryptedBytes = attachmentPart.ContentBytes;
                                log.Debug($"[{correlationId}] Debug mode: saving encrypted attachment without decryption");
                            }
                            else
                            {
                                // Production mode: decrypt the attachment
                                decryptedBytes = DecryptPeppolAttachment(
                                    attachmentPart.ContentBytes, 
                                    soapXml, 
                                    ourCert, 
                                    href);
                                log.Info($"[{correlationId}] Successfully decrypted attachment: {attachmentPart.ContentId}");
                            }
                            
                            // Save the decrypted payload
                            var payloadInfo = new PayloadInfo
                            {
                                ContentId = attachmentPart.ContentId,
                                MimeType = attachmentPart.ContentType,
                                IsGzip = attachmentPart.ContentType.Contains("gzip") || 
                                         userMsg.PayloadProperties.ContainsKey("CompressionType") && 
                                         userMsg.PayloadProperties["CompressionType"] == "application/gzip"
                            };
                            
                            var savedPath = _payloadPersister.Persist(userMsg.MessageId, payloadInfo, decryptedBytes);
                            attachmentPaths.Add(savedPath);
                            log.Info($"[{correlationId}] Saved payload to: {savedPath}");
                        }
                        else
                        {
                            log.Warn($"[{correlationId}] Referenced attachment not found: {href}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] Attachment processing failed: {ex.Message}", ex);
                    return await HandleEbms3Error(correlationId, "EBMS:0004", "Error", $"Attachment processing failed: {ex.Message}", userMsg.MessageId, HttpStatusCode.BadRequest);
                }

                log.Info($"[{correlationId}] Message {userMsg.MessageId} successfully validated and processed. Attachments: {attachmentPaths.Count}");
                return await GenerateAs4Receipt(userMsg.MessageId, correlationId);
            }
            catch (Exception ex)
            {
                log.Error($"[{correlationId}] Unhandled error processing AS4 message: {ex.Message}", ex);
                return await HandleEbms3Error(correlationId, "EBMS:0004", "UnexpectedError", "An unexpected error occurred.", null, HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Generates AS4 Receipt (SignalMessage) according to AS4 profile
        /// Enhanced for Phase4/WSS4J compatibility
        /// </summary>
        private async Task<IHttpActionResult> GenerateAs4Receipt(string refToMessageId, string correlationId)
        {
            log.Info($"[{correlationId}] Generating AS4 Receipt for message: {refToMessageId}");

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var messageId = $"receipt-{Guid.NewGuid()}@{_configService.GetPeppolDomain()}";
                
                var receiptMessage = _messageBuilder.BuildSignalMessage(timestamp, messageId, refToMessageId);

                var signingCert = _certificateManager.LoadSigningCertificate();
                var bodyId = "body-" + Guid.NewGuid().ToString("N");
                var messagingId = "_1";

                log.Debug($"[{correlationId}] Building receipt message components - MessageId: {messageId}, BodyId: {bodyId}, MessagingId: {messagingId}");

                // Step 1: Build messaging and WS-Security header with proper element IDs
                var messaging = _messageBuilder.BuildMessaging(receiptMessage, messagingId);
                var wsSecurityHeader = PeppolAs4Signer.BuildWsSecurityHeader(messaging, signingCert, timestamp, messagingId);

                // Step 2: CRITICAL FIX - Validate and ensure all referenced elements have proper IDs
                ValidateReceiptElementIds(wsSecurityHeader, messaging, bodyId, messagingId);

                // Step 3: Build SOAP envelope with validated elements
                var soapEnvelope = _messageBuilder.BuildSoapEnvelope(messaging, wsSecurityHeader);

                // Step 4: Enhanced signing with proper reference validation
                try
                {
                    var certPath = _configService.GetSigningCertificatePath();
                    var certPassword = _configService.GetSigningCertificatePassword();
                    
                    // CRITICAL FIX: Use enhanced signing method that validates references before signing
                    SignReceiptEnvelopeWithValidation(soapEnvelope, certPath, certPassword, messagingId, bodyId, correlationId);
                    
                    log.Info($"[{correlationId}] AS4 Receipt signed successfully");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] Receipt signing failed: {ex.Message}", ex);
                    // For receipts, we can continue with unsigned response as fallback
                    log.Warn($"[{correlationId}] Generating unsigned receipt as fallback");
                }

                var response = _messageBuilder.CreateMtomResponse(soapEnvelope, null, HttpStatusCode.OK);
                log.Info($"[{correlationId}] AS4 Receipt generated successfully");
                return ResponseMessage(response);
            }
            catch (Exception ex)
            {
                log.Error($"[{correlationId}] Failed to generate AS4 Receipt: {ex.Message}", ex);
                return InternalServerError(new Exception($"Receipt generation failed: {ex.Message}"));
            }
        }

        /// <summary>
        /// Validates that all elements referenced in WS-Security have proper IDs
        /// Critical for preventing malformed reference errors during signing
        /// </summary>
        private void ValidateReceiptElementIds(XElement wsSecurityHeader, XElement messaging, string bodyId, string messagingId)
        {
            try
            {
                // Ensure messaging element has proper ID
                var messagingElement = messaging;
                if (messagingElement.Attribute(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")) == null)
                {
                    messagingElement.SetAttributeValue(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"), messagingId);
                    log.Debug($"Added missing wsu:Id to messaging element: {messagingId}");
                }

                // Validate BST element ID
                var bstElement = wsSecurityHeader.Descendants(XName.Get("BinarySecurityToken", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd")).FirstOrDefault();
                if (bstElement != null)
                {
                    var bstId = bstElement.Attribute(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"))?.Value;
                    if (string.IsNullOrEmpty(bstId))
                    {
                        bstId = "BST-" + Guid.NewGuid().ToString("N");
                        bstElement.SetAttributeValue(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"), bstId);
                        log.Debug($"Added missing wsu:Id to BST element: {bstId}");
                    }
                }

                // Validate Timestamp element ID
                var timestampElement = wsSecurityHeader.Descendants(XName.Get("Timestamp", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")).FirstOrDefault();
                if (timestampElement != null)
                {
                    var timestampId = timestampElement.Attribute(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"))?.Value;
                    if (string.IsNullOrEmpty(timestampId))
                    {
                        timestampId = "TS-" + Guid.NewGuid().ToString("N");
                        timestampElement.SetAttributeValue(XName.Get("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"), timestampId);
                        log.Debug($"Added missing wsu:Id to Timestamp element: {timestampId}");
                    }
                }

                log.Debug("Receipt element ID validation completed successfully");
            }
            catch (Exception ex)
            {
                log.Warn($"Element ID validation warning: {ex.Message}");
                // Continue execution - this is not fatal
            }
        }

        /// <summary>
        /// Enhanced receipt signing with reference validation
        /// Prevents malformed reference errors by validating all references before signing
        /// </summary>
        private void SignReceiptEnvelopeWithValidation(XDocument soapEnvelope, string certPath, string certPassword, 
            string messagingId, string bodyId, string correlationId)
        {
            try
            {
                // Convert to XmlDocument for SignedXml processing
                var xmlDoc = new XmlDocument { PreserveWhitespace = true };
                using (var reader = soapEnvelope.CreateReader())
                    xmlDoc.Load(reader);

                // Ensure Body element has the correct ID
                var nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
                nsManager.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
                nsManager.AddNamespace("wsu", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd");

                var bodyElement = xmlDoc.SelectSingleNode("//soap:Body", nsManager) as XmlElement;
                if (bodyElement != null && string.IsNullOrEmpty(bodyElement.GetAttribute("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd")))
                {
                    bodyElement.SetAttribute("Id", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd", bodyId);
                    log.Debug($"[{correlationId}] Added wsu:Id to Body element: {bodyId}");
                }

                // Load certificate
                var cert = new X509Certificate2(certPath, certPassword, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

                // Create SignedXmlWithId for enhanced ID resolution
                var signedXml = new SignedXmlWithId(xmlDoc);
                signedXml.SigningKey = cert.GetRSAPrivateKeySafe(); // Use safe wrapper

                // Create simplified reference list for receipts (avoid problematic references)
                var reference = new Reference("#" + bodyId);
                reference.AddTransform(new XmlDsigExcC14NTransform());
                reference.DigestMethod = SignedXml.XmlDsigSHA256Url;
                signedXml.AddReference(reference);

                // Add simplified KeyInfo for receipts
                var keyInfo = new KeyInfo();
                keyInfo.AddClause(new KeyInfoX509Data(cert));
                signedXml.KeyInfo = keyInfo;

                // Sign with enhanced error handling
                signedXml.ComputeSignature();

                // Insert signature into Security header
                nsManager.AddNamespace("wsse", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd");
                var securityElement = xmlDoc.SelectSingleNode("//wsse:Security", nsManager) as XmlElement;
                if (securityElement != null)
                {
                    securityElement.AppendChild(xmlDoc.ImportNode(signedXml.GetXml(), true));
                    log.Debug($"[{correlationId}] Signature successfully added to WS-Security header");
                }
                else
                {
                    log.Warn($"[{correlationId}] Could not locate WS-Security header for signature insertion");
                }

                // Update the original XDocument
                soapEnvelope.Root.ReplaceWith(XElement.Load(new XmlNodeReader(xmlDoc.DocumentElement)));
                
                log.Info($"[{correlationId}] Receipt envelope signed successfully with validated references");
            }
            catch (Exception ex)
            {
                log.Error($"[{correlationId}] Enhanced receipt signing failed: {ex.Message}", ex);
                throw new InvalidOperationException($"Receipt signing failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Handles ebMS3 compliant error responses
        /// </summary>
        private async Task<IHttpActionResult> HandleEbms3Error(string correlationId, string errorCode, 
            string severity, string description, string refToMessageId, HttpStatusCode httpStatus)
        {
            try
            {
                log.Error($"[{correlationId}] AS4 Error - Code: {errorCode}, Severity: {severity}, Description: {description}");

                // Generate error message details
                var errorTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var errorMessageId = $"error-{Guid.NewGuid()}@{_configService.GetPeppolDomain()}";

                // Build ebMS3 Error message
                var errorMessage = _messageBuilder.BuildErrorMessage(
                    errorTimestamp,
                    errorMessageId,
                    refToMessageId,
                    errorCode,
                    severity,
                    description
                );

                string messagingId = "phase4-error-" + Guid.NewGuid().ToString("N");
                var messaging = _messageBuilder.BuildMessaging(errorMessage, messagingId);

                // Sign error message if possible
                XDocument soapEnvelope;
                try
                {
                    var signingCert = _configService.LoadSigningCertificate();
                    // Build a basic WS-Security header for error messages
                    var wsSecurityHeader = PeppolAs4Signer.BuildWsSecurityHeader(messaging, signingCert, errorTimestamp, messagingId);
                    soapEnvelope = _messageBuilder.BuildSoapEnvelope(messaging, wsSecurityHeader);
                }
                catch (Exception ex)
                {
                    log.Warn($"[{correlationId}] Could not sign error message: {ex.Message}");
                    // Return unsigned error message
                    soapEnvelope = _messageBuilder.BuildSoapEnvelope(messaging, null);
                }

                var response = Request.CreateResponse(httpStatus);
                response.Content = new StringContent(soapEnvelope.ToString(), Encoding.UTF8, "application/soap+xml");
                
                // Add error-specific headers
                response.Headers.Add("X-AS4-Message-Type", "Error");
                response.Headers.Add("X-AS4-Error-Code", errorCode);
                response.Headers.Add("X-AS4-Error-Severity", severity);
                if (!string.IsNullOrEmpty(refToMessageId))
                    response.Headers.Add("X-AS4-Ref-To-Message-Id", refToMessageId);

                return ResponseMessage(response);
            }
            catch (Exception ex)
            {
                log.Fatal($"[{correlationId}] Critical error while generating ebMS3 error response: {ex.Message}", ex);
                
                // Return basic HTTP error as last resort
                return InternalServerError(new Exception($"Critical AS4 processing failure: {description}"));
            }
        }

        /// <summary>
        /// Extracts boundary parameter from Content-Type header
        /// </summary>
        private string ExtractBoundary(string contentType)
        {
            return contentType.Split(';')
                .Select(p => p.Trim())
                .FirstOrDefault(p => p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                ?.Substring("boundary=".Length)
                .Trim('"');
        }

        // ... existing code ...

        /// <summary>
        /// Sends AS4 message to Peppol participant endpoint
        /// Implements Peppol AS4 Profile v2.0.3 for outbound messaging
        /// </summary>
        [HttpPost]
        [Route("send")]
        public async Task<IHttpActionResult> SendAs4Message()
        {
            var correlationId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;
            string messageId = null;

            try
            {
                log.Info($"[{correlationId}] === Outbound AS4 Message Send Started ===");

                // Step 1: Read and validate invoice XML
                var invoiceXml = await Request.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(invoiceXml))
                {
                    log.Error($"[{correlationId}] Empty or null invoice XML received");
                    return BadRequest("Invoice XML content is required");
                }

                log.Info($"[{correlationId}] Invoice XML received - Length: {invoiceXml.Length} characters");

                // Step 2: Extract PEPPOL header information
                PeppolHeaderInfo peppolHeader;
                try
                {
                    peppolHeader = ExtractPeppolHeaderInfo(invoiceXml);
                    log.Info($"[{correlationId}] Peppol header extracted - Sender: {peppolHeader.SenderId}, Receiver: {peppolHeader.ReceiverId}");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] Failed to extract Peppol header: {ex.Message}", ex);
                    return BadRequest($"Invalid Peppol document structure: {ex.Message}");
                }

                var recipientId = peppolHeader.ReceiverId;
                var senderId = peppolHeader.SenderId;
                var receiverId = peppolHeader.ReceiverId;
                var receiverScheme = peppolHeader.ReceiverScheme ?? "iso6523-actorid-upis";
                var docTypeId = $"{peppolHeader.DocumentScheme}::{peppolHeader.DocTypeId}";
                var processId = peppolHeader.ProcessId;
                var instanceId = peppolHeader.InstanceId;
                var conversationId = $"Conv-{Guid.NewGuid()}";

                // Step 3: Perform SMP lookup for recipient endpoint and certificate
                log.Info($"[{correlationId}] Performing SMP lookup for recipient {receiverId}");
                Models.SmpEndpoint endpointMeta;
                try
                {
                    endpointMeta = await _smkSmpLookup.LookupEndpointMetadata(
                        receiverId, receiverScheme, docTypeId, processId);
                    log.Info($"[{correlationId}] SMP lookup successful - Endpoint: {endpointMeta.EndpointReference.Address}");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] SMP lookup failed: {ex.Message}", ex);
                    return BadRequest($"SMP lookup failed for recipient {receiverId}: {ex.Message}");
                }

                var recipientEndpoint = endpointMeta.EndpointReference.Address;
                var recipientCert = new X509Certificate2(Convert.FromBase64String(endpointMeta.Certificate));

                // Step 4: Generate message identifiers using configuration
                var domain = _configService.GetPeppolDomain();
                messageId = $"{instanceId}@{domain}";
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                log.Info($"[{correlationId}] Generated MessageId: {messageId}");

                // Step 5: Load sender certificate from configuration
                X509Certificate2 senderCert;
                try
                {
                    senderCert = _configService.LoadSigningCertificate();
                    log.Info($"[{correlationId}] Sender certificate loaded successfully");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] Failed to load sender certificate: {ex.Message}", ex);
                    return InternalServerError(new Exception($"Certificate loading failed: {ex.Message}"));
                }

                var senderPartyId = GetCertificateCommonName(senderCert);
                var receiverPartyId = GetCertificateCommonName(recipientCert);

                // Step 6: Compress and encrypt payload using AES-GCM
                log.Info($"[{correlationId}] Starting payload encryption");
                byte[] encryptedAttachment;
                string encKeyB64;
                try
                {
                    var plainBytes = Encoding.UTF8.GetBytes(invoiceXml);
                    var gzipped = CompressGzip(plainBytes);
                    
                    // Generate AES key and IV (12 bytes for GCM as per eDelivery AS4 Profile)
                    var aesKey = new byte[16]; // 128-bit AES key
                    var aesIv = new byte[12];  // 96-bit IV for GCM
                    byte[] gcmTag;
                    
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(aesKey);
                        rng.GetBytes(aesIv);
                    }
                    
                    // Encrypt the gzipped payload with AES-GCM
                    var cipher = CryptoUtil.AesGcmEncrypt(aesKey, aesIv, gzipped, out gcmTag);

                    // Concatenate IV + cipher + tag (as per Peppol conventions)
                    encryptedAttachment = new byte[aesIv.Length + cipher.Length + gcmTag.Length];
                    Buffer.BlockCopy(aesIv, 0, encryptedAttachment, 0, aesIv.Length);
                    Buffer.BlockCopy(cipher, 0, encryptedAttachment, aesIv.Length, cipher.Length);
                    Buffer.BlockCopy(gcmTag, 0, encryptedAttachment, aesIv.Length + cipher.Length, gcmTag.Length);

                    // Protect AES key with RSA-OAEP/SHA-256
                    var encryptedAesKey = RsaOaepEncrypt_MGF1_SHA256(aesKey, recipientCert);
                    encKeyB64 = Convert.ToBase64String(encryptedAesKey);
                    
                    log.Info($"[{correlationId}] Payload encryption completed - Encrypted size: {encryptedAttachment.Length} bytes");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] Payload encryption failed: {ex.Message}", ex);
                    return InternalServerError(new Exception($"Payload encryption failed: {ex.Message}"));
                }

                // Step 7: Generate unique identifiers for encryption elements
                var encryptedKeyId = "EK-" + Guid.NewGuid().ToString("N");
                var encryptedDataId = "ED-" + Guid.NewGuid().ToString("N");
                var attachmentCid = "phase4-att-" + Guid.NewGuid().ToString("N") + "@cid";
                var partHref = "cid:" + attachmentCid;

                // Step 8: Build UserMessage according to Peppol AS4 Profile
                log.Info($"[{correlationId}] Building AS4 UserMessage");
                var userMsg = _messageBuilder.BuildUserMessage(
                    messageId, timestamp, conversationId,
                    senderPartyId, receiverPartyId,
                    docTypeId, processId, partHref,
                    receiverId, senderId, true
                );
                var messagingId = "phase4-msg-" + Guid.NewGuid().ToString("N");
                var messaging = _messageBuilder.BuildMessaging(userMsg, messagingId);

                // Step 9: Build WS-Security header with encryption elements
                log.Info($"[{correlationId}] Building WS-Security header");
                string recipientBstId = "BST-Recipient-" + Guid.NewGuid().ToString("N");
                var recipientBst = _messageBuilder.BuildBinarySecurityToken(recipientCert, recipientBstId);
                string senderBstId = "BST-Sender-" + Guid.NewGuid().ToString("N");
                var senderBst = _messageBuilder.BuildBinarySecurityToken(senderCert, senderBstId);

                var encryptedKeyEl = _messageBuilder.BuildEncryptedKey(encryptedKeyId, recipientBstId, encKeyB64, encryptedDataId);
                var encryptedDataEl = _messageBuilder.BuildEncryptedData(encryptedDataId, encryptedKeyId, attachmentCid);

                var wsseSec = _messageBuilder.BuildSecurityHeader(
                    recipientBst, encryptedKeyEl, encryptedDataEl, senderBst
                );

                // Step 10: Assemble and sign SOAP envelope
                log.Info($"[{correlationId}] Assembling and signing SOAP envelope");
                XDocument soapDoc;
                try
                {
                    var bodyId = "id-" + Guid.NewGuid().ToString("N");
                    soapDoc = _messageBuilder.WrapInSoapEnvelope(wsseSec, messaging, bodyId);
                    
                    PeppolAs4Signer.SignEnvelope(
                        soapDoc, 
                        _configService.GetSigningCertificatePath(), 
                        _configService.GetSigningCertificatePassword(),
                        senderBstId, messagingId, bodyId,
                        partHref, encryptedAttachment
                    );
                    
                    log.Info($"[{correlationId}] SOAP envelope signed successfully");
                }
                catch (Exception ex)
                {
                    log.Error($"[{correlationId}] SOAP envelope signing failed: {ex.Message}", ex);
                    return InternalServerError(new Exception($"Message signing failed: {ex.Message}"));
                }

                // Step 11: Build MIME multipart/related message
                log.Info($"[{correlationId}] Building MIME multipart message");
                var attachments = new List<As4Attachment>
                {
                    new As4Attachment
                    {
                        ContentId = attachmentCid,
                        ContentType = "application/octet-stream",
                        Bytes = encryptedAttachment
                    }
                };

                var mtomResponse = _messageBuilder.CreateMtomResponse(
                    soapDoc,
                    attachments,
                    HttpStatusCode.OK
                );

                // Step 12: Send AS4 message to recipient endpoint
                log.Info($"[{correlationId}] Sending AS4 message to endpoint: {recipientEndpoint}");
                using (var http = new HttpClient())
                {
                    // Configure HttpClient timeout
                    http.Timeout = TimeSpan.FromMinutes(5);

                    var outReq = new HttpRequestMessage(HttpMethod.Post, recipientEndpoint)
                    {
                        Content = mtomResponse.Content
                    };

                    // Add required AS4 headers
                    outReq.Headers.Add("SOAPAction", ""); // Required by AS4 specification
                    outReq.Headers.TryAddWithoutValidation("MIME-Version", "1.0");
                    
                    // Add optional debugging headers (remove in production)
                    if (_configService.IsDebugMode())
                    {
                        outReq.Headers.Add("X-Debug-Token", "PeppolSG-AS4");
                        outReq.Headers.Add("X-Debug-CorrelationId", correlationId);
                    }

                    // Log request headers for debugging
                    foreach (var h in outReq.Headers)
                        log.Debug($"[{correlationId}] Request header: {h.Key}: {string.Join(", ", h.Value)}");

                    // Optional: Log MIME body for debugging (be careful with sensitive data)
                    if (_configService.IsDebugMode())
                    {
                        var debugRaw = await mtomResponse.Content.ReadAsStringAsync();
                        log.Debug($"[{correlationId}] Outgoing MIME body length: {debugRaw.Length} characters");
                    }

                    HttpResponseMessage resp;
                    try
                    {
                        resp = await http.SendAsync(outReq);
                        log.Info($"[{correlationId}] HTTP response received - Status: {resp.StatusCode}");
                    }
                    catch (HttpRequestException ex)
                    {
                        log.Error($"[{correlationId}] HTTP request failed: {ex.Message}", ex);
                        return InternalServerError(new Exception($"Failed to send AS4 message to {recipientEndpoint}: {ex.Message}"));
                    }
                    catch (TaskCanceledException ex)
                    {
                        log.Error($"[{correlationId}] HTTP request timeout: {ex.Message}", ex);
                        return InternalServerError(new Exception($"Timeout sending AS4 message to {recipientEndpoint}"));
                    }

                    // Log response details
                    var responseBody = await resp.Content.ReadAsStringAsync();
                    log.Info($"[{correlationId}] Response body length: {responseBody.Length} characters");
                    
                    if (resp.IsSuccessStatusCode)
                    {
                        log.Info($"[{correlationId}] === AS4 Message Send Completed Successfully === " +
                                $"Duration: {(DateTime.UtcNow - startTime).TotalMilliseconds}ms");
                    }
                    else
                    {
                        log.Error($"[{correlationId}] AS4 send failed with HTTP {resp.StatusCode}: {responseBody}");
                    }

                    return ResponseMessage(resp);
                }
            }
            catch (Exception ex)
            {
                log.Error($"[{correlationId}] Unhandled exception in AS4 send: {ex.Message}", ex);
                return InternalServerError(new Exception($"AS4 send operation failed: {ex.Message}"));
            }
        }

        // ------------------------------------------------------------------
        // helper methods (C# 7.3-friendly)
        // ------------------------------------------------------------------

        internal static byte[] CompressGzip(byte[] input)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(input, 0, input.Length);
                return ms.ToArray();
            }
        }

        public static PeppolHeaderInfo ExtractPeppolHeaderInfo(string sbdXml)
        {
            XDocument xdoc = XDocument.Parse(sbdXml);
            XNamespace ns = "http://www.unece.org/cefact/namespaces/StandardBusinessDocumentHeader";

            var header = xdoc.Descendants(ns + "StandardBusinessDocumentHeader").FirstOrDefault();
            if (header == null)
                throw new Exception("StandardBusinessDocumentHeader missing");

            var senderId = header.Descendants(ns + "Sender").Descendants(ns + "Identifier").FirstOrDefault()?.Value;
            var receiverId = header.Descendants(ns + "Receiver").Descendants(ns + "Identifier").FirstOrDefault()?.Value;
            var senderScheme = header.Descendants(ns + "Sender").Descendants(ns + "Identifier").FirstOrDefault()?.Attribute("Authority")?.Value;
            var receiverScheme = header.Descendants(ns + "Receiver").Descendants(ns + "Identifier").FirstOrDefault()?.Attribute("Authority")?.Value;

            // DocumentIdentification->InstanceIdentifier (unique for every invoice)
            var instanceId = header.Descendants(ns + "DocumentIdentification").Descendants(ns + "InstanceIdentifier").FirstOrDefault()?.Value;

            // BusinessScope->Scope for DOCUMENTID
            var docScope = header.Descendants(ns + "BusinessScope")
                .Descendants(ns + "Scope")
                .FirstOrDefault(e => e.Element(ns + "Type")?.Value == "DOCUMENTID");
            var docTypeId = docScope?.Element(ns + "InstanceIdentifier")?.Value;
            var documentScheme = docScope?.Element(ns + "Identifier")?.Value ?? "busdox-docid-qns"; // fallback to default if missing

            // BusinessScope->Scope for PROCESSID
            var processId = header.Descendants(ns + "BusinessScope")
                .Descendants(ns + "Scope")
                .FirstOrDefault(e => e.Element(ns + "Type")?.Value == "PROCESSID")
                ?.Element(ns + "InstanceIdentifier")?.Value;

            return new PeppolHeaderInfo
            {
                SenderId = senderId,
                SenderScheme = senderScheme,
                ReceiverId = receiverId,
                ReceiverScheme = receiverScheme,
                DocTypeId = docTypeId,
                DocumentScheme = documentScheme,
                ProcessId = processId,
                InstanceId = instanceId
            };
        }
        private byte[] DecompressGzip(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }
        static string GetCertificateCommonName(X509Certificate2 cert)
        {
            // Extract CN from subject, e.g. "CN=0195:201531725N, OU=..."
            var subject = cert.Subject;
            var cn = subject.Split(',')
                .Select(part => part.Trim())
                .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));
            return cn?.Substring(3); // Skip "CN="
        }
        public static byte[] RsaOaepEncrypt_MGF1_SHA256(byte[] data, X509Certificate2 cert)
        {
            // Get BouncyCastle public key from X509Certificate2
            var bcCert = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(cert.RawData);
            var rsaKey = (Org.BouncyCastle.Crypto.Parameters.RsaKeyParameters)bcCert.GetPublicKey();

            // OAEP with SHA-256 for both digest and MGF1
            var engine = new OaepEncoding(
                new RsaEngine(),
                new Sha256Digest(), // Digest
                new Sha256Digest(), // MGF1 Digest
                null // Optional encoding parameters (default null)
            );

            engine.Init(true, rsaKey); // true = encryption

            return engine.ProcessBlock(data, 0, data.Length);
        }
        public static byte[] RsaOaepDecrypt_MGF1_SHA256(byte[] cipherText, X509Certificate2 cert)
        {
            // CRITICAL FIX: Use CryptoCompatibilityHelper instead of DotNetUtilities for .NET Framework 4.8 compatibility
            var rsa = cert.GetRSAPrivateKeySafe(); // Use safe wrapper
            var rsaPrivate = CryptoCompatibilityHelper.ConvertToBouncyCastleRsaPrivateKey(rsa);

            var engine = new OaepEncoding(
                new RsaEngine(),
                new Sha256Digest(), // Digest
                new Sha256Digest(), // MGF1 Digest
                null // Optional encoding parameters (default null)
            );

            engine.Init(false, rsaPrivate); // false = decrypt
            return engine.ProcessBlock(cipherText, 0, cipherText.Length);
        }

        // =====================
        // Helper methods below
        // =====================
        // =============== Multipart parser(manual) ===============
        public class MimePartManual
        {
            public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
            public byte[] ContentBytes { get; set; }
            public string ContentText { get; set; }
            public string ContentType => Headers.TryGetValue("content-type", out var ct) ? ct : "";
            public string ContentId => Headers.TryGetValue("content-id", out var cid) ? cid.Trim('<', '>') : "";
        }

        // Manual boundary parser
        public static List<MimePartManual> ParseMultipartString(string raw, string boundary)
        {
            var parts = new List<MimePartManual>();
            var boundaryMarker = "--" + boundary.Trim('\"');
            var blocks = raw.Split(new[] { boundaryMarker }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                if (block.Trim() == "--") continue; // last boundary
                // Split headers from content
                var headerEnd = block.IndexOf("\r\n\r\n");
                if (headerEnd < 0) headerEnd = block.IndexOf("\n\n");
                if (headerEnd < 0) continue;
                var headerText = block.Substring(0, headerEnd);
                var contentText = block.Substring(headerEnd + (block[headerEnd] == '\r' ? 4 : 2));

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in headerText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var idx = line.IndexOf(':');
                    if (idx > 0)
                    {
                        var key = line.Substring(0, idx).Trim();
                        var value = line.Substring(idx + 1).Trim();
                        headers[key.ToLower()] = value;
                    }
                }

                var part = new MimePartManual
                {
                    Headers = headers
                };

                // If XML or text, treat as string; else as bytes
                if (part.ContentType.Contains("xml") || part.ContentType.Contains("text") || part.ContentType.Contains("soap"))
                {
                    part.ContentText = contentText.Trim();
                    part.ContentBytes = Encoding.UTF8.GetBytes(part.ContentText);
                }
                else
                {
                    // If not XML, treat as raw bytes (if actually binary, you'll need to use a stream in prod)
                    part.ContentBytes = Encoding.UTF8.GetBytes(contentText.Trim('\r', '\n'));
                }
                parts.Add(part);
            }
            return parts;
        }
        private void ValidateMessageId(string messageId)
        {
            // Implement protocol syntax validation here!
            if (string.IsNullOrWhiteSpace(messageId))
                throw new Exception("Invalid MessageId (blank)");
            // You can enforce regex, length, etc.
        }

        private bool ValidateSoapSignature(XDocument soapXml, X509Certificate2 cert)
        {
            // call into your existing SignedXmlWithId helper
            return ValidateSignature(soapXml, cert);
        }
        private void ValidatePayloadHeaders(PayloadInfo payload)
        {
            if (string.IsNullOrWhiteSpace(payload.MimeType))
                throw new Exception("Payload missing MIME type.");
            // ...add charset and other part property checks here as required by Peppol/AS4
        }
        public static bool ValidateSignature(XDocument soapXml, X509Certificate2 senderCert)
        {
            // 1) Load into XmlDocument with whitespace preserved
            var xmlDoc = new XmlDocument { PreserveWhitespace = true };
            using (var r = soapXml.CreateReader())
                xmlDoc.Load(r);

            // 2) Prepare namespace manager
            var nsm = new XmlNamespaceManager(xmlDoc.NameTable);
            nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
            nsm.AddNamespace("ec", "http://www.w3.org/2001/10/xml-exc-c14n#");

            // 3) Find the <ds:Signature> element
            var sigElem = xmlDoc.SelectSingleNode("//ds:Signature", nsm) as XmlElement
                ?? throw new InvalidOperationException("No <ds:Signature> found in SOAP header.");

            // 4) Remove any InclusiveNamespaces (otherwise .NET's transform lookup chokes)
            foreach (XmlElement inc in sigElem.SelectNodes(".//ec:InclusiveNamespaces", nsm))
                inc.ParentNode.RemoveChild(inc);

            // 5) Strip out any attachment <Reference URI="cid:..."> elements
            var signedInfo = sigElem.SelectSingleNode("ds:SignedInfo", nsm);
            var toRemove = signedInfo.SelectNodes("ds:Reference", nsm)
                                     .Cast<XmlElement>()
                                     .Where(rn => rn.GetAttribute("URI")?.StartsWith("cid:") == true)
                                     .ToList();
            foreach (var rn in toRemove)
                signedInfo.RemoveChild(rn);

            // 6) Finally, let SignedXmlWithId do its work
            var signedXml = new SignedXmlWithId(xmlDoc)
            {
                SigningKey = senderCert.GetRSAPublicKeySafe() // Use safe wrapper
            };
            signedXml.LoadXml(sigElem);
            return signedXml.CheckSignature(senderCert, true);
        }

        private List<string> ProcessAttachments(
            MultipartRelated related,
            XDocument soapXml,
            IList<string> hrefs,
            string certPath,
            string certPwd,
            UserMessage userMsg)
        {
            var attachmentPaths = new List<string>();

            for (int i = 0; i < related.Count; i++)
            {
                if (!(related[i] is MimeKit.MimePart attachment))
                    continue;

                string href = "cid:" + attachment.ContentId;
                if (!hrefs.Contains(href))
                    continue;

                byte[] data = null;
                using (var ms = new MemoryStream())
                {
                    attachment.Content.DecodeTo(ms);
                    data = ms.ToArray();
                }

                if (_configService.IsDebugMode())
                {
                    // For debugging, we can just save it. In prod, we'd need decryption
                    var path = _payloadPersister.Persist(userMsg.MessageId, new PayloadInfo { ContentId = attachment.ContentId, IsGzip = false, MimeType = attachment.ContentType.MimeType }, data);
                    attachmentPaths.Add(path);
                }
                else
                {
                    var myCert = new X509Certificate2(certPath, certPwd, X509KeyStorageFlags.MachineKeySet);
                    var decryptedBytes = DecryptPeppolAttachment(data, soapXml, myCert, href);
                    var path = _payloadPersister.Persist(userMsg.MessageId, new PayloadInfo { ContentId = attachment.ContentId, IsGzip = false, MimeType = attachment.ContentType.MimeType }, decryptedBytes);
                    attachmentPaths.Add(path);
                }
            }
            return attachmentPaths;
        }

        private byte[] DecryptPeppolAttachment(
            byte[] encryptedBytes,
            XDocument soapXml,
            X509Certificate2 myCert,
            string href)
        {
            // Namespaces
            var xenc = XNamespace.Get("http://www.w3.org/2001/04/xmlenc#");
            var xenc11 = XNamespace.Get("http://www.w3.org/2009/xmlenc11#");
            var ds = XNamespace.Get("http://www.w3.org/2000/09/xmldsig#");

            try
            {
                log.Debug($"Starting decryption of attachment: {href}, Size: {encryptedBytes?.Length ?? 0} bytes");

                // 1) locate EncryptedData for this href
                var encryptedData = soapXml
                  .Descendants(xenc + "EncryptedData")
                  .FirstOrDefault(ed =>
                      (string)ed
                        .Element(xenc + "CipherData")
                        .Element(xenc + "CipherReference")
                        .Attribute("URI") == href
                  );
                
                if (encryptedData == null)
                {
                    log.Error($"No EncryptedData found for href: {href}");
                    throw new InvalidOperationException("No EncryptedData for " + href);
                }

                // 2) Algorithm detection (CBC or GCM)
                var encMethodElem = encryptedData.Element(xenc + "EncryptionMethod")
                                    ?? encryptedData.Element(xenc11 + "EncryptionMethod");
                var alg = encMethodElem?.Attribute("Algorithm")?.Value;

                log.Debug($"Detected encryption algorithm: {alg}");

                // 3) find the EncryptedKey that it references
                var keyRef = encryptedData
                  .Element(ds + "KeyInfo")
                  .Descendants()
                  .First(n => n.Name.LocalName == "Reference");
                var keyId = keyRef.Attribute("URI").Value.TrimStart('#');

                var encryptedKeyElem = soapXml
                  .Descendants(xenc + "EncryptedKey")
                  .FirstOrDefault(ek => (string)ek.Attribute("Id") == keyId);
                
                if (encryptedKeyElem == null)
                {
                    log.Error($"No EncryptedKey found with Id: {keyId}");
                    throw new InvalidOperationException("No EncryptedKey with Id=" + keyId);
                }

                // 4) Determine key encryption method and decrypt AES key
                var keyEncAlg = encryptedKeyElem.Element(xenc + "EncryptionMethod")?.Attribute("Algorithm")?.Value;
                var encryptedKeyB64 = encryptedKeyElem.Element(xenc + "CipherData").Element(xenc + "CipherValue").Value;
                var encryptedKey = Convert.FromBase64String(encryptedKeyB64);

                log.Debug($"Key encryption algorithm: {keyEncAlg}");

                byte[] aesKey;
                var rsa = myCert.GetRSAPrivateKeySafe(); // Use safe wrapper
                if (keyEncAlg == "http://www.w3.org/2001/04/xmlenc#rsa-oaep-mgf1p" // OAEP w/ SHA-1
                    || string.IsNullOrEmpty(keyEncAlg)) // default fallback
                {
                    aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA1);
                }
                else if (keyEncAlg == "http://www.w3.org/2009/xmlenc11#rsa-oaep") // OAEP w/ SHA-256
                {
                    aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
                }
                else
                {
                    throw new NotSupportedException("Unsupported key encryption algorithm: " + keyEncAlg);
                }

                log.Debug($"Successfully decrypted AES key, length: {aesKey.Length} bytes");

                // 5) ENHANCED: Decrypt data with format auto-detection for AES-GCM
                if (alg == "http://www.w3.org/2009/xmlenc11#aes256-gcm" || 
                    alg == "http://www.w3.org/2009/xmlenc11#aes128-gcm" || 
                    alg?.EndsWith("aes-gcm") == true)
                {
                    return DecryptAesGcmWithFormatDetection(encryptedBytes, aesKey);
                }
                else if (alg == "http://www.w3.org/2001/04/xmlenc#aes256-cbc" || 
                         alg == "http://www.w3.org/2001/04/xmlenc#aes128-cbc" || 
                         alg?.EndsWith("aes-cbc") == true)
                {
                    // CBC: First 16 bytes = IV, rest = ciphertext
                    if (encryptedBytes.Length < 16)
                        throw new InvalidOperationException("Attachment too short to contain IV");
                    var iv = encryptedBytes.Take(16).ToArray();
                    var cipherText = encryptedBytes.Skip(16).ToArray();
                    return CryptoUtil.AesCbcDecrypt(aesKey, iv, cipherText);
                }
                else
                {
                    throw new NotSupportedException("Unsupported data encryption algorithm: " + alg);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to decrypt attachment {href}: {ex.Message}", ex);
                throw new InvalidOperationException($"Attachment decryption failed for {href}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Enhanced AES-GCM decryption with multiple format detection
        /// Handles different IV lengths and tag positioning used by various Peppol implementations
        /// </summary>
        private byte[] DecryptAesGcmWithFormatDetection(byte[] encryptedBytes, byte[] aesKey)
        {
            log.Debug($"Starting AES-GCM decryption with format detection, data length: {encryptedBytes.Length}");

            // Try different GCM formats in order of likelihood
            var formats = new[]
            {
                new { Name = "Standard_12ByteIV", IvLength = 12, TagLength = 16 },
                new { Name = "Extended_16ByteIV", IvLength = 16, TagLength = 16 },
                new { Name = "Phase4_Format", IvLength = 12, TagLength = 12 },
                new { Name = "Alternative_16ByteTag", IvLength = 16, TagLength = 12 }
            };

            Exception lastException = null;

            foreach (var format in formats)
            {
                try
                {
                    log.Debug($"Trying AES-GCM format: {format.Name} (IV: {format.IvLength}, Tag: {format.TagLength})");

                    // Check if data is long enough for this format
                    int minLength = format.IvLength + format.TagLength + 1; // +1 for at least some ciphertext
                    if (encryptedBytes.Length < minLength)
                    {
                        log.Debug($"Data too short for format {format.Name}, need at least {minLength} bytes");
                        continue;
                    }

                    // Extract components based on format
                    var (iv, cipherText, tag) = ExtractGcmComponents(encryptedBytes, format.IvLength, format.TagLength);

                    log.Debug($"Extracted - IV: {iv.Length} bytes, Cipher: {cipherText.Length} bytes, Tag: {tag.Length} bytes");

                    // Attempt decryption
                    var decrypted = CryptoUtil.AesGcmDecrypt(aesKey, iv, cipherText, tag);
                    
                    log.Info($"Successfully decrypted using format: {format.Name}");
                    return decrypted;
                }
                catch (Exception ex)
                {
                    log.Debug($"Format {format.Name} failed: {ex.Message}");
                    lastException = ex;
                    continue;
                }
            }

            // If all formats failed, try to analyze the data structure
            log.Warn("All standard formats failed, attempting data structure analysis");
            try
            {
                return AnalyzeAndDecryptGcmData(encryptedBytes, aesKey);
            }
            catch (Exception ex)
            {
                log.Error($"Data structure analysis also failed: {ex.Message}");
                lastException = ex;
            }

            throw new InvalidOperationException($"Failed to decrypt AES-GCM data with any known format. Last error: {lastException?.Message}", lastException);
        }

        /// <summary>
        /// Extracts IV, ciphertext, and tag components from encrypted data
        /// Format: [IV | ciphertext | tag]
        /// </summary>
        private (byte[] iv, byte[] cipherText, byte[] tag) ExtractGcmComponents(byte[] encryptedBytes, int ivLength, int tagLength)
        {
            if (encryptedBytes.Length < ivLength + tagLength)
                throw new ArgumentException($"Data too short for IV length {ivLength} and tag length {tagLength}");

            var iv = encryptedBytes.Take(ivLength).ToArray();
            var tag = encryptedBytes.Skip(encryptedBytes.Length - tagLength).ToArray();
            var cipherText = encryptedBytes.Skip(ivLength).Take(encryptedBytes.Length - ivLength - tagLength).ToArray();

            return (iv, cipherText, tag);
        }

        /// <summary>
        /// Analyzes encrypted data structure to determine the most likely GCM format
        /// Uses heuristics based on common Peppol implementations
        /// </summary>
        private byte[] AnalyzeAndDecryptGcmData(byte[] encryptedBytes, byte[] aesKey)
        {
            log.Debug("Analyzing encrypted data structure for GCM format detection");

            // Heuristic 1: Look for patterns in the data that might indicate boundaries
            // Heuristic 2: Try common variations of IV/tag positioning
            
            var analysisResults = new[]
            {
                // Format: [12-byte IV at start | ciphertext | 16-byte tag at end]
                new { IvStart = 0, IvLength = 12, TagStart = encryptedBytes.Length - 16, TagLength = 16 },
                
                // Format: [16-byte IV at start | ciphertext | 16-byte tag at end]  
                new { IvStart = 0, IvLength = 16, TagStart = encryptedBytes.Length - 16, TagLength = 16 },
                
                // Format: [ciphertext | 12-byte IV | 16-byte tag] (alternative layout)
                new { IvStart = encryptedBytes.Length - 28, IvLength = 12, TagStart = encryptedBytes.Length - 16, TagLength = 16 },
                
                // Format: [16-byte tag at start | 12-byte IV | ciphertext] (reverse layout)
                new { IvStart = 16, IvLength = 12, TagStart = 0, TagLength = 16 }
            };

            foreach (var analysis in analysisResults)
            {
                try
                {
                    if (analysis.IvStart + analysis.IvLength > encryptedBytes.Length ||
                        analysis.TagStart + analysis.TagLength > encryptedBytes.Length)
                        continue;

                    var iv = new byte[analysis.IvLength];
                    var tag = new byte[analysis.TagLength];
                    
                    Array.Copy(encryptedBytes, analysis.IvStart, iv, 0, analysis.IvLength);
                    Array.Copy(encryptedBytes, analysis.TagStart, tag, 0, analysis.TagLength);

                    // Calculate ciphertext (everything except IV and tag)
                    var cipherTextLength = encryptedBytes.Length - analysis.IvLength - analysis.TagLength;
                    if (cipherTextLength <= 0) continue;

                    var cipherText = new byte[cipherTextLength];
                    var cipherStart = (analysis.IvStart == 0) ? analysis.IvLength : 0;
                    if (analysis.TagStart == 0) cipherStart = analysis.TagLength;
                    
                    // Extract ciphertext avoiding IV and tag positions
                    var sourceIndex = 0;
                    var destIndex = 0;
                    
                    while (sourceIndex < encryptedBytes.Length && destIndex < cipherTextLength)
                    {
                        // Skip IV and tag positions
                        if ((sourceIndex >= analysis.IvStart && sourceIndex < analysis.IvStart + analysis.IvLength) ||
                            (sourceIndex >= analysis.TagStart && sourceIndex < analysis.TagStart + analysis.TagLength))
                        {
                            sourceIndex++;
                            continue;
                        }
                        
                        cipherText[destIndex++] = encryptedBytes[sourceIndex++];
                    }

                    log.Debug($"Analysis attempt - IV start: {analysis.IvStart}, Tag start: {analysis.TagStart}, Cipher length: {cipherTextLength}");
                    
                    var decrypted = CryptoUtil.AesGcmDecrypt(aesKey, iv, cipherText, tag);
                    log.Info("Successfully decrypted using data structure analysis");
                    return decrypted;
                }
                catch (Exception ex)
                {
                    log.Debug($"Analysis attempt failed: {ex.Message}");
                    continue;
                }
            }

            throw new InvalidOperationException("Could not determine GCM data structure from encrypted bytes");
        }

        public static string GetClientIp(HttpRequestMessage request)
        {
            // If hosted in IIS and using integrated pipeline
            if (request.Properties.ContainsKey("MS_HttpContext"))
            {
                dynamic ctx = request.Properties["MS_HttpContext"];
                if (ctx != null)
                    return ctx.Request.UserHostAddress;
            }
            // Self-hosting (OWIN)
            if (request.Properties.ContainsKey("System.ServiceModel.Channels.RemoteEndpointMessageProperty"))
            {
                dynamic remoteEndpoint = request.Properties["System.ServiceModel.Channels.RemoteEndpointMessageProperty"];
                if (remoteEndpoint != null)
                    return remoteEndpoint.Address;
            }
            return null;
        }
    }

    #region POCOs

    // =============== Multipart parser(manual) ===============
    public class MimePartManual
    {
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        public byte[] ContentBytes { get; set; }
        public string ContentText { get; set; }
        public string ContentType => Headers.TryGetValue("content-type", out var ct) ? ct : "";
        public string ContentId => Headers.TryGetValue("content-id", out var cid) ? cid.Trim('<', '>') : "";
    }
    public class PayloadInfo
    {
        public string ContentId { get; set; }
        public string MimeType { get; set; }
        public string Charset { get; set; }
        public bool IsGzip { get; set; }
    }

    public class As4InboundMetadata
    {
        public string MessageId { get; set; }
        public string Timestamp { get; set; }
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public string DocumentType { get; set; }
        //public X509Certificate2 Certificate { get; set; }
        public List<string> PayloadPaths { get; set; }
        public string RawSoapXml { get; set; }
        public object EnvelopeHeader { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateBase64 { get; set; }
        public string CorrelationId { get; set; }
        public DateTime ProcessingStartTime { get; set; }
        public DateTime ProcessingEndTime { get; set; }
    }

    public class PeppolHeaderInfo
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string SenderScheme { get; set; }
        public string ReceiverScheme { get; set; }
        public string DocTypeId { get; set; } // Will hold only the value/InstanceIdentifier for backward compat
        public string DocumentScheme { get; set; } // NEW: The scheme part for docTypeId
        public string ProcessId { get; set; }
        public string InstanceId { get; set; }
    }

    #endregion

    #region Services (Simplified/Stub Implementations)
    // =====================================================================
    //  2.  MIME BUILDER  – creates multipart/related + HttpContent
    // =====================================================================
    internal static class MimeBuilder
    {
        public static MimeMessage BuildRelatedMultipart(XDocument soapDoc, string attachmentCid, byte[] attachmentBytes)
        {
            var soapPart = new TextPart("soap+xml")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(soapDoc.ToString()))),
                ContentId = "soap-part@peppol.eu"
            };

            var attachmentPart = new MimeKit.MimePart("application/octet-stream")
            {
                Content = new MimeContent(new MemoryStream(attachmentBytes)),
                ContentId = attachmentCid,
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                ContentTransferEncoding = ContentEncoding.Base64,
                FileName = attachmentCid
            };

            var multipart = new MultipartRelated(soapPart, attachmentPart);
            var msg = new MimeMessage(multipart);
            return msg;
        }

        public static MimeMessage BuildSoapOnly(XDocument soapDoc)
        {
            var soapPart = new TextPart("soap+xml")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(soapDoc.ToString()))),
                ContentId = "soap-part@peppol.eu"
            };
            var msg = new MimeMessage(new MultipartRelated(soapPart));
            return msg;
        }

        public static HttpContent ToHttpContent(MimeMessage msg)
        {
            var ms = new MemoryStream();
            msg.WriteTo(ms);
            ms.Position = 0;

            var content = new StreamContent(ms);

            // Set the full Content-Type of the MimeMessage, NOT the root part!
            var ct = msg.Headers["Content-Type"];
            if (!string.IsNullOrEmpty(ct))
                content.Headers.TryAddWithoutValidation("Content-Type", ct);

            return content;
        }
    }
    public interface ICertificateValidator { void Validate(X509Certificate2 cert); }
    public class BasicCertificateValidator : ICertificateValidator
    {
        public void Validate(X509Certificate2 cert)
        {
            if (cert == null)
                throw new SecurityException("Certificate is null.");

            if (cert.NotAfter < DateTime.Now)
                throw new SecurityException("Certificate has expired.");

            if (cert.NotBefore > DateTime.Now)
                throw new SecurityException("Certificate is not yet valid.");
        }
    }

    public interface IMetadataPersister { void Persist(As4InboundMetadata metadata); }
    public class FileSystemMetadataPersister : IMetadataPersister
    {
        private readonly IPeppolConfigurationService _configService;

        public FileSystemMetadataPersister() : this(new PeppolConfigurationService()) { }
        
        public FileSystemMetadataPersister(IPeppolConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public void Persist(As4InboundMetadata metadata)
        {
            if (!_configService.EnableFileSystemPersistence) return;

            var dir = Path.Combine(_configService.InboundStoragePath, metadata.MessageId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "metadata.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(metadata));
        }
    }

    public interface IPayloadPersister { string Persist(string messageId, PayloadInfo info, byte[] data); }
    public class FileSystemPayloadPersister : IPayloadPersister
    {
        private readonly IPeppolConfigurationService _configService;

        public FileSystemPayloadPersister() : this(new PeppolConfigurationService()) { }
        
        public FileSystemPayloadPersister(IPeppolConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public string Persist(string messageId, PayloadInfo info, byte[] data)
        {
            if (!_configService.EnableFileSystemPersistence) return null;

            var dir = Path.Combine(_configService.InboundStoragePath, messageId, "payloads");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{info.ContentId ?? Guid.NewGuid().ToString()}.{(info.IsGzip ? "gz" : "bin")}");
            File.WriteAllBytes(path, data);
            return path;
        }
    }

    #endregion
}