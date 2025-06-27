using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Xml.Linq;
using System.Xml;
using WebGrease;
using RoutePrefixAttribute = System.Web.Http.RoutePrefixAttribute;
using HttpPostAttribute = System.Web.Http.HttpPostAttribute;
using RouteAttribute = System.Web.Http.RouteAttribute;
using PeppolSG.API.Service;
using System.IO.Compression;
using System.Runtime.Remoting.Messaging;
using MimeKit;
using Org.BouncyCastle.Crypto.Encodings;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Security;
using System.Security.Cryptography.Xml;

namespace PeppolSG.API.Controllers
{
    [RoutePrefix("as4")]
    public class As4Controller : ApiController
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(As4Controller));
        private readonly ICertificateValidator _certificateValidator = new BasicCertificateValidator();
        private readonly IMetadataPersister _metadataPersister = new FileSystemMetadataPersister();
        private readonly IPayloadPersister _payloadPersister = new FileSystemPayloadPersister();
        private readonly SmkSmpLookupService _smkSmpLookup;

        public As4Controller(SmkSmpLookupService smkSmpLookup)
        {
            _smkSmpLookup = smkSmpLookup;
        }


        [HttpPost, Route("")]
        public async Task<IHttpActionResult> ReceiveAs4Message()
        {
            try
            {
                log.Info($"=== Incoming AS4 Request Details ===\n" +
                        $"URL: {Request.RequestUri}\n" +
                        $"Method: {Request.Method}\n" +
                        $"Remote IP: {GetClientIp(Request)}\n" +
                        $"Headers: {string.Join(", ", Request.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");

                var contentType = Request.Content.Headers.ContentType?.ToString() ?? throw new Exception("Missing Content-Type");
                log.Info($"=== Incoming AS4 Request ===\nContent-Type: {contentType}");

                // Get the boundary string
                var boundary = contentType.Split(';')
                    .Select(p => p.Trim())
                    .FirstOrDefault(p => p.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("boundary=".Length)
                    .Trim('"');
                if (string.IsNullOrWhiteSpace(boundary))
                    throw new Exception("Missing boundary in Content-Type");

                // Read the full request body as a string
                string bodyText;
                using (var sr = new StreamReader(await Request.Content.ReadAsStreamAsync(), Encoding.UTF8))
                    bodyText = sr.ReadToEnd();
                log.Info($"=== RAW INCOMING AS4 REQUEST BODY START ===\n{bodyText}\n=== RAW INCOMING AS4 REQUEST BODY END ===");
                // Parse the multipart
                var mimeParts = ParseMultipartString(bodyText, boundary);

                if (mimeParts.Count == 0)
                    throw new Exception("No parts found in AS4 message.");

                // Find SOAP part (envelope)
                var soapPart = mimeParts.FirstOrDefault(p =>
                    p.ContentType.Contains("application/soap+xml") ||
                    (p.ContentText != null && p.ContentText.TrimStart().StartsWith("<S12:Envelope"))
                );
                if (soapPart == null) throw new Exception("No SOAP part found.");

                var soapXml = XDocument.Parse(soapPart.ContentText);
                log.Info($"=== Parsed SOAP Envelope ===\n{soapXml}");

                // Parse protocol details
                //var senderCert = SOAPHeaderParser.GetSenderCertificate(soapXml);
                var sigBytes = SOAPHeaderParser.GetSignature(soapXml);
                var userMsg = SOAPHeaderParser.GetUserMessage(soapXml);
                var references = SOAPHeaderParser.GetReferenceListFromSignedInfo(soapXml);

                // Extract identifiers from the received SOAP XML (already have these lines)
                var originalSenderProp = soapXml.Descendants()
    .FirstOrDefault(x => x.Name.LocalName == "Property" &&
                         (string)x.Attribute("name") == "originalSender");

                if (originalSenderProp == null)
                    throw new Exception("originalSender property missing from message!");

                string senderParticipantId = originalSenderProp?.Value; // e.g., "9922:NGTBCNTRLP1001"
                string senderScheme = originalSenderProp?.Attribute("type")?.Value ?? "iso6523-actorid-upis";

                //string senderScheme = senderParts[0];          // "iso6523-actorid-upis"
                //string senderParticipantId = senderParts[1];   // "9922:NGTBCNTRLP1001"

                // Action node for docTypeId
                string docTypeId = null;
                var ebAction = soapXml.Descendants().FirstOrDefault(x => x.Name.LocalName == "Action")?.Value;
                if (!string.IsNullOrEmpty(ebAction))
                    docTypeId = ebAction; // should be e.g., "busdox-docid-qns::urn:oasis:..."

                string processId = soapXml.Descendants().FirstOrDefault(x => x.Name.LocalName == "Service")?.Value;
                if (string.IsNullOrEmpty(docTypeId) || string.IsNullOrEmpty(processId))
                    throw new Exception("Could not extract docTypeId or processId for SMP lookup!");

                // Peppol-compliant SMP lookup
                var endpointMetadata = await _smkSmpLookup.LookupEndpointMetadata(senderParticipantId, senderScheme, docTypeId, processId);

                //if (!ValidateSignature(soapXml, new X509Certificate2(Convert.FromBase64String(endpointMetadata.Certificate))))
                //    throw new Exception("AS4 signature validation failed (does not match SMP certificate)!");
                //if (!ValidateSignatureWithXmlSec(soapXml, endpointMetadata.Certificate))
                //    throw new Exception("AS4 signature validation failed (does not match SMP certificate)!");

                log.Info($"=== Incoming AS4 Message Details ===\n" +
                        $"MessageId: {userMsg.MessageId}\n" +
                        $"From: {userMsg.FromPartyId}\n" +
                        $"To: {userMsg.ToPartyId}\n" +
                        $"Service: {userMsg.Service}\n" +
                        $"Action: {userMsg.Action}");

                if (string.IsNullOrWhiteSpace(userMsg.MessageId))
                    throw new Exception("MessageId missing from UserMessage.");

                ValidateMessageId(userMsg.MessageId);
                _certificateValidator.Validate(new X509Certificate2(Convert.FromBase64String(endpointMetadata.Certificate)));

                // Payloads: All non-SOAP parts with Content-ID
                var userMsgHrefs = userMsg.PayloadHrefs ?? new List<string>();
                var payloadParts = mimeParts.Where(p => p != soapPart && !string.IsNullOrWhiteSpace(p.ContentId)).ToList();
                var payloadPaths = new List<string>();

                if (payloadParts.Count != userMsgHrefs.Count)
                    throw new Exception($"Mismatch: {payloadParts.Count} attachments but {userMsgHrefs.Count} UserMessage part(s).");

                for (int i = 0; i < payloadParts.Count; i++)
                {
                    var payload = payloadParts[i];
                    var href = userMsgHrefs[i];
                    if (!href.StartsWith("cid:"))
                        throw new Exception($"Payload href {href} is not a valid 'cid:' URI.");

                    var payloadInfo = new PayloadInfo
                    {
                        ContentId = payload.ContentId,
                        MimeType = payload.ContentType,
                        Charset = payload.Headers.TryGetValue("charset", out var cs) ? cs : null,
                        IsGzip = payload.ContentType.Contains("gzip") ||
                                 (payload.Headers.TryGetValue("compressiontype", out var ct) && ct.Contains("gzip"))
                    };
                    ValidatePayloadHeaders(payloadInfo);

                    var digest = SOAPHeaderParser.GetAttachmentDigest(href, soapXml);
                    // Optionally validate digest here.

                    byte[] data = payload.ContentBytes;
                    if (payloadInfo.IsGzip)
                        data = DecompressGzip(data);

                    var filePath = _payloadPersister.Persist(userMsg.MessageId, payloadInfo, data);
                    payloadPaths.Add(filePath);
                }

                // 5. Persist metadata
                var metadata = new As4InboundMetadata
                {
                    MessageId = userMsg.MessageId,
                    Timestamp = userMsg.Timestamp,
                    Sender = userMsg.FromPartyId,
                    Receiver = userMsg.ToPartyId,
                    DocumentType = userMsg.Service,
                    //Certificate = senderCert,
                    PayloadPaths = payloadPaths,
                    RawSoapXml = soapXml.ToString(),
                    EnvelopeHeader = userMsg,
                    CertificateThumbprint = new X509Certificate2(Convert.FromBase64String(endpointMetadata.Certificate)).Thumbprint,
                    CertificateBase64 = endpointMetadata.Certificate,
                };
                _metadataPersister.Persist(metadata);

                // 6. Generate and sign AS4 receipt
                var receiptTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var receiptMessageId = $"{Guid.NewGuid()}@{System.Configuration.ConfigurationManager.AppSettings["PeppolDomain"]}";
                var receiptMessage = As4MessageBuilder.BuildSignalMessage(
                    receiptTimestamp,
                    receiptMessageId,
                    userMsg.MessageId,
                    references: references
                );
                string messagingId = "phase4-msg-" + Guid.NewGuid().ToString("N");
                var messaging = As4MessageBuilder.BuildMessaging(receiptMessage, messagingId);

                // Load signing certificate
                var certPath = ConfigurationManager.AppSettings["PeppolP12FilePath"];
                var certPwd = ConfigurationManager.AppSettings["PeppolP12Password"];
                var signingCert = new X509Certificate2(certPath, certPwd);

                // Build WS-Security header
                string bstId;
                var wsseSecurity = As4MessageBuilder.BuildWsseSecurity(signingCert, out bstId);

                // Wrap into SOAP envelope and sign
                string bodyId = Guid.NewGuid().ToString("N");
                var receiptSoapDoc = As4MessageBuilder.WrapInSoapEnvelope(messaging, wsseSecurity, bodyId);
                PeppolAs4Signer.SignEnvelope(
                    receiptSoapDoc,
                    certPath,
                    certPwd,
                    bstId,
                    messagingId: messagingId,
                    bodyId: bodyId
                );

                // Collect original attachments to echo back
                var attachments = mimeParts
                    .Where(p => p != soapPart && !string.IsNullOrEmpty(p.ContentId))
                    .Select(p => new As4MessageBuilder.Attachment
                    {
                        ContentId = p.ContentId,
                        ContentType = p.ContentType,
                        Bytes = p.ContentBytes
                    })
                    .ToList();

                // 7. Use Phase4 to build the MTOM response
                HttpResponseMessage as4Response = As4MessageBuilder.CreateMtomResponse(
                    receiptSoapDoc,
                    attachments,
                    HttpStatusCode.OK
                );

                // 8. Return directly
                return ResponseMessage(as4Response);

            }
            catch (Exception ex)
            {
                log.Error($"=== AS4 Error ===\nMessage: {ex.Message}\nStack Trace: {ex.StackTrace}");
                string soapFaultXml = $@"<S12:Envelope xmlns:S12=""http://www.w3.org/2003/05/soap-envelope"">
<S12:Header/>
<S12:Body>
  <S12:Fault>
    <S12:Code><S12:Value>S12:Receiver</S12:Value></S12:Code>
    <S12:Reason><S12:Text xml:lang=""en"">{SecurityElement.Escape(ex.Message)}</S12:Text></S12:Reason>
  </S12:Fault>
</S12:Body>
</S12:Envelope>";
                var errorResponse = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(soapFaultXml, Encoding.UTF8, "application/soap+xml")
                };
                return ResponseMessage(errorResponse);
            }

        }

        [HttpPost]
        [Route("send")]
        public async Task<IHttpActionResult> SendAs4Message()
        {
            try
            {
                // 1. Read invoice XML
                var invoiceXml = await Request.Content.ReadAsStringAsync();

                // Extract receiverId and scheme from SBDH
                var xdoc = XDocument.Parse(invoiceXml);
                XNamespace ns = "http://www.unece.org/cefact/namespaces/StandardBusinessDocumentHeader";
                var receiverEl = xdoc.Descendants(ns + "StandardBusinessDocumentHeader")
                    .Descendants(ns + "Receiver")
                    .Descendants(ns + "Identifier")
                    .FirstOrDefault();
                string receiverId = receiverEl?.Value;
                string receiverScheme = receiverEl?.Attribute("Authority")?.Value ?? "iso6523-actorid-upis";
                // 2. Extract PEPPOL header info (your implementation)
                var peppolHeader = ExtractPeppolHeaderInfo(invoiceXml);
                var recipientId = peppolHeader.ReceiverId;
                var senderId = peppolHeader.SenderId;
                var docTypeId = $"{peppolHeader.DocumentScheme}::{peppolHeader.DocTypeId}";
                var processId = peppolHeader.ProcessId;
                var instanceId = peppolHeader.InstanceId;
                var conversationId = $"Conv-{Guid.NewGuid()}";

                // 3. Lookup recipient endpoint and cert via SMP
                var endpointMeta = await _smkSmpLookup.LookupEndpointMetadata(
                    receiverId, receiverScheme, docTypeId, processId
                );
                var recipientEndpoint = endpointMeta.EndpointUrl;
                var recipientCert = new X509Certificate2(Convert.FromBase64String(endpointMeta.Certificate));

                // 4. Build AS4 header values
                var domain = System.Configuration.ConfigurationManager.AppSettings["PeppolDomain"];
                var messageId = instanceId + "@" + domain;
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                // 5. Load sender cert
                var certPath = System.Configuration.ConfigurationManager.AppSettings["PeppolP12FilePath"];
                var certPwd = System.Configuration.ConfigurationManager.AppSettings["PeppolP12Password"];
                var senderCert = new X509Certificate2(certPath, certPwd);
                var senderPartyId = GetCertificateCommonName(senderCert);
                var receiverPartyId = GetCertificateCommonName(recipientCert);

                // 6. Compress & AES-GCM encrypt payload
                var plainBytes = Encoding.UTF8.GetBytes(invoiceXml);
                var gzipped = CompressGzip(plainBytes);
                var aesKey = new byte[16];
                var aesIv = new byte[16];   // CBC IV is 16 bytes
                using (var rng = RandomNumberGenerator.Create())
                {
                    rng.GetBytes(aesKey);
                    rng.GetBytes(aesIv);
                }
                // encrypt the gzipped payload with AES-CBC + PKCS7
                var cipher = CryptoUtil.AesCbcEncrypt(aesKey, aesIv, gzipped);

                // Prepend IV, no tag
                var encryptedAttachment = new byte[aesIv.Length + cipher.Length];
                Buffer.BlockCopy(aesIv, 0, encryptedAttachment, 0, aesIv.Length);
                Buffer.BlockCopy(cipher, 0, encryptedAttachment, aesIv.Length, cipher.Length);

                // 7. Protect AES key with RSA-OAEP/SHA-256
                var encryptedAesKey = RsaOaepEncrypt_MGF1_SHA256(aesKey, recipientCert);
                var encKeyB64 = Convert.ToBase64String(encryptedAesKey);

                // 8. IDs and CIDs
                var encryptedKeyId = "EK-" + Guid.NewGuid().ToString("N");
                var encryptedDataId = "ED-" + Guid.NewGuid().ToString("N");
                var attachmentCid = "phase4-att-" + Guid.NewGuid().ToString("N") + "@cid";
                var partHref = "cid:" + attachmentCid;

                // 9. Build UserMessage/Messaging (Phase4 helper)
                var userMsg = As4MessageBuilder.BuildUserMessage(
                    messageId, timestamp, "Conv-" + Guid.NewGuid().ToString("N"),
                    senderPartyId, receiverPartyId,
                    docTypeId, processId, partHref,
                    receiverId, senderId, true
                );
                var messagingId = "phase4-msg-" + Guid.NewGuid().ToString("N");
                var messaging = As4MessageBuilder.BuildMessaging(userMsg, messagingId);

                // 10. Build WS-Security header
                string recipientBstId = Guid.NewGuid().ToString("N");
                var recipientBst = As4MessageBuilder.BuildBinarySecurityToken(recipientCert, recipientBstId);
                string senderBstId = "X509-" + Guid.NewGuid().ToString("N");
                var senderBst = As4MessageBuilder.BuildBinarySecurityToken(senderCert, senderBstId);

                var encryptedKeyEl = As4MessageBuilder.BuildEncryptedKey(encryptedKeyId, recipientBstId, encKeyB64, encryptedDataId);
                var encryptedDataEl = As4MessageBuilder.BuildEncryptedData(encryptedDataId, encryptedKeyId, attachmentCid);

                var wsseSec = As4MessageBuilder.BuildSecurityHeader(
                    recipientBst, encryptedKeyEl, encryptedDataEl, senderBst
                );

                // 11. Assemble and sign SOAP envelope
                var bodyId = "id-" + Guid.NewGuid().ToString("N");
                var soapDoc = As4MessageBuilder.WrapInSoapEnvelope(wsseSec, messaging, bodyId);
                PeppolAs4Signer.SignEnvelope(
                    soapDoc, certPath, certPwd,
                    senderBstId, messagingId, bodyId,
                    partHref, encryptedAttachment
                );

                // Serialize SOAP with XML declaration
                var xmlSettings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = false,
                    Encoding = new UTF8Encoding(false),
                    Indent = false
                };
                string soapXmlString;
                using (var ms = new MemoryStream())
                {
                    using (var xmlWriter = XmlWriter.Create(ms, xmlSettings))
                    {
                        soapDoc.WriteTo(xmlWriter);
                        xmlWriter.Flush();
                    }
                    soapXmlString = Encoding.UTF8.GetString(ms.ToArray());
                }

                // 12. Build MIME multipart/related
                var boundary = "----=_Part_" + Guid.NewGuid().ToString("N");
                var message = new MimeMessage();
                var multipart = new MultipartRelated { ContentType = { Boundary = boundary } };
                multipart.ContentType.Parameters.Add("type", "application/soap+xml");

                // SOAP part
                var soapPart = new MimePart("application", "soap+xml")
                {
                    Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(soapXmlString)), ContentEncoding.Binary),
                    ContentTransferEncoding = ContentEncoding.Binary,
                };
                soapPart.ContentType.Charset = "UTF-8";

                multipart.Add(soapPart);

                // Encrypted attachment part
                var payloadPart = new MimePart("application", "octet-stream")
                {
                    Content = new MimeContent(new MemoryStream(encryptedAttachment), ContentEncoding.Binary),
                    ContentTransferEncoding = ContentEncoding.Binary,
                };
                payloadPart.ContentId = $"<{attachmentCid}>"; // Must match href in SOAP
                payloadPart.ContentDescription = "Attachment";
                multipart.Add(payloadPart);

                message.Body = multipart;

                // 13. Send HTTP request
                using (var http = new HttpClient())
                using (var msOut = new MemoryStream())
                {
                    // write the full MimeKit message into our buffer
                    message.WriteTo(msOut);

                    // rewind & grab the bytes so we can both log (or inspect) *and* send them
                    var rawBytes = msOut.ToArray();
                    var rawText = Encoding.UTF8.GetString(rawBytes);

                    // **1) log it**
                    log.Info("=== OUTGOING AS4 MIME ===\n" + rawText);

                    // **2) if you want to *see* it in your HTTP response for debugging:**
                    // return Content(HttpStatusCode.OK, rawText, "text/plain");

                    // otherwise rewind and send on its way:
                    msOut.Flush(); // Ensure all data is writtenmsOut.Position = 0;
                    msOut.Position = 0;
                    var content = new StreamContent(msOut);
                    content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(
                        $"multipart/related; type=\"application/soap+xml\"; boundary=\"{boundary}\""
                    );

                    var req = new HttpRequestMessage(HttpMethod.Post, recipientEndpoint)
                    {
                        Content = content
                    };
                    req.Headers.ExpectContinue = false;
                    req.Headers.Add("SOAPAction", string.Empty);

                    var resp = await http.SendAsync(req);
                    var respText = await resp.Content.ReadAsStringAsync();
                    return Content(resp.StatusCode, respText);
                }
            }
            catch (Exception ex)
            {
                log.Error("AS4 send error: " + ex.Message, ex);
                return Content(HttpStatusCode.InternalServerError, new
                {
                    Message = "AS4 send error",
                    ExceptionMessage = ex.Message,
                    ExceptionType = ex.GetType().FullName,
                    StackTrace = ex.StackTrace,
                    InnerException = ex.InnerException?.ToString()
                });
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
                ReceiverId = receiverId,
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
            var bcCert = new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(cert.RawData);
            // Get the private key from your .p12/.pfx as BouncyCastle key
            var rsaPrivate = DotNetUtilities.GetKeyPair(cert.GetRSAPrivateKey()).Private;

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
            var boundaryMarker = "--" + boundary.Trim('"');
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
                SigningKey = senderCert.GetRSAPublicKey()
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
            // find all non-SOAP parts
            var parts = new List<MimePart>();
            foreach (var e in related)
                if (e is MimePart mp && mp.ContentType.MimeType != "application/soap+xml")
                    parts.Add(mp);

            if (parts.Count != hrefs.Count)
                throw new Exception("Attachment count mismatch");

            var results = new List<string>();
            var myCert = new X509Certificate2(certPath, certPwd, X509KeyStorageFlags.Exportable);
            for (int i = 0; i < hrefs.Count; i++)
            {
                string href = hrefs[i];
                // strip "cid:"
                string cid = href.Substring(4).Trim('<', '>');

                // find matching part by Content-ID
                MimePart part = parts[i];
                string actualCid = part.ContentId;
                if (string.IsNullOrEmpty(actualCid))
                {
                    // fallback: look in raw headers
                    foreach (var h in part.Headers)
                    {
                        if (h.Field.Equals("Content-ID", StringComparison.OrdinalIgnoreCase))
                        {
                            actualCid = h.Value;
                            break;
                        }
                    }
                }
                actualCid = actualCid.Trim('<', '>');
                if (!actualCid.Equals(cid, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Href {href} ≠ Content-ID {actualCid}");

                // decode
                byte[] encrypted;
                using (var ms = new MemoryStream())
                {
                    part.Content.DecodeTo(ms);
                    encrypted = ms.ToArray();
                }

                // decrypt
                byte[] clear = DecryptPeppolAttachment(encrypted, soapXml, myCert, href);

                // digest check
                byte[] expected = SOAPHeaderParser.GetAttachmentDigest(href, soapXml);
                if (expected != null)
                {
                    using (var sha = SHA256.Create())
                    {
                        byte[] actual = sha.ComputeHash(clear);
                        if (!actual.SequenceEqual(expected))
                            throw new Exception("Digest mismatch for " + href);
                    }
                }

                // persist
                var info = new PayloadInfo
                {
                    ContentId = cid,
                    MimeType = part.ContentType.MimeType,
                    IsGzip = part.ContentType.MimeType == "application/gzip"
                };
                results.Add(_payloadPersister.Persist(userMsg.MessageId, info, clear));
            }
            return results;
        }

        private byte[] DecryptPeppolAttachment(
    byte[] encryptedBytes,
    XDocument soapXml,
    X509Certificate2 myCert,
    string href)
        {
            var xenc = XNamespace.Get("http://www.w3.org/2001/04/xmlenc#");
            var ds = XNamespace.Get("http://www.w3.org/2000/09/xmldsig#");

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
                throw new InvalidOperationException("No EncryptedData for " + href);

            // 2) find the EncryptedKey that it references
            var keyRef = encryptedData
              .Element(ds + "KeyInfo")
              .Descendants()
              .First(n => n.Name.LocalName == "Reference");
            var keyId = keyRef.Attribute("URI").Value.TrimStart('#');

            var encryptedKeyElem = soapXml
              .Descendants(xenc + "EncryptedKey")
              .FirstOrDefault(ek => (string)ek.Attribute("Id") == keyId);
            if (encryptedKeyElem == null)
                throw new InvalidOperationException("No EncryptedKey with Id=" + keyId);

            // 3) unwrap the AES key via RSA-OAEP-SHA256
            var encryptedKeyB64 = encryptedKeyElem
              .Element(xenc + "CipherData")
              .Element(xenc + "CipherValue")
              .Value;
            var encryptedKey = Convert.FromBase64String(encryptedKeyB64);
            var rsa = myCert.GetRSAPrivateKey();
            var aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);

            // 4) split out IV (16 bytes) + ciphertext
            if (encryptedBytes.Length < 16)
                throw new InvalidOperationException("Attachment too short to contain IV");
            var iv = encryptedBytes.Take(16).ToArray();
            var cipherText = encryptedBytes.Skip(16).ToArray();

            // 5) decrypt with AES-CBC
            return CryptoUtil.AesCbcDecrypt(aesKey, iv, cipherText);
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
    }

    public class PeppolHeaderInfo
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
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
            var msg = new MimeMessage();

            var multi = new Multipart("related");
            multi.ContentType.Parameters.Add("type", "application/soap+xml");

            // Optionally set a root Content-ID for the SOAP part, and set 'start' parameter
            var rootContentId = "rootpart@as4";
            //multi.ContentType.Parameters.Add("start", "<" + rootContentId + ">"); // uncomment if you want to explicitly specify root

            // --- part 1: SOAP envelope ---
            var soapPart = new MimePart("application", "soap+xml")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(soapDoc.ToString())), ContentEncoding.Binary)
            };
            soapPart.ContentType.Charset = "UTF-8";
            //soapPart.Headers.Replace(HeaderId.ContentId, "<" + rootContentId + ">"); // uncomment if you want to specify Content-ID

            multi.Add(soapPart);

            // --- part 2: encrypted attachment ---
            var attPart = new MimePart("application", "octet-stream")
            {
                Content = new MimeContent(new MemoryStream(attachmentBytes), ContentEncoding.Binary),
                ContentTransferEncoding = ContentEncoding.Binary
            };
            attPart.Headers.Replace(HeaderId.ContentId, "<" + attachmentCid + ">");
            multi.Add(attPart);

            msg.Body = multi;
            return msg;
        }

        public static MimeMessage BuildSoapOnly(XDocument soapDoc)
        {
            var msg = new MimeMessage();
            msg.Body = new MimePart("application", "soap+xml")
            {
                Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(soapDoc.ToString())), ContentEncoding.Binary),
                ContentTransferEncoding = ContentEncoding.Binary
            };
            ((MimePart)msg.Body).ContentType.Charset = "UTF-8";
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
            // TODO: Implement Peppol trust validation
            if (cert == null)
                throw new SecurityException("No client certificate.");
        }
    }

    public interface IMetadataPersister { void Persist(As4InboundMetadata metadata); }
    public class FileSystemMetadataPersister : IMetadataPersister
    {
        public void Persist(As4InboundMetadata metadata)
        {
            var dir = Path.Combine("C:\\As4Inbound", metadata.MessageId);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "metadata.json"),
                System.Text.Json.JsonSerializer.Serialize(metadata));
        }
    }

    public interface IPayloadPersister { string Persist(string messageId, PayloadInfo info, byte[] data); }
    public class FileSystemPayloadPersister : IPayloadPersister
    {
        public string Persist(string messageId, PayloadInfo info, byte[] data)
        {
            var dir = Path.Combine("C:\\As4Inbound", messageId, "payloads");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{info.ContentId ?? Guid.NewGuid().ToString()}.{(info.IsGzip ? "gz" : "bin")}");
            File.WriteAllBytes(path, data);
            return path;
        }
    }

    #endregion
}