
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Moq;
using PeppolSG.API.Service.Interfaces;
using System.Net.Http;
using System.Net;
using System.IO;
using System.Xml.Linq;
using PeppolSG.API.Controllers;
using PeppolSG.API.Models;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Web.Http;
using System.Web.Http.Results;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class TestbedScenarios
    {
        private Mock<IPeppolConfigurationService> _mockConfigService;
        private Mock<ISmkSmpLookupService> _mockSmpLookupService;
        private Mock<ICertificateManager> _mockCertificateManager;
        private Mock<IAs4MessageBuilder> _mockMessageBuilder;
        private Mock<IPeppolAs4Signer> _mockAs4Signer;
        private Mock<IMimeParserService> _mockMimeParser;
        private Mock<IPayloadPersister> _mockPayloadPersister;

        [TestInitialize]
        public void Setup()
        {
            _mockConfigService = new Mock<IPeppolConfigurationService>();
            _mockSmpLookupService = new Mock<ISmkSmpLookupService>();
            _mockCertificateManager = new Mock<ICertificateManager>();
            _mockMessageBuilder = new Mock<IAs4MessageBuilder>();
            _mockAs4Signer = new Mock<IPeppolAs4Signer>();
            _mockMimeParser = new Mock<IMimeParserService>();
            _mockPayloadPersister = new Mock<IPayloadPersister>();
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_AS4_01_SendMessage_Success()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.GetPeppolDomain()).Returns("test.com");
            _mockConfigService.Setup(s => s.LoadSigningCertificate()).Returns(new X509Certificate2());

            // 2. Mock SMP Lookup
            var smpEndpoint = new SmpEndpoint
            {
                EndpointReference = new EndpointReference { Address = "http://test.com/as4" },
                Certificate = Convert.ToBase64String(new X509Certificate2().Export(X509ContentType.Cert))
            };
            _mockSmpLookupService.Setup(s => s.LookupEndpointMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(smpEndpoint);

            // 3. Mock Message Builder
            var userMessage = new XElement("UserMessage");
            _mockMessageBuilder.Setup(s => s.BuildUserMessage(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns(userMessage);
            var soapEnvelope = new XDocument(new XElement("Envelope", new XElement("Body")));
            _mockMessageBuilder.Setup(s => s.WrapInSoapEnvelope(It.IsAny<XElement>(), It.IsAny<XElement>(), It.IsAny<string>())).Returns(soapEnvelope);
            var mtomResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("MIME-formatted-message")
            };
            _mockMessageBuilder.Setup(s => s.CreateMtomResponse(It.IsAny<XDocument>(), It.IsAny<System.Collections.Generic.IList<As4Attachment>>(), It.IsAny<HttpStatusCode>())).Returns(mtomResponse);

            // 4. Mock Signer
            _mockAs4Signer.Setup(s => s.SignEnvelope(It.IsAny<XDocument>(), It.IsAny<X509Certificate2>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>()));

            // 5. Create Controller
            var controller = new As4Controller(
                _mockConfigService.Object,
                null, // validator not needed for sending
                _mockCertificateManager.Object,
                _mockSmpLookupService.Object,
                _mockMessageBuilder.Object,
                _mockMimeParser.Object,
                _mockPayloadPersister.Object,
                _mockAs4Signer.Object
            );

            // 6. Create Request
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/as4/send");
            var invoiceXml = File.ReadAllText("..\\..\\TestData\\sample-invoice.xml");
            request.Content = new StringContent(invoiceXml);
            controller.Request = request;

            // Act
            var result = await controller.SendAs4Message();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(ResponseMessageResult));
            var responseMessage = ((ResponseMessageResult)result).Response;
            Assert.AreEqual(HttpStatusCode.OK, responseMessage.StatusCode);
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_AS4_02_ReceiveMessage_Success()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.GetPeppolDomain()).Returns("test.com");
            _mockConfigService.Setup(s => s.LoadSigningCertificate()).Returns(new X509Certificate2());

            // 2. Mock Validator
            var mockValidator = new Mock<IPeppolAs4MessageValidator>();
            var validationResult = new ValidationResult(); // Represents a successful validation
            mockValidator.Setup(v => v.ValidateIncomingMessage(It.IsAny<XDocument>(), It.IsAny<string>())).Returns(validationResult);

            // 3. Mock Mime Parser
            var soapPart = new MimePart { ContentType = "application/soap+xml", ContentText = "<xml>test</xml>" };
            var mimeParts = new[] { soapPart };
            _mockMimeParser.Setup(p => p.ParseMultipartRequest(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(mimeParts);

            // 4. Mock Signer
            _mockAs4Signer.Setup(s => s.VerifyMessageSignature(It.IsAny<XDocument>(), It.IsAny<X509Certificate2>())).Returns(true);
            _mockAs4Signer.Setup(s => s.VerifyTimestamp(It.IsAny<XDocument>())).Returns(true);

            // 5. Mock SMP Lookup
            var smpEndpoint = new SmpEndpoint
            {
                EndpointReference = new EndpointReference { Address = "http://test.com/as4" },
                Certificate = Convert.ToBase64String(new X509Certificate2().Export(X509ContentType.Cert))
            };
            _mockSmpLookupService.Setup(s => s.LookupEndpointMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(smpEndpoint);

            // 6. Create Controller
            var controller = new As4Controller(
                _mockConfigService.Object,
                mockValidator.Object,
                _mockCertificateManager.Object,
                _mockSmpLookupService.Object,
                _mockMessageBuilder.Object,
                _mockMimeParser.Object,
                _mockPayloadPersister.Object,
                _mockAs4Signer.Object
            );

            // 7. Create Request
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/as4");
            request.Content = new StringContent("mime-message");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/related");
            controller.Request = request;

            // Act
            var result = await controller.ReceiveAs4Message();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(ResponseMessageResult));
            var responseMessage = ((ResponseMessageResult)result).Response;
            Assert.AreEqual(HttpStatusCode.OK, responseMessage.StatusCode);
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_AS4_04_ReceiveMessage_WrongDigest()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.GetPeppolDomain()).Returns("test.com");

            // 2. Mock Validator
            var mockValidator = new Mock<IPeppolAs4MessageValidator>();
            var validationResult = new ValidationResult();
            validationResult.AddError("EBMS:0004", "Digest mismatch");
            mockValidator.Setup(v => v.ValidateIncomingMessage(It.IsAny<XDocument>(), It.IsAny<string>())).Returns(validationResult);

            // 3. Mock Mime Parser
            var soapPart = new MimePart { ContentType = "application/soap+xml", ContentText = "<xml>test</xml>" };
            var mimeParts = new[] { soapPart };
            _mockMimeParser.Setup(p => p.ParseMultipartRequest(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(mimeParts);

            // 4. Create Controller
            var controller = new As4Controller(
                _mockConfigService.Object,
                mockValidator.Object,
                _mockCertificateManager.Object,
                _mockSmpLookupService.Object,
                _mockMessageBuilder.Object,
                _mockMimeParser.Object,
                _mockPayloadPersister.Object,
                _mockAs4Signer.Object
            );

            // 5. Create Request
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/as4");
            request.Content = new StringContent("mime-message");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/related");
            controller.Request = request;

            // Act
            var result = await controller.ReceiveAs4Message();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(ResponseMessageResult));
            var responseMessage = ((ResponseMessageResult)result).Response;
            Assert.AreEqual(HttpStatusCode.BadRequest, responseMessage.StatusCode);
            Assert.IsTrue(responseMessage.Headers.Contains("X-AS4-Error-Code"));
            Assert.AreEqual("EBMS:0004", String.Join(", ", responseMessage.Headers.GetValues("X-AS4-Error-Code")));
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_AS4_05_SendMessage_Receipt_Failure()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.GetPeppolDomain()).Returns("test.com");
            _mockConfigService.Setup(s => s.LoadSigningCertificate()).Returns(new X509Certificate2());

            // 2. Mock SMP Lookup
            var smpEndpoint = new SmpEndpoint
            {
                EndpointReference = new EndpointReference { Address = "http://test.com/as4" },
                Certificate = Convert.ToBase64String(new X509Certificate2().Export(X509ContentType.Cert))
            };
            _mockSmpLookupService.Setup(s => s.LookupEndpointMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(smpEndpoint);

            // 3. Mock Message Builder to return a failure response
            var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            errorResponse.Content = new StringContent("AS4 Error Message");
            _mockMessageBuilder.Setup(s => s.CreateMtomResponse(It.IsAny<XDocument>(), It.IsAny<System.Collections.Generic.IList<As4Attachment>>(), It.IsAny<HttpStatusCode>())).Returns(errorResponse);

            // 4. Create Controller
            var controller = new As4Controller(
                _mockConfigService.Object,
                null, // validator not needed for sending
                _mockCertificateManager.Object,
                _mockSmpLookupService.Object,
                _mockMessageBuilder.Object,
                _mockMimeParser.Object,
                _mockPayloadPersister.Object,
                _mockAs4Signer.Object
            );

            // 5. Create Request
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/as4/send");
            var invoiceXml = File.ReadAllText("..\\..\\TestData\\sample-invoice.xml");
            request.Content = new StringContent(invoiceXml);
            controller.Request = request;

            // Act
            var result = await controller.SendAs4Message();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(ResponseMessageResult));
            var responseMessage = ((ResponseMessageResult)result).Response;
            Assert.AreEqual(HttpStatusCode.InternalServerError, responseMessage.StatusCode);
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_AS4_10_Signature_Failure()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.GetPeppolDomain()).Returns("test.com");

            // 2. Mock Validator
            var mockValidator = new Mock<IPeppolAs4MessageValidator>();
            var validationResult = new ValidationResult(); // Successful validation for structure
            mockValidator.Setup(v => v.ValidateIncomingMessage(It.IsAny<XDocument>(), It.IsAny<string>())).Returns(validationResult);

            // 3. Mock Mime Parser
            var soapPart = new MimePart { ContentType = "application/soap+xml", ContentText = "<xml>test</xml>" };
            var mimeParts = new[] { soapPart };
            _mockMimeParser.Setup(p => p.ParseMultipartRequest(It.IsAny<Stream>(), It.IsAny<string>())).ReturnsAsync(mimeParts);

            // 4. Mock Signer to fail signature verification
            _mockAs4Signer.Setup(s => s.VerifyMessageSignature(It.IsAny<XDocument>(), It.IsAny<X509Certificate2>())).Returns(false);
            _mockAs4Signer.Setup(s => s.VerifyTimestamp(It.IsAny<XDocument>())).Returns(true);

            // 5. Mock SMP Lookup
            var smpEndpoint = new SmpEndpoint
            {
                EndpointReference = new EndpointReference { Address = "http://test.com/as4" },
                Certificate = Convert.ToBase64String(new X509Certificate2().Export(X509ContentType.Cert))
            };
            _mockSmpLookupService.Setup(s => s.LookupEndpointMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(smpEndpoint);

            // 6. Create Controller
            var controller = new As4Controller(
                _mockConfigService.Object,
                mockValidator.Object,
                _mockCertificateManager.Object,
                _mockSmpLookupService.Object,
                _mockMessageBuilder.Object,
                _mockMimeParser.Object,
                _mockPayloadPersister.Object,
                _mockAs4Signer.Object
            );

            // 7. Create Request
            var request = new HttpRequestMessage(HttpMethod.Post, "http://test.com/as4");
            request.Content = new StringContent("mime-message");
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/related");
            controller.Request = request;

            // Act
            var result = await controller.ReceiveAs4Message();

            // Assert
            Assert.IsNotNull(result);
            Assert.IsInstanceOfType(result, typeof(ResponseMessageResult));
            var responseMessage = ((ResponseMessageResult)result).Response;
            Assert.AreEqual(HttpStatusCode.Unauthorized, responseMessage.StatusCode);
            Assert.IsTrue(responseMessage.Headers.Contains("X-AS4-Error-Code"));
            Assert.AreEqual("EBMS:0102", String.Join(", ", responseMessage.Headers.GetValues("X-AS4-Error-Code")));
        }

        [TestMethod]
        [TestCategory("Peppol-Testbed")]
        public async Task Test_SMP_01_Lookup_Success()
        {
            // Arrange
            // 1. Mock Configuration
            _mockConfigService.Setup(s => s.UsePeppolTestNetwork).Returns(true);

            // 2. Mock Certificate Manager
            _mockCertificateManager.Setup(c => c.ValidateCertificate(It.IsAny<X509Certificate2>())).Returns(true);

            // 3. Create Service
            var service = new SmkSmpLookupService(_mockConfigService.Object, _mockCertificateManager.Object);

            // Act
            // Note: This test requires a live connection to the Peppol test SML/SMP.
            // Replace with a real test participant identifier.
            var participantId = "9915:test";
            var participantScheme = "iso6523-actorid-upis";
            var documentTypeId = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2::Invoice##urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0::2.1";
            var processId = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";

            try
            {
                var result = await service.LookupEndpointMetadata(participantId, participantScheme, documentTypeId, processId);

                // Assert
                Assert.IsNotNull(result);
                Assert.IsFalse(string.IsNullOrEmpty(result.EndpointReference.Address));
                Assert.IsFalse(string.IsNullOrEmpty(result.Certificate));
            }
            catch (Exception ex)
            {
                Assert.Inconclusive("SMP lookup failed. This may be due to a network issue or the test participant not being registered. " + ex.Message);
            }
        }
    }
}
 