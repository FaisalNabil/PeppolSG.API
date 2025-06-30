using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Performs enhanced validation of incoming AS4 SOAP envelopes and MIME parts.
    /// Throws detailed exceptions if validation fails.
    /// </summary>
    public class MessageValidationService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MessageValidationService));

        private readonly int _maxSoapSizeKb;
        private readonly int _maxAttachmentSizeMb;
        private readonly int _maxTotalAttachmentsMb;
        private readonly HashSet<string> _allowedNamespaces;

        public MessageValidationService()
        {
            _maxSoapSizeKb = int.Parse(ConfigurationManager.AppSettings["MaxSoapSizeKb"] ?? "512");
            _maxAttachmentSizeMb = int.Parse(ConfigurationManager.AppSettings["MaxAttachmentSizeMb"] ?? "50");
            _maxTotalAttachmentsMb = int.Parse(ConfigurationManager.AppSettings["MaxTotalAttachmentsMb"] ?? "200");

            // Allowed XML Namespaces based on ebMS 3.0 Core + WS-Security + SOAP 1.2 + XMLENC.
            _allowedNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/",
                "http://www.w3.org/2003/05/soap-envelope",
                "http://schemas.xmlsoap.org/soap/envelope/",
                "http://www.w3.org/2001/04/xmlenc#",
                "http://www.w3.org/2009/xmlenc11#",
                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd",
                "http://docs.oasis-open.org/wss/oasis-wss-wssecurity-secext-1.1.xsd",
                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd",
                "http://www.w3.org/2000/09/xmldsig#",
                "http://www.w3.org/1999/xlink",
                "http://docs.oasis-open.org/ebxml-bp/ebbp-signals-2.0"
            };
        }

        #region Public API
        /// <summary>
        /// Validates SOAP envelope, namespaces, message size and UserMessage properties.
        /// </summary>
        /// <param name="soapDoc">Parsed SOAP envelope</param>
        /// <param name="mimeParts">Full list of MIME parts (including SOAP part)</param>
        public void Validate(XDocument soapDoc, IList<As4Controller.MimePartManual> mimeParts)
        {
            if (soapDoc == null) throw new ArgumentNullException(nameof(soapDoc));
            if (mimeParts == null) throw new ArgumentNullException(nameof(mimeParts));

            ValidateSoapSize(soapDoc);
            ValidateNamespaces(soapDoc);
            ValidateSoapHeader(soapDoc);
            ValidateUserMessage(soapDoc);
            ValidatePayloadConsistency(soapDoc, mimeParts);
            ValidatePartyIdSchemes(soapDoc);
            ValidateAttachmentSizes(mimeParts);
        }
        #endregion

        #region Private Validation Methods

        private void ValidateSoapSize(XDocument soapDoc)
        {
            var sb = new StringBuilder();
            using (var sw = new StringWriter(sb))
                soapDoc.Save(sw);
            var sizeKb = Encoding.UTF8.GetByteCount(sb.ToString()) / 1024.0;
            if (sizeKb > _maxSoapSizeKb)
            {
                throw new Exception($"SOAP envelope size {sizeKb:F1} KB exceeds limit of {_maxSoapSizeKb} KB.");
            }
            log.Debug($"SOAP size check OK: {sizeKb:F1} KB <= {_maxSoapSizeKb} KB");
        }

        private void ValidateNamespaces(XDocument soapDoc)
        {
            var disallowed = soapDoc.Descendants()
                .Select(el => el.Name.NamespaceName)
                .Where(ns => !string.IsNullOrEmpty(ns) && !_allowedNamespaces.Contains(ns))
                .Distinct()
                .ToList();
            if (disallowed.Any())
            {
                throw new Exception("Disallowed namespaces detected: " + string.Join(", ", disallowed));
            }
            log.Debug("Namespace validation passed.");
        }

        private void ValidateSoapHeader(XDocument soapDoc)
        {
            var soapNs = soapDoc.Root.Name.Namespace;
            var header = soapDoc.Root.Element(soapNs + "Header");
            if (header == null)
                throw new Exception("SOAP Header missing.");

            var messaging = header.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Messaging");
            if (messaging == null)
                throw new Exception("<Messaging> element missing in SOAP Header.");

            var userMessage = messaging.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "UserMessage");
            if (userMessage == null)
                throw new Exception("<UserMessage> element missing in <Messaging>.");

            // MUST contain MessageInfo, PartyInfo and CollaborationInfo
            if (!userMessage.Elements().Any(e => e.Name.LocalName == "MessageInfo"))
                throw new Exception("<MessageInfo> missing in <UserMessage>.");
            if (!userMessage.Elements().Any(e => e.Name.LocalName == "PartyInfo"))
                throw new Exception("<PartyInfo> missing in <UserMessage>.");
            if (!userMessage.Elements().Any(e => e.Name.LocalName == "CollaborationInfo"))
                throw new Exception("<CollaborationInfo> missing in <UserMessage>.");

            log.Debug("SOAP header structural validation passed.");
        }

        private void ValidateUserMessage(XDocument soapDoc)
        {
            var userMsg = SOAPHeaderParser.GetUserMessage(soapDoc);

            // MessageId format already validated elsewhere but ensure non-empty
            if (string.IsNullOrWhiteSpace(userMsg.MessageId))
                throw new Exception("UserMessage/MessageId is blank.");

            // Timestamp must parse to DateTime & within ±10 minutes (clock skew)
            if (!DateTime.TryParse(userMsg.Timestamp, out var ts))
                throw new Exception("Invalid UserMessage/Timestamp format.");
            var diff = DateTime.UtcNow - ts.ToUniversalTime();
            if (Math.Abs(diff.TotalMinutes) > 10)
                throw new Exception("UserMessage/Timestamp outside allowable skew (±10 minutes).");

            // Party IDs non-empty
            if (string.IsNullOrWhiteSpace(userMsg.FromPartyId))
                throw new Exception("From PartyId missing.");
            if (string.IsNullOrWhiteSpace(userMsg.ToPartyId))
                throw new Exception("To PartyId missing.");

            // Service & Action not blank
            if (string.IsNullOrWhiteSpace(userMsg.Service))
                throw new Exception("UserMessage/Service missing.");
            if (string.IsNullOrWhiteSpace(userMsg.Action))
                throw new Exception("UserMessage/Action missing.");

            // Validate originalSender / finalRecipient properties exist
            if (!userMsg.Properties.TryGetValue("originalSender", out var _))
                throw new Exception("Property 'originalSender' missing.");
            if (!userMsg.Properties.TryGetValue("finalRecipient", out var _))
                throw new Exception("Property 'finalRecipient' missing.");

            log.Debug("UserMessage property validation passed.");
        }

        private void ValidatePayloadConsistency(XDocument soapDoc, IList<As4Controller.MimePartManual> mimeParts)
        {
            var userMsg = SOAPHeaderParser.GetUserMessage(soapDoc);
            var hrefs = userMsg.PayloadHrefs ?? new List<string>();

            var attachmentParts = mimeParts.Where(p => !p.ContentType.Contains("soap+xml")).ToList();

            if (hrefs.Count != attachmentParts.Count)
                throw new Exception($"PayloadInfo/PartInfo href count ({hrefs.Count}) does not match attachment count ({attachmentParts.Count}).");

            foreach (var href in hrefs)
            {
                if (string.IsNullOrWhiteSpace(href) || !href.StartsWith("cid:"))
                    throw new Exception($"Invalid href '{href}' – must start with 'cid:' and not be blank.");

                var cid = href.Substring(4).Trim('<', '>');
                if (!attachmentParts.Any(p => p.ContentId.Equals(cid, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception($"No attachment part found with Content-ID '{cid}'.");
            }

            log.Debug("Payload consistency validation passed.");
        }

        private void ValidatePartyIdSchemes(XDocument soapDoc)
        {
            var userMsg = SOAPHeaderParser.GetUserMessage(soapDoc);
            var isoPrefix = "iso6523-actorid-upis::";

            if (!userMsg.FromPartyId.StartsWith(isoPrefix, StringComparison.OrdinalIgnoreCase))
                throw new Exception("From/PartyId must start with 'iso6523-actorid-upis::'.");
            if (!userMsg.ToPartyId.StartsWith(isoPrefix, StringComparison.OrdinalIgnoreCase))
                throw new Exception("To/PartyId must start with 'iso6523-actorid-upis::'.");

            log.Debug("Party ID scheme validation passed.");
        }

        private void ValidateAttachmentSizes(IList<As4Controller.MimePartManual> mimeParts)
        {
            double totalBytes = 0;
            foreach (var part in mimeParts)
            {
                var isSoap = part.ContentType.Contains("soap+xml");
                var sizeBytes = part.ContentBytes?.Length ?? 0;
                if (!isSoap)
                {
                    if (sizeBytes > _maxAttachmentSizeMb * 1024L * 1024L)
                        throw new Exception($"Attachment exceeds {_maxAttachmentSizeMb} MB limit.");
                    totalBytes += sizeBytes;
                }
            }
            if (totalBytes > _maxTotalAttachmentsMb * 1024L * 1024L)
                throw new Exception($"Total attachment size {totalBytes / (1024 * 1024):F1} MB exceeds {_maxTotalAttachmentsMb} MB limit.");

            log.Debug("Attachment size validation passed.");
        }

        #endregion
    }
} 