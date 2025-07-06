using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Xml.Linq;
using PeppolSG.API.Models;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IAs4MessageBuilder
    {
        XElement BuildUserMessage(string messageId, string timestamp, string conversationId, string senderId, string receiverId, string docTypeId, string processId, string partHref, string originalReceiverId, string originalSenderId, bool compressed);
        XElement BuildSignalMessage(string timestamp, string messageId, string refToMessageId, IEnumerable<XElement> references = null);
        XElement BuildErrorMessage(string timestamp, string messageId, string refToMessageId, string errorCode, string severity, string description, string shortDescription = null, string origin = "ebMS", string category = "Content");
        XElement BuildMessaging(XElement messageOrSignal, string messagingId = null);
        XDocument BuildSoapEnvelope(XElement messaging, XElement wsSecurityHeader, string bodyId);
        XElement BuildSbdh(string senderId, string receiverId, string docTypeId, string processId, string instanceId, string creationDateTime);
        HttpResponseMessage CreateMtomResponse(XDocument soapEnvelope, IList<As4Attachment> attachments, HttpStatusCode statusCode);
        XDocument WrapInSoapEnvelope(XElement securityHeader, XElement messaging, string bodyId);
        XElement BuildBinarySecurityToken(System.Security.Cryptography.X509Certificates.X509Certificate2 cert, string bstId);
        XElement BuildEncryptedKey(string ekId, string bstToRefId, string encryptedKeyB64, string dataRefId);
        XElement BuildEncryptedData(string edId, string ekId, string attachmentCid, bool gzip = true);
        XElement BuildSecurityHeader(XElement recipientBstEl, XElement encryptedKeyEl, XElement encryptedDataEl, XElement senderBstEl);
    }
} 