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

namespace PeppolSG.API.Service
{
    public static class As4MessageBuilder
    {
        // PEPPOL / ebMS namespaces
        private static readonly XNamespace EB = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/";
        private static readonly XNamespace EBBP = "http://docs.oasis-open.org/ebxml-bp/ebbp-signals-2.0";
        // WS-Security / XML Signature / XML Encryption
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

        const string EbmsRole = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/role/ebms";

        public static XElement BuildUserMessage(
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
            var msg = new XElement(EB + "UserMessage",
                new XElement(EB + "MessageInfo",
                    new XElement(EB + "Timestamp", timestamp),
                    new XElement(EB + "MessageId", messageId)
                ),
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

            if (!string.IsNullOrWhiteSpace(partHref))
            {
                var props = new List<object>
                {
                    new XElement(EB + "Property",
                        new XAttribute("name", "MimeType"),
                        "application/xml")
                };
                if (compressed)
                    props.Add(new XElement(EB + "Property",
                        new XAttribute("name", "CompressionType"),
                        "application/gzip"));

                msg.Add(
                    new XElement(EB + "PayloadInfo",
                        new XElement(EB + "PartInfo",
                            new XAttribute("href", partHref),
                            new XElement(EB + "PartProperties", props)
                        )
                    )
                );
            }

            return msg;
        }

        public static XElement BuildSignalMessage(
            string timestamp,
            string messageId,
            string refToMessageId,
            IEnumerable<XElement> references = null)
        {
            var info = new XElement(EB + "MessageInfo",
                new XElement(EB + "Timestamp", timestamp),
                new XElement(EB + "MessageId", messageId),
                new XElement(EB + "RefToMessageId", refToMessageId)
            );

            XElement rr = null;
            if (references?.Any() == true)
            {
                rr = new XElement(EBBP + "NonRepudiationInformation",
                    references.Select(r =>
                        new XElement(EBBP + "MessagePartNRInformation", r)
                    )
                );
            }

            var receipt = rr != null
                ? new XElement(EB + "Receipt", rr)
                : new XElement(EB + "Receipt");

            return new XElement(EB + "SignalMessage", info, receipt);
        }

        public static XElement BuildMessaging(XElement messageOrSignal, string messagingId = null)
        {
            var el = new XElement(EB + "Messaging",
                new XAttribute(XNamespace.Xmlns + "ds", DS.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "eb", EB.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ebbp", EBBP.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ns2", NS2.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ns3", NS3.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xlink", XLINK.NamespaceName)
            );

            if (!string.IsNullOrWhiteSpace(messagingId))
                el.Add(new XAttribute(WSU + "Id", messagingId));

            el.Add(messageOrSignal);
            return el;
        }

        public static XDocument WrapInSoapEnvelope(XElement securityHeader, XElement messaging, string bodyId)
        {
            var hdr = new List<object>();
            if (securityHeader != null) hdr.Add(securityHeader);
            hdr.Add(messaging);

            var body = new XElement(S12 + "Body");
            if (!string.IsNullOrWhiteSpace(bodyId))
                body.Add(
                    new XAttribute(XNamespace.Xmlns + "wsu", WSU),
                    new XAttribute(WSU + "Id", bodyId));

            return new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement(S12 + "Envelope",
                    new XAttribute(XNamespace.Xmlns + "S12", S12.NamespaceName),
                    new XElement(S12 + "Header", hdr),
                    body
                )
            );
        }

        public static XElement BuildWsseSecurity(X509Certificate2 cert, out string bstId)
        {
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

            // wrap in Security
            return new XElement(WSSE + "Security",
                new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(S12 + "mustUnderstand", "1"),
                new XAttribute(S12 + "role", EbmsRole),
                bst,
                // you'll insert <xenc:EncryptedKey>, <xenc:EncryptedData> and a <ds:Signature> here later
                new XElement(DS + "Signature")
            );
        }

        public static XElement BuildEncryptedKey(
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

        public static XElement BuildEncryptedData(
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
        public static XElement BuildBinarySecurityToken(X509Certificate2 cert, string bstId)
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
        public static XElement BuildSecurityHeader(
                   XElement recipientBstEl,
                   XElement encryptedKeyEl,
                   XElement encryptedDataEl,
                   XElement senderBstEl)
        {
            return new XElement(WSSE + "Security",
                // namespace declarations
                new XAttribute(XNamespace.Xmlns + "wsse", WSSE.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "wsu", WSU.NamespaceName),
                new XAttribute(S12 + "mustUnderstand", "1"),
                new XAttribute(S12 + "role", EbmsRole),
                // include tokens and encryption elements in order
                recipientBstEl,
                encryptedKeyEl,
                encryptedDataEl,
                senderBstEl,
                // placeholder Signature element
                new XElement(DS + "Signature")
            );
        }

        public static XElement BuildErrorMessaging(string messageId, Exception ex, string refToMsgId = null)
        {
            var msgInfo = new XElement(EB + "MessageInfo",
                    new XElement(EB + "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                    new XElement(EB + "MessageId", messageId));
            if (!string.IsNullOrEmpty(refToMsgId))
                msgInfo.Add(new XElement(EB + "RefToMessageId", refToMsgId));

            var error = new XElement(EB + "Error",
                new XAttribute("origin", "ebMS"),
                new XAttribute("category", "Content"),
                new XAttribute("errorCode", "EBMS:0004"),
                new XAttribute("severity", "failure"),
                new XAttribute("shortDescription", "Processing failure"),
                new XElement(EB + "Description",
                    new XAttribute(XNamespace.Xml + "lang", "en"),
                    ex.ToString()));

            return new XElement(EB + "Messaging",
                new XAttribute(XNamespace.Xmlns + "eb", EB.NamespaceName),
                new XElement(EB + "SignalMessage", msgInfo, error));
        }

        public static HttpResponseMessage CreateMtomResponse(
    XDocument soapEnvelope,
    IList<Attachment> attachments,
    HttpStatusCode statusCode)
        {
            // 1) Create a "multipart/related" container with a random boundary
            var boundary = "----=_Part_" + Guid.NewGuid().ToString("N");
            var multipart = new MultipartContent("related", boundary);

            // 2) Add the SOAP part
            var xml = soapEnvelope.Declaration + soapEnvelope.ToString(SaveOptions.DisableFormatting);
            var soapContent = new StringContent(xml, Encoding.UTF8, "application/soap+xml");
            // indicate the root part
            //soapContent.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("type", "\"application/soap+xml\""));
            soapContent.Headers.Add("Content-Transfer-Encoding", "binary");
            soapContent.Headers.Add("Content-ID", "<root.message>");
            multipart.Add(soapContent);

            // 3) Add each binary attachment
            foreach (var att in attachments)
            {
                var bin = new ByteArrayContent(att.Bytes);
                bin.Headers.ContentType = MediaTypeHeaderValue.Parse(att.ContentType);
                bin.Headers.Add("Content-Transfer-Encoding", "binary");
                bin.Headers.Add("Content-ID", $"<{att.ContentId}>");
                multipart.Add(bin);
            }

            // 4) Wrap into HttpResponseMessage
            var resp = new HttpResponseMessage(statusCode)
            {
                Content = multipart
            };
            // set the overall Content-Type header
            resp.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/related; boundary=\"{boundary}\"; type=\"application/soap+xml\"");
            return resp;
        }
        public class Attachment
        {
            public string ContentId { get; set; }
            public string ContentType { get; set; }
            public byte[] Bytes { get; set; }
        }

    }
}