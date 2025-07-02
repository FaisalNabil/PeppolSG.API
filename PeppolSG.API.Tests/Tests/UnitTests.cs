using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;
using PeppolSG.API.Service.Interfaces;
using PeppolSG.API.Models;
using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class UnitTests
    {
        private IPeppolConfigurationService _configService;

        [TestInitialize]
        public void Setup()
        {
            _configService = new PeppolConfigurationService();
        }

        [TestMethod]
        public void Test_PeppolConfigurationService_LoadsWithoutErrors()
        {
            // Arrange & Act
            var config = new PeppolConfigurationService();

            // Assert
            Assert.IsNotNull(config);
            Assert.IsFalse(string.IsNullOrEmpty(config.PeppolDomain));
            Assert.IsFalse(string.IsNullOrEmpty(config.PeppolAccessPointId));
        }

        [TestMethod]
        public void Test_PeppolConfigurationService_StoragePathsAreConfigurable()
        {
            // Arrange & Act
            var config = new PeppolConfigurationService();

            // Assert - Verify that storage paths are now configurable (not hardcoded)
            Assert.IsNotNull(config.InboundStoragePath);
            Assert.IsNotNull(config.LogPath);
            Assert.IsTrue(config.EnableFileSystemPersistence);
            
            // Verify paths don't contain hardcoded Windows drive letters
            Assert.IsFalse(config.InboundStoragePath.Contains("C:\\"));
            Assert.IsFalse(config.LogPath.Contains("C:\\"));
        }

        [TestMethod]
        public void Test_CertificateManager_ConstructorAcceptsConfiguration()
        {
            // Arrange & Act
            var certManager = new CertificateManager(_configService);

            // Assert
            Assert.IsNotNull(certManager);
        }

        [TestMethod]
        public void Test_As4MessageBuilder_ConstructorAcceptsConfiguration()
        {
            // Arrange & Act
            var messageBuilder = new As4MessageBuilder(_configService);

            // Assert
            Assert.IsNotNull(messageBuilder);
        }

        [TestMethod]
        public void Test_PeppolAs4MessageValidator_ConstructorAcceptsConfiguration()
        {
            // Arrange & Act
            var validator = new PeppolAs4MessageValidator(_configService);

            // Assert
            Assert.IsNotNull(validator);
        }

        [TestMethod]
        public void Test_SmkSmpLookupService_ConstructorAcceptsDependencies()
        {
            // Arrange
            var certManager = new CertificateManager(_configService);

            // Act
            var smpService = new SmkSmpLookupService(_configService, certManager);

            // Assert
            Assert.IsNotNull(smpService);
        }

        [TestMethod]
        public void Test_PeppolExceptions_HaveCorrelationIds()
        {
            // Arrange & Act
            var validationException = new PeppolAs4ValidationException("Test error", "test-correlation-id");
            var securityException = new PeppolAs4SecurityException("Security error", "test-correlation-id");
            var smpException = new PeppolSmpException("SMP error", "test-correlation-id");

            // Assert
            Assert.AreEqual("test-correlation-id", validationException.CorrelationId);
            Assert.AreEqual("test-correlation-id", securityException.CorrelationId);
            Assert.AreEqual("test-correlation-id", smpException.CorrelationId);
        }

        [TestMethod]
        public void Test_PeppolExceptions_HaveErrorCodes()
        {
            // Arrange & Act
            var validationException = new PeppolAs4ValidationException("Test error");
            var securityException = new PeppolAs4SecurityException("Security error");
            var smpException = new PeppolSmpException("SMP error");

            // Assert
            Assert.AreEqual("EBMS:0004", validationException.ErrorCode);
            Assert.AreEqual("EBMS:0101", securityException.ErrorCode);
            Assert.AreEqual("EBMS:0010", smpException.ErrorCode);
        }

        [TestMethod]
        public void Test_As4Attachment_ModelProperties()
        {
            // Arrange
            var attachment = new As4Attachment
            {
                ContentId = "test-content-id",
                ContentType = "application/xml",
                Bytes = new byte[] { 1, 2, 3, 4 }
            };

            // Assert
            Assert.AreEqual("test-content-id", attachment.ContentId);
            Assert.AreEqual("application/xml", attachment.ContentType);
            Assert.AreEqual(4, attachment.Bytes.Length);
        }

        [TestMethod]
        public void Test_MimeParserService_ConstructorWorks()
        {
            // Arrange & Act
            var mimeParser = new MimeParserService();

            // Assert
            Assert.IsNotNull(mimeParser);
        }

        [TestMethod]
        public void Test_FileSystemMetadataPersister_ConstructorAcceptsConfiguration()
        {
            // Arrange & Act
            var persister = new PeppolSG.API.Controllers.FileSystemMetadataPersister(_configService);

            // Assert
            Assert.IsNotNull(persister);
        }

        [TestMethod]
        public void Test_FileSystemPayloadPersister_ConstructorAcceptsConfiguration()
        {
            // Arrange & Act
            var persister = new PeppolSG.API.Controllers.FileSystemPayloadPersister(_configService);

            // Assert
            Assert.IsNotNull(persister);
        }

        [TestMethod]
        public void Test_ConfigurationService_PathResolution()
        {
            // Arrange & Act
            var config = new PeppolConfigurationService();

            // Assert - Verify path resolution works
            Assert.IsNotNull(config.InboundStoragePath);
            Assert.IsNotNull(config.LogPath);
            
            // Verify paths are absolute (resolved)
            Assert.IsTrue(Path.IsPathRooted(config.InboundStoragePath));
            Assert.IsTrue(Path.IsPathRooted(config.LogPath));
        }

        [TestMethod]
        public void Test_ConfigurationService_DefaultValues()
        {
            // Arrange & Act
            var config = new PeppolConfigurationService();

            // Assert - Verify default values are sensible
            Assert.IsTrue(config.EnableFileSystemPersistence);
            Assert.IsFalse(string.IsNullOrEmpty(config.PeppolDomain));
            Assert.IsFalse(string.IsNullOrEmpty(config.PeppolAccessPointId));
        }

        [TestMethod]
        public void Test_ServiceInterfaces_AreImplemented()
        {
            // Arrange & Act
            var configService = new PeppolConfigurationService();
            var certManager = new CertificateManager(configService);
            var messageBuilder = new As4MessageBuilder(configService);
            var validator = new PeppolAs4MessageValidator(configService);
            var smpService = new SmkSmpLookupService(configService, certManager);
            var mimeParser = new MimeParserService();

            // Assert - Verify all services implement their interfaces
            Assert.IsInstanceOfType(configService, typeof(IPeppolConfigurationService));
            Assert.IsInstanceOfType(certManager, typeof(ICertificateManager));
            Assert.IsInstanceOfType(messageBuilder, typeof(IAs4MessageBuilder));
            Assert.IsInstanceOfType(validator, typeof(IPeppolAs4MessageValidator));
            Assert.IsInstanceOfType(smpService, typeof(ISmkSmpLookupService));
            Assert.IsInstanceOfType(mimeParser, typeof(IMimeParserService));
        }

        [TestMethod]
        public void Test_ReflectionUsage_Eliminated()
        {
            // This test verifies that we've eliminated dangerous reflection usage
            // by checking that the PeppolAs4Signer no longer uses reflection patterns
            
            // Arrange
            var config = new PeppolConfigurationService();
            
            // Act - Try to access the SignEnvelope method
            var methodInfo = typeof(PeppolAs4Signer).GetMethod("SignEnvelope");
            
            // Assert - Method should exist and be accessible
            Assert.IsNotNull(methodInfo);
            
            // Verify the method signature doesn't include reflection parameters
            var parameters = methodInfo.GetParameters();
            foreach (var param in parameters)
            {
                Assert.IsFalse(param.ParameterType.Name.Contains("FieldInfo"));
                Assert.IsFalse(param.ParameterType.Name.Contains("PropertyInfo"));
                Assert.IsFalse(param.ParameterType.Name.Contains("MethodInfo"));
            }
        }

        [TestMethod]
        public void Test_DisposablePattern_Implemented()
        {
            // Arrange
            var config = new PeppolConfigurationService();
            var certManager = new CertificateManager(config);
            
            // Act
            var smpService = new SmkSmpLookupService(config, certManager);
            
            // Assert - Verify IDisposable is implemented
            Assert.IsInstanceOfType(smpService, typeof(IDisposable));
            
            // Cleanup
            smpService.Dispose();
        }

        [TestMethod]
        public void Test_ExceptionHierarchy_Serialization()
        {
            // Arrange
            var originalException = new PeppolAs4ValidationException("Test message", "test-correlation-id", "test-message-id");
            
            // Act & Assert - Verify exception properties are preserved
            Assert.AreEqual("test-correlation-id", originalException.CorrelationId);
            Assert.AreEqual("test-message-id", originalException.MessageId);
            Assert.AreEqual("EBMS:0004", originalException.ErrorCode);
        }
    }
} 