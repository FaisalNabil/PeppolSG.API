using System.Collections.Generic;
using System.Net.Http;
using System.Net;
using System.Xml.Linq;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IAs4MessageBuilder
    {
        XElement BuildUserMessage(string messageId, string timestamp, string conversationId, string senderId, string receiverId, string docTypeId, string processId, string partHref, string originalReceiverId, string originalSenderId, bool compressed);
        XElement BuildSignalMessage(string timestamp, string messageId, string refToMessageId, IEnumerable<XElement> references = null);
        XElement BuildErrorMessage(string timestamp, string messageId, string refToMessageId, string errorCode, string severity, string description, string shortDescription = null, string origin = "ebMS", string category = "Content");
        XElement BuildMessaging(XElement messageOrSignal, string messagingId = null);
        XDocument BuildSoapEnvelope(XElement messaging, XElement wsSecurityHeader);
        XElement BuildSbdh(string senderId, string receiverId, string docTypeId, string processId, string instanceId, string creationDateTime);
        HttpResponseMessage CreateMtomResponse(XDocument soapEnvelope, IList<As4MessageBuilder.Attachment> attachments, HttpStatusCode statusCode);
    }
} 