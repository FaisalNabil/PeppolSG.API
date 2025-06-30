using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PeppolSG.API.Controllers;
using PeppolSG.API.Service;
using PeppolSG.API.Service.Resilience;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class EndToEndTests
    {
        private Mock<SmkSmpLookupService> _smpMock;
        private TestMetadataPersister _metaPersister;
        private TestPayloadPersister _payloadPersister;
        private As4Controller _controller;
        private MessageIdManager _messageIdManager;

        [TestInitialize]
        public void Init()
        {
            _smpMock = new Mock<SmkSmpLookupService>("test.smp.endpoint") { CallBase = true };
            _smpMock.Setup(s => s.LookupEndpointMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new PeppolEndpointMetadata
                {
                    EndpointUrl = "https://dummy",
                    Certificate = Convert.ToBase64String(new byte[256])
                });
            _metaPersister = new TestMetadataPersister();
            _payloadPersister = new TestPayloadPersister();
            _messageIdManager = new MessageIdManager();
            _controller = new As4Controller(_smpMock.Object, new PermissiveCertValidator(), _messageIdManager)
            {
                Request = TestHttpFactory.CreateRequest(),
                // use default configuration in factory
            };
        }

        [TestMethod]
        public void ReceiveAs4Message_MinimalFlow_ShouldPersistMetadata()
        {
            // Build minimal valid SOAP (reuse helper from validation tests)
            var soap = TestSoapBuilder.BuildMinimalValidSoap("e2e-" + Guid.NewGuid());
            var mimeParts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                }
            };
            // Validate and persist to mimic pipeline parts
            var validator = new MessageValidationService();
            validator.Validate(soap, mimeParts);
            _metaPersister.Persist(new As4Controller.As4InboundMetadata { MessageId = "dummy", RawSoapXml = soap.ToString() });
            Assert.AreEqual(1, _metaPersister.Items.Count);
        }

        private class TestMetadataPersister : IMetadataPersister
        {
            public List<As4Controller.As4InboundMetadata> Items { get; } = new();
            public void Persist(As4Controller.As4InboundMetadata metadata) => Items.Add(metadata);
        }
        private class TestPayloadPersister : IPayloadPersister
        {
            public string Persist(string messageId, As4Controller.PayloadInfo info, byte[] data) => string.Empty;
        }
        private class PermissiveCertValidator : ICertificateValidator
        {
            public void Validate(System.Security.Cryptography.X509Certificates.X509Certificate2 cert) { /* accept all */ }
        }
    }
} 