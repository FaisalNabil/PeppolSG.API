using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;
using System.Xml.Linq;
using System.Security.Cryptography.Xml;
using PeppolSG.API.Service.Interfaces;
using PeppolSG.API.Models;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// AS4 Message Builder for Peppol Access Point
    /// Implements eDelivery AS4 Profile v1.1.0 and Peppol AS4 Profile v2.0.3
    /// </summary>
    public class As4MessageBuilder : IAs4MessageBuilder
    {
        private readonly IPeppolConfigurationService _config;

        public As4MessageBuilder(IPeppolConfigurationService config)
        {
            _config = config;
        }

        // PEPPOL / ebMS namespaces
        private static readonly XNamespace EB = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/";
        private static readonly XNamespace EBBP = "http://docs.oasis-open.org/ebxml-bp/ebbp-signals-2.0";
        
        // WS-Security / XML Signature / XML Encryption (WS-Security 1.1.1)
        private static readonly XNamespace WSSE = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        private static readonly XNamespace WSSE11 = "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd";
        private static readonly XNamespace WSU = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        private static readonly XNamespace DS = SignedXml.XmlDsigNamespaceUrl;
        private static readonly XNamespace XENC = "http://www.w3.org/2001/04/xmlenc#";
        private static readonly XNamespace XENC11 = "http://www.w3.org/2009/xmlenc11#";
        
        // SOAP / XLink
        private static readonly XNamespace S12 = "http://www.w3.org/2003/05/soap-envelope";
        private static readonly XNamespace XLINK = "http://www.w3.org/1999/xlink";
        private static readonly XNamespace NS2 = "http://schemas.xmlsoap.org/soap/envelope/";
        private static readonly XNamespace NS3 = "http://www.w3.org/2003/05/soap-envelope";

        // Standard Business Document Header
        private static readonly XNamespace SBDH = "http://www.unece.org/cefact/namespaces/StandardBusinessDocumentHeader";

        // ebMS Role constant
        const string EbmsRole = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/role/ebms";

        /// <summary>
        /// Builds UserMessage according to Peppol AS4 Profile v2.0.3
        /// </summary>
        public XElement BuildUserMessage(
            string messageId,
            string timestamp,
            string conversationId,
            string senderId,
            string receiverId,
            string docTypeId,
            string processId,
            string partHref,
            string originalReceiverId,
            string originalSenderId,
            bool compressed)
        {
            // Validate required parameters
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));
            if (string.IsNullOrEmpty(conversationId)) throw new ArgumentNullException(nameof(conversationId));

            var msg = new XElement(EB + "UserMessage",
                // MessageInfo - Required by AS4 Profile
                new XElement(EB + "MessageInfo",
                    new XElement(EB + "Timestamp", timestamp),
                    new XElement(EB + "MessageId", messageId)
                ),
                
                // PartyInfo - Peppol AP identifiers
                new XElement(EB + "PartyInfo",
                    new XElement(EB + "From",
                        new XElement(EB + "PartyId",
                            new XAttribute("type", "urn:fdc:peppol.eu:2017:identifiers:ap"),
                            senderId
                        ),
                        new XElement(EB + "Role",
                            "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/initiator")
                    ),
                    new XElement(EB + "To",
                        new XElement(EB + "PartyId",
                            new XAttribute("type", "urn:fdc:peppol.eu:2017:identifiers:ap"),
                            receiverId
                        ),
                        new XElement(EB + "Role",
                            "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/responder")
                    )
                ),
                
                // CollaborationInfo - Peppol specific values
                new XElement(EB + "CollaborationInfo",
                    new XElement(EB + "AgreementRef",
                        "urn:fdc:peppol.eu:2017:agreements:tia:ap_provider"),
                    new XElement(EB + "Service",
                        new XAttribute("type", "cenbii-procid-ubl"),
                        processId
                    ),
                    new XElement(EB + "Action", docTypeId),
                    new XElement(EB + "ConversationId", conversationId)
                ),
                
                // MessageProperties - Peppol Four Corner Model
                new XElement(EB + "MessageProperties",
                    new XElement(EB + "Property",
                        new XAttribute("name", "originalSender"),
                        new XAttribute("type", "iso6523-actorid-upis"),
                        originalSenderId
                    ),
                    new XElement(EB + "Property",
                        new XAttribute("name", "finalRecipient"),
                        new XAttribute("type", "iso6523-actorid-upis"),
                        originalReceiverId
                    )
                )
            );

            // PayloadInfo - Only if attachment present
            if (!string.IsNullOrWhiteSpace(partHref))
            {
                var partProperties = new List<XElement>
                {
                    new XElement(EB + "Property",
                        new XAttribute("name", "MimeType"),
                        "application/xml"),
                    new XElement(EB + "Property",
                        new XAttribute("name", "CharacterSet"),
                        "UTF-8")
                };

                if (compressed)
                {
                    partProperties.Add(new XElement(EB + "Property",
                        new XAttribute("name", "CompressionType"),
                        "application/gzip"));
                }

                msg.Add(
                    new XElement(EB + "PayloadInfo",
                        new XElement(EB + "PartInfo",
                            new XAttribute("href", partHref),
                            new XElement(EB + "PartProperties", partProperties)
                        )
                    )
                );
            }

            return msg;
        }

        /// <summary>
        /// Builds SignalMessage (Receipt) according to AS4 Profile
        /// Enhanced for Peppol AS4 Profile v2.0.3 NonRepudiationInformation support
        /// </summary>
        public XElement BuildSignalMessage(
            string timestamp,
            string messageId,
            string refToMessageId,
            IEnumerable<XElement> signatureReferences = null)
        {
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrEmpty(refToMessageId)) throw new ArgumentNullException(nameof(refToMessageId));

            var messageInfo = new XElement(EB + "MessageInfo",
                new XElement(EB + "Timestamp", timestamp),
                new XElement(EB + "MessageId", messageId),
                new XElement(EB + "RefToMessageId", refToMessageId)
            );

            // Build Receipt with NonRepudiationInformation for Peppol AS4 Profile v2.0.3 compliance
            XElement receipt;
            if (signatureReferences?.Any() == true)
            {
                // Create NonRepudiationInformation with MessagePartNRInformation elements
                var nrInfo = new XElement(EBBP + "NonRepudiationInformation",
                    signatureReferences.Select(refElement =>
                        new XElement(EBBP + "MessagePartNRInformation", refElement)
                    )
                );
                receipt = new XElement(EB + "Receipt", nrInfo);
            }
            else
            {
                // Fallback for backward compatibility - empty receipt
                receipt = new XElement(EB + "Receipt");
            }

            return new XElement(EB + "SignalMessage", messageInfo, receipt);
        }

        /// <summary>
        /// Builds ebMS3 Error Message for AS4 failures
        /// </summary>
        public XElement BuildErrorMessage(
            string timestamp,
            string messageId,
            string refToMessageId,
            string errorCode,
            string severity,
            string description,
            string shortDescription = null,
            string origin = "ebMS",
            string category = "Content")
        {
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));
            if (string.IsNullOrEmpty(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrEmpty(errorCode)) throw new ArgumentNullException(nameof(errorCode));

            var info = new XElement(EB + "MessageInfo",
                new XElement(EB + "Timestamp", timestamp),
                new XElement(EB + "MessageId", messageId)
            );

            if (!string.IsNullOrEmpty(refToMessageId))
            {
                info.Add(new XElement(EB + "RefToMessageId", refToMessageId));
            }

            var error = new XElement(EB + "Error",
                new XAttribute("origin", origin),
                new XAttribute("category", category),
                new XAttribute("errorCode", errorCode),
                new XAttribute("severity", severity)
            );

            if (!string.IsNullOrEmpty(shortDescription))
            {
                error.Add(new XAttribute("shortDescription", shortDescription));
            }

            if (!string.IsNullOrEmpty(description))
            {
                error.Add(new XElement(EB + "Description",
                    new XAttribute(XNamespace.Xml + "lang", "en"),
                    description));
            }

            return new XElement(EB + "SignalMessage", info, error);
        }

        /// <summary>
        /// Builds Messaging element with proper namespace declarations
        /// </summary>
        public XElement BuildMessaging(XElement messageOrSignal, string messagingId = null)
        {
            var messaging = new XElement(EB + "Messaging",
                // Required namespace declarations for eDelivery AS4 Profile
                new XAttribute(XNamespace.Xmlns + "eb", EB.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ds", DS.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ebbp", EBBP.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xlink", XLINK.NamespaceName)
            );

            if (!string.IsNullOrWhiteSpace(messagingId))
                messaging.Add(new XAttribute(WSU + "Id", messagingId));

            messaging.Add(messageOrSignal);
            return messaging;
        }

        /// <summary>
        /// Builds complete SOAP envelope with WS-Security header
        /// Implementation for eDelivery AS4 Profile v1.1.0
        /// </summary>
        public XDocument BuildSoapEnvelope(XElement messaging, XElement wsSecurityHeader, string bodyId)
        {
            if (messaging == null) throw new ArgumentNullException(nameof(messaging));

            var headerElements = new List<XElement> { messaging };
            if (wsSecurityHeader != null)
            {
                headerElements.Insert(0, wsSecurityHeader); // WS-Security should come first
            }

            var body = new XElement(S12 + "Body");
            if (!string.IsNullOrWhiteSpace(bodyId))
            {
                body.Add(
                    new XAttribute(XNamespace.Xmlns + "wsu", WSU),
                    new XAttribute(WSU + "Id", bodyId)
                );
            }

            var envelope = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(S12 + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "S12", S12.NamespaceName),
                    new XElement(S12 + "Header", headerElements),
                    body
                )
            );

            return envelope;
        }

        /// <summary>
        /// Legacy method for backward compatibility
        /// </summary>
        public XDocument WrapInSoapEnvelope(XElement securityHeader, XElement messaging, string bodyId)
        {
            var headerElements = new List<object>();
            if (securityHeader != null) headerElements.Add(securityHeader);
            headerElements.Add(messaging);

            var body = new XElement(S12 + "Body");
            if (!string.IsNullOrWhiteSpace(bodyId))
            {
                body.Add(
                    new XAttribute(XNamespace.Xmlns + "wsu", WSU),
                    new XAttribute(WSU + "Id", bodyId)
                );
            }

            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(S12 + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "S12", S12.NamespaceName),
                    new XElement(S12 + "Header", headerElements),
                    body
                )
            );
        }

        /// <summary>
        /// Builds WS-Security header with timestamp token for WS-Security 1.1.1 compliance
        /// Used by the AS4 controller for proper message signing
        /// Enhanced for Phase4/WSS4J compatibility
        /// </summary>
        public XElement BuildWsSecurityHeader(
            XElement messaging,
            X509Certificate2 signingCert,
            string timestamp,
            string messagingId)
        {
            if (messaging == null) throw new ArgumentNullException(nameof(messaging));
            if (signingCert == null) throw new ArgumentNullException(nameof(signingCert));
            if (string.IsNullOrEmpty(timestamp)) throw new ArgumentNullException(nameof(timestamp));

            // Generate unique IDs
            var timestampId = "TS-" + Guid.NewGuid().ToString("N");
            var bstId = "BST-" + Guid.NewGuid().ToString("N");

            // Build Timestamp token (required by WS-Security 1.1.1)
            var timestampElement = new XElement(WSU + "Timestamp",
                new XAttribute(WSU + "Id", timestampId),
                new XElement(WSU + "Created", timestamp),
                new XElement(WSU + "Expires", 
                    DateTime.Parse(timestamp).AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            );

            // Build Binary Security Token
            var binarySecurityToken = new XElement(WSSE + "BinarySecurityToken",
                new XAttribute("EncodingType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                new XAttribute("ValueType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3"),
                new XAttribute(WSU + "Id", bstId),
                Convert.ToBase64String(signingCert.RawData)
            );

            // CRITICAL FIX: Build WS-Security header with Phase4/WSS4J compatibility
            var wsSecurityHeader = new XElement(WSSE + "Security",
                // Namespace declarations - critical for WSS4J processing
                new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsse11", WSSE11.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                
                // SOAP mustUnderstand attribute
                new XAttribute(S12 + "mustUnderstand", "1"),
                
                // Peppol-specific role attribute for AS4
                new XAttribute(S12 + "role", EbmsRole),
                
                // CRITICAL: Element ordering must be correct for WSS4J
                // 1. Timestamp first (WSS4J expects this order)
                timestampElement,
                
                // 2. BinarySecurityToken second
                binarySecurityToken
                
                // Signature will be added later by the signing process
            );

            return wsSecurityHeader;
        }

        /// <summary>
        /// Builds Standard Business Document Header (SBDH) for Peppol documents
        /// Implements SBDH v1.3 for Peppol Business Interoperability Specifications
        /// </summary>
        public XElement BuildSbdh(
            string senderId,
            string receiverId,
            string docTypeId,
            string processId,
            string instanceId,
            string creationDateTime)
        {
            if (string.IsNullOrEmpty(senderId)) throw new ArgumentNullException(nameof(senderId));
            if (string.IsNullOrEmpty(receiverId)) throw new ArgumentNullException(nameof(receiverId));
            if (string.IsNullOrEmpty(docTypeId)) throw new ArgumentNullException(nameof(docTypeId));

            return new XElement(SBDH + "StandardBusinessDocumentHeader",
                new XAttribute(XNamespace.Xmlns + "sbdh", SBDH.NamespaceName),
                
                // Header Version
                new XElement(SBDH + "HeaderVersion", "1.0"),
                
                // Sender information
                new XElement(SBDH + "Sender",
                    new XElement(SBDH + "Identifier",
                        new XAttribute("Authority", "iso6523-actorid-upis"),
                        senderId)),
                
                // Receiver information
                new XElement(SBDH + "Receiver",
                    new XElement(SBDH + "Identifier",
                        new XAttribute("Authority", "iso6523-actorid-upis"),
                        receiverId)),
                
                // Document identification
                new XElement(SBDH + "DocumentIdentification",
                    new XElement(SBDH + "Standard", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"),
                    new XElement(SBDH + "TypeVersion", "2.1"),
                    new XElement(SBDH + "InstanceIdentifier", instanceId ?? Guid.NewGuid().ToString()),
                    new XElement(SBDH + "Type", "Invoice"),
                    new XElement(SBDH + "CreationDateAndTime", 
                        creationDateTime ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))),
                
                // Business scope with Peppol identifiers
                new XElement(SBDH + "BusinessScope",
                    new XElement(SBDH + "Scope",
                        new XElement(SBDH + "Type", "DOCUMENTID"),
                        new XElement(SBDH + "Identifier", "busdox-docid-qns"),
                        new XElement(SBDH + "InstanceIdentifier", docTypeId)),
                    new XElement(SBDH + "Scope",
                        new XElement(SBDH + "Type", "PROCESSID"),
                        new XElement(SBDH + "Identifier", "cenbii-procid-ubl"),
                        new XElement(SBDH + "InstanceIdentifier", processId))
                )
            );
        }

        public XElement BuildWsseSecurity(X509Certificate2 cert, out string bstId)
        {
            // CRITICAL FIX: Generate Timestamp for Phase4/WSS4J compatibility
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var timestampId = "TS-" + Guid.NewGuid().ToString("N");
            var timestampElement = new XElement(WSU + "Timestamp",
                new XAttribute(WSU + "Id", timestampId),
                new XElement(WSU + "Created", timestamp),
                new XElement(WSU + "Expires", 
                    DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            );

            // create BST
            bstId = "X509-" + Guid.NewGuid().ToString("N");
            var bst = new XElement(WSSE + "BinarySecurityToken",
                new XAttribute("EncodingType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                new XAttribute("ValueType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3"),
                new XAttribute(WSU + "Id", bstId),
                Convert.ToBase64String(cert.RawData)
            );

            // wrap in Security with Phase4/WSS4J compatible element ordering
            return new XElement(WSSE + "Security",
                new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsse11", WSSE11.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(S12 + "mustUnderstand", "1"),
                new XAttribute(S12 + "role", EbmsRole),
                
                // CRITICAL: Element ordering must be correct for WSS4J
                // 1. Timestamp first (WSS4J expects this order)
                timestampElement,
                
                // 2. BinarySecurityToken second
                bst,
                
                // you'll insert <xenc:EncryptedKey>, <xenc:EncryptedData> and a <ds:Signature> here later
                new XElement(DS + "Signature")
            );
        }

        public XElement BuildEncryptedKey(
            string ekId, string bstToRefId, string encryptedKeyB64, string dataRefId)
        {
            return new XElement(XENC + "EncryptedKey",
                new XAttribute(XNamespace.Xmlns + "xenc", XENC.NamespaceName),
                new XAttribute("Id", ekId),
                new XElement(XENC + "EncryptionMethod",
                    new XAttribute("Algorithm", XENC11.NamespaceName + "rsa-oaep"),
                    new XElement(DS + "DigestMethod",
                        new XAttribute("Algorithm", XENC.NamespaceName + "sha256")),
                    new XElement(XENC11 + "MGF",
                        new XAttribute("Algorithm", XENC11.NamespaceName + "mgf1sha256"))
                ),
                new XElement(DS + "KeyInfo",
                    new XElement(WSSE + "SecurityTokenReference",
                        new XElement(WSSE + "Reference",
                            new XAttribute("URI", "#" + bstToRefId),
                            new XAttribute("ValueType",
                                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3")
                        )
                    )
                ),
                new XElement(XENC + "CipherData",
                    new XElement(XENC + "CipherValue", encryptedKeyB64)
                ),
                new XElement(XENC + "ReferenceList",
                    new XElement(XENC + "DataReference",
                        new XAttribute("URI", "#" + dataRefId)
                    )
                )
            );
        }

        public XElement BuildEncryptedData(
            string edId, string ekId, string attachmentCid, bool gzip = true)
        {
            var attrs = new List<XAttribute>
            {
                new XAttribute("Id", edId),
                new XAttribute(XNamespace.Xmlns + "xenc", XENC.NamespaceName)
            };
            if (gzip)
            {
                attrs.Add(new XAttribute("MimeType", "application/gzip"));
                attrs.Add(new XAttribute("Type",
                    "http://docs.oasis-open.org/wss/oasis-wss-SwAProfile-1.1#Attachment-Content-Only"));
            }

            return new XElement(XENC + "EncryptedData", attrs,
                new XElement(XENC + "EncryptionMethod",
                    new XAttribute("Algorithm", XENC11.NamespaceName + "aes128-gcm")
                ),
                new XElement(DS + "KeyInfo",
                    new XElement(WSSE + "SecurityTokenReference",
                        new XAttribute(XNamespace.Xmlns + "wsse11", WSSE11.NamespaceName),
                        new XAttribute(WSU + "TokenType",
                            "http://docs.oasis-open.org/wss/oasis-wss-soap-message-security-1.1#EncryptedKey"),
                        new XElement(WSSE + "Reference",
                            new XAttribute("URI", "#" + ekId)
                        )
                    )
                ),
                new XElement(XENC + "CipherData",
                    new XElement(XENC + "CipherReference",
                        new XAttribute("URI", "cid:" + attachmentCid),
                        new XElement(XENC + "Transforms",
                            new XElement(DS + "Transform",
                                new XAttribute("Algorithm",
                                    "http://docs.oasis-open.org/wss/oasis-wss-SwAProfile-1.1#Attachment-Ciphertext-Transform")
                            )
                        )
                    )
                )
            );
        }
        public XElement BuildBinarySecurityToken(X509Certificate2 cert, string bstId)
        {
            return new XElement(WSSE + "BinarySecurityToken",
                // mandatory attributes per OASIS X509 Token Profile
                new XAttribute("EncodingType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                new XAttribute("ValueType",
                    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3"),
                // assign the wsu:Id so Signature KeyInfo can reference it
                new XAttribute(WSU + "Id", bstId),
                // embed the raw Base64 of the DER-encoded certificate
                Convert.ToBase64String(cert.RawData)
            );
        }
        public XElement BuildSecurityHeader(
                   XElement recipientBstEl,
                   XElement encryptedKeyEl,
                   XElement encryptedDataEl,
                   XElement senderBstEl)
        {
            // CRITICAL FIX: Generate Timestamp for Phase4/WSS4J compatibility
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var timestampId = "TS-" + Guid.NewGuid().ToString("N");
            var timestampElement = new XElement(WSU + "Timestamp",
                new XAttribute(WSU + "Id", timestampId),
                new XElement(WSU + "Created", timestamp),
                new XElement(WSU + "Expires", 
                    DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
            );

            return new XElement(WSSE + "Security",
                // namespace declarations
                new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsse11", WSSE11.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(S12 + "mustUnderstand", "1"),
                new XAttribute(S12 + "role", EbmsRole),
                
                // CRITICAL: Element ordering must be correct for WSS4J
                // 1. Timestamp first (WSS4J expects this order)
                timestampElement,
                
                // 2. Recipient BST (for encryption)
                recipientBstEl,
                
                // 3. EncryptedKey (referencing recipient BST)
                encryptedKeyEl,
                
                // 4. EncryptedData (referencing attachment)
                encryptedDataEl,
                
                // 5. Sender BST (for signing)
                senderBstEl,
                //IMPORTANT: If signature is not added, later shows error
                new XElement(DS + "Signature")
            );
        }

        /// <summary>
        /// Enhanced multipart message creation with proper AS4 structure
        /// Implements eDelivery AS4 Profile v1.1.0 multipart/related requirements
        /// </summary>
        public HttpResponseMessage CreateMtomResponse(
            XDocument soapEnvelope,
            IList<As4Attachment> attachments,
            HttpStatusCode statusCode)
        {
            if (soapEnvelope == null) throw new ArgumentNullException(nameof(soapEnvelope));

            // Generate boundary for multipart/related
            var boundary = "----=_Part_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

            // 1. Add SOAP part as root part
            var soapXml = soapEnvelope.Declaration?.ToString() + soapEnvelope.ToString(SaveOptions.DisableFormatting);
            var soapContent = new StringContent(soapXml, Encoding.UTF8, "application/soap+xml");
            
            // Set proper headers for SOAP part according to eDelivery AS4 Profile
            soapContent.Headers.ContentType.CharSet = "UTF-8";
            soapContent.Headers.Add("Content-Transfer-Encoding", "8bit");
            soapContent.Headers.Add("Content-ID", "<root.message@cxf.apache.org>");
            
            multipart.Add(soapContent);

            // 2. Add binary attachments
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var binaryContent = new ByteArrayContent(attachment.Bytes);
                    binaryContent.Headers.ContentType = MediaTypeHeaderValue.Parse(attachment.ContentType);
                    binaryContent.Headers.Add("Content-Transfer-Encoding", "binary");
                    binaryContent.Headers.Add("Content-ID", $"<{attachment.ContentId}>");
                    
                    multipart.Add(binaryContent);
                }
            }

            // 3. Create HTTP response
            var response = new HttpResponseMessage(statusCode)
            {
                Content = multipart
            };

            // Set proper Content-Type header for AS4 multipart/related
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
                $"multipart/related; boundary=\"{boundary}\"; " +
                $"type=\"application/soap+xml\"; " +
                $"start=\"<root.message@cxf.apache.org>\""
            );

            return response;
        }


    }
}