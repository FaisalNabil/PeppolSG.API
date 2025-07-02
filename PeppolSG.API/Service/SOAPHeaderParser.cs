using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;
using System.Xml.Linq;

namespace PeppolSG.API.Service
{
    public static class XmlHelper
    {
        // Namespace-insensitive search (like Java's "*")
        public static IEnumerable<XElement> FindAllElementsByLocalName(XElement root, string localName)
        {
            return root.DescendantsAndSelf().Where(e => e.Name.LocalName == localName);
        }
    }

    public class UserMessage
    {
        public string MessageId { get; set; }
        public string Timestamp { get; set; }
        public string ConversationId { get; set; }
        public string FromPartyId { get; set; }
        public string ToPartyId { get; set; }
        public string FromPartyIdType { get; set; }
        public string ToPartyIdType { get; set; }
        public string Service { get; set; }
        public string Action { get; set; }
        public List<string> PayloadHrefs { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    public static class SOAPHeaderParser
    {
        // 1. Extract sender certificate from SOAP header
        public static X509Certificate2 GetSenderCertificate(XDocument soapDoc)
        {
            var security = XmlHelper.FindAllElementsByLocalName(soapDoc.Root, "Security").FirstOrDefault();
            if (security == null)
                throw new Exception("No <Security> header found in SOAP.");

            var sigNodes = XmlHelper.FindAllElementsByLocalName(security, "Signature").ToList();
            if (sigNodes.Count != 1)
                throw new Exception($"Expected one Signature element in header, found {sigNodes.Count}.");
            var sigElement = sigNodes[0];

            var keyInfoNodes = XmlHelper.FindAllElementsByLocalName(sigElement, "KeyInfo").ToList();
            if (keyInfoNodes.Count != 1)
                throw new Exception($"Expected one KeyInfo under Signature, found {keyInfoNodes.Count}.");
            var keyInfoElement = keyInfoNodes[0];

            var refNodes = XmlHelper.FindAllElementsByLocalName(keyInfoElement, "Reference").ToList();
            if (refNodes.Count != 1)
                throw new Exception("Zero or multiple Reference nodes under Signature->KeyInfo");
            var refUri = refNodes[0].Attribute("URI")?.Value?.Replace("#", "");
            if (string.IsNullOrEmpty(refUri))
                throw new Exception("Reference URI not found in <Reference>.");

            // Find BinarySecurityToken with wsu:Id or Id matching refUri
            var bstNodes = XmlHelper.FindAllElementsByLocalName(security, "BinarySecurityToken");
            foreach (var bstElem in bstNodes)
            {
                var idAttr = bstElem.Attributes().FirstOrDefault(a =>
                    a.Name.LocalName == "Id" || a.Name.LocalName == "wsu:Id");
                if (idAttr != null && idAttr.Value == refUri)
                {
                    var pem = bstElem.Value.Replace("\r", "").Replace("\n", "").Trim();
                    var buf = Convert.FromBase64String(pem);
                    return new X509Certificate2(buf);
                }
            }
            throw new Exception($"No BinarySecurityToken found with Id or wsu:Id='{refUri}'");
        }

        // 2. Extract SignatureValue as byte[]
        public static byte[] GetSignature(XDocument soapDoc)
        {
            var sigNodes = XmlHelper.FindAllElementsByLocalName(soapDoc.Root, "Signature").ToList();
            if (sigNodes.Count != 1)
                throw new Exception($"Expected one Signature element in header, found {sigNodes.Count}.");
            var sigElement = sigNodes[0];

            var sigValNodes = XmlHelper.FindAllElementsByLocalName(sigElement, "SignatureValue").ToList();
            if (sigValNodes.Count != 1)
                throw new Exception("Zero or multiple SignatureValue elements in header.");
            var val = sigValNodes[0].Value.Replace("\r", "").Replace("\n", "");
            return Encoding.UTF8.GetBytes(val);
        }

        // 3. Extract DigestValue for attachment
        public static byte[] GetAttachmentDigest(string refId, XDocument soapDoc)
        {
            var sigInfoNodes = XmlHelper.FindAllElementsByLocalName(soapDoc.Root, "SignedInfo").ToList();
            if (sigInfoNodes.Count != 1)
                throw new Exception($"Expected one SignedInfo element in header, found {sigInfoNodes.Count}.");
            var sigInfoElement = sigInfoNodes[0];

            var refNodes = XmlHelper.FindAllElementsByLocalName(sigInfoElement, "Reference").ToList();
            foreach (var refElem in refNodes)
            {
                var uri = refElem.Attribute("URI")?.Value;
                if (uri == refId || uri == ("#" + refId))
                {
                    var digestValueNodes = XmlHelper.FindAllElementsByLocalName(refElem, "DigestValue").ToList();
                    if (digestValueNodes.Count == 1)
                        return Encoding.UTF8.GetBytes(digestValueNodes[0].Value);
                }
            }
            return null;
        }

        // 4. Extract Reference URIs from SignedInfo
        public static List<XElement> GetReferenceListFromSignedInfo(XDocument soapDoc)
        {
            var sigInfoNodes = XmlHelper.FindAllElementsByLocalName(soapDoc.Root, "SignedInfo").ToList();
            if (sigInfoNodes.Count != 1)
                throw new Exception("Zero or multiple SignedInfo elements in header.");
            var sigInfoElement = sigInfoNodes[0];
            var refNodes = XmlHelper.FindAllElementsByLocalName(sigInfoElement, "Reference").ToList();
            return refNodes;
        }

        // 5. Extract UserMessage (POCO) from SOAP header
        public static UserMessage GetUserMessage(XDocument soapDoc)
        {
            // Find <Messaging> anywhere
            var messagingNode = XmlHelper.FindAllElementsByLocalName(soapDoc.Root, "Messaging").FirstOrDefault();
            if (messagingNode == null)
                throw new Exception("No <Messaging> node found in SOAP header.");

            var userMessageNode = XmlHelper.FindAllElementsByLocalName(messagingNode, "UserMessage").FirstOrDefault();
            if (userMessageNode == null)
                throw new Exception("No <UserMessage> node found in <Messaging>.");

            var messageInfo = userMessageNode.Elements().FirstOrDefault(e => e.Name.LocalName == "MessageInfo");
            var partyInfo = userMessageNode.Elements().FirstOrDefault(e => e.Name.LocalName == "PartyInfo");
            var collaborationInfo = userMessageNode.Elements().FirstOrDefault(e => e.Name.LocalName == "CollaborationInfo");
            var payloadInfo = userMessageNode.Elements().FirstOrDefault(e => e.Name.LocalName == "PayloadInfo");

            // FROM/TO
            var fromPartyId = partyInfo?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "From")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "PartyId")?.Value;

            var fromPartyIdType = partyInfo?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "From")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "PartyId")?.Attribute("type")?.Value;

            var toPartyId = partyInfo?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "To")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "PartyId")?.Value;
            
            var toPartyIdType = partyInfo?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "To")?
                .Elements().FirstOrDefault(e => e.Name.LocalName == "PartyId")?.Attribute("type")?.Value;

            // Properties
            var messageProperties = userMessageNode.Elements()
                .FirstOrDefault(e => e.Name.LocalName == "MessageProperties");
            var properties = messageProperties?
                .Elements().Where(e => e.Name.LocalName == "Property")
                .ToDictionary(
                    e => e.Attribute("name")?.Value ?? "",
                    e => e.Value);

            // Payload hrefs
            var payloadHrefs = payloadInfo?
                .Elements().Where(e => e.Name.LocalName == "PartInfo")
                .Select(e => e.Attribute("href")?.Value)
                .Where(h => h != null)
                .ToList() ?? new List<string>();

            return new UserMessage
            {
                MessageId = messageInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "MessageId")?.Value,
                Timestamp = messageInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "Timestamp")?.Value,
                ConversationId = collaborationInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "ConversationId")?.Value,
                Service = collaborationInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "Service")?.Value,
                Action = collaborationInfo?.Elements().FirstOrDefault(e => e.Name.LocalName == "Action")?.Value,
                FromPartyId = fromPartyId,
                ToPartyId = toPartyId,
                FromPartyIdType = fromPartyIdType,
                ToPartyIdType = toPartyIdType,
                PayloadHrefs = payloadHrefs,
                Properties = properties ?? new Dictionary<string, string>()
            };
        }
    }
}