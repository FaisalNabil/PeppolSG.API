using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;
using System.Linq;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class MessageValidationTests
    {
        private MessageValidationService _validator;

        [TestInitialize]
        public void Init()
        {
            _validator = new MessageValidationService();
        }

        [TestMethod]
        public void Validate_WithValidSoap_ShouldPass()
        {
            var soap = BuildMinimalValidSoap("msg-1@example.com");
            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string, string>{{"content-type", "application/soap+xml"}}
                }
            };
            _validator.Validate(soap, parts); // should not throw
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Validate_SoapSizeTooBig_ShouldFail()
        {
            // Build huge SOAP by repeating payload inside header comment
            var soap = BuildMinimalValidSoap("msg-big@example.com");
            var sb = new StringBuilder();
            for (int i=0;i<60000;i++) sb.Append("a");
            soap.Root.Add(new XComment(sb.ToString()));

            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string, string>{{"content-type", "application/soap+xml"}}
                }
            };
            _validator.Validate(soap, parts);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Validate_MissingMessaging_ShouldFail()
        {
            var soap = new XDocument(
                new XElement("Envelope"));
            var parts = new List<As4Controller.MimePartManual>();
            _validator.Validate(soap, parts);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Validate_AttachmentHrefMismatch_ShouldFail()
        {
            var soap = BuildMinimalValidSoap("msg-attach-fail@example.com");
            // Modify href to mismatch
            soap.Descendants().First(e=>e.Name.LocalName=="PartInfo")?.SetAttributeValue("href","cid:unknown@cid");
            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string, string>{{"content-type", "application/soap+xml"}}
                },
                // One dummy attachment part with different CID
                new As4Controller.MimePartManual
                {
                    ContentBytes = Encoding.UTF8.GetBytes("dummy"),
                    Headers = new Dictionary<string,string>{{"content-type","application/octet-stream"},{"content-id","<actual@cid>"}}
                }
            };
            _validator.Validate(soap, parts);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void Validate_InvalidPartyIdScheme_ShouldFail()
        {
            var soap = BuildMinimalValidSoap("msg-partyid-fail@example.com");
            // replace PartyIds
            soap.Descendants().First(e=>e.Name.LocalName=="PartyId").Value = "WRONG::123";
            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string, string>{{"content-type", "application/soap+xml"}}
                }
            };
            _validator.Validate(soap, parts);
        }

        private static XDocument BuildMinimalValidSoap(string messageId)
        {
            XNamespace s12 = "http://www.w3.org/2003/05/soap-envelope";
            XNamespace eb = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/";
            XNamespace wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

            var env = new XElement(s12 + "Envelope",
                new XElement(s12 + "Header",
                    new XElement(eb + "Messaging",
                        new XElement(eb + "UserMessage",
                            new XElement(eb + "MessageInfo",
                                new XElement(eb + "MessageId", messageId),
                                new XElement(eb + "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"))
                            ),
                            new XElement(eb + "PartyInfo",
                                new XElement(eb + "From",
                                    new XElement(eb + "PartyId", "iso6523-actorid-upis::1234:AAA")),
                                new XElement(eb + "To",
                                    new XElement(eb + "PartyId", "iso6523-actorid-upis::1234:BBB"))
                            ),
                            new XElement(eb + "CollaborationInfo",
                                new XElement(eb + "Service", "busdox-docid-qns::simple"),
                                new XElement(eb + "Action", "test"),
                                new XElement(eb + "ConversationId", "conv123")),
                            new XElement(eb + "MessageProperties",
                                new XElement(eb + "Property", new XAttribute("name", "originalSender"), new XAttribute("type", "iso6523-actorid-upis"), "1234:AAA"),
                                new XElement(eb + "Property", new XAttribute("name", "finalRecipient"), new XAttribute("type", "iso6523-actorid-upis"), "1234:BBB")))
                    )
                ),
                new XElement(s12 + "Body"));
            return new XDocument(env);
        }
    }
} 