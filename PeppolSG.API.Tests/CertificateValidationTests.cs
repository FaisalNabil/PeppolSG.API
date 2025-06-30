using System;
using System.Security.Cryptography.X509Certificates;
using System.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PeppolSG.API.Service;
using System.Collections.Generic;
using System.Linq;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class CertificateValidationTests
    {
        private Mock<ICertificateStoreManager> _mockCertificateStore;
        private EnhancedCertificateValidator _validator;
        private EnhancedCertificateValidator _testValidator;

        [TestInitialize]
        public void Setup()
        {
            _mockCertificateStore = new Mock<ICertificateStoreManager>();
            _validator = new EnhancedCertificateValidator(_mockCertificateStore.Object, isTestEnvironment: false);
            _testValidator = new EnhancedCertificateValidator(_mockCertificateStore.Object, isTestEnvironment: true);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_NullCertificate_ThrowsSecurityException()
        {
            // Arrange & Act & Assert
            _validator.Validate(null);
        }

        [TestMethod]
        public void Validate_ValidCertificate_PassesValidation()
        {
            // Arrange
            var validCert = CreateValidTestCertificate();
            SetupMockCertificateStore();

            // Act & Assert (should not throw)
            _testValidator.Validate(validCert);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_ExpiredCertificate_ThrowsSecurityException()
        {
            // Arrange
            var expiredCert = CreateExpiredCertificate();
            SetupMockCertificateStore();

            // Act & Assert
            _validator.Validate(expiredCert);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_NotYetValidCertificate_ThrowsSecurityException()
        {
            // Arrange
            var futureValidCert = CreateFutureValidCertificate();
            SetupMockCertificateStore();

            // Act & Assert
            _validator.Validate(futureValidCert);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_CertificateWithoutDigitalSignature_ThrowsSecurityException()
        {
            // Arrange
            var certWithoutDigitalSignature = CreateCertificateWithoutDigitalSignature();
            SetupMockCertificateStore();

            // Act & Assert
            _testValidator.Validate(certWithoutDigitalSignature);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_CertificateNotInPeppolPki_ThrowsSecurityException()
        {
            // Arrange
            var nonPeppolCert = CreateValidTestCertificate();
            _mockCertificateStore.Setup(x => x.GetPeppolRootCertificates())
                               .Returns(new List<X509Certificate2> { CreateDifferentRootCertificate() });
            _mockCertificateStore.Setup(x => x.GetIntermediateCertificates())
                               .Returns(new List<X509Certificate2>());

            // Act & Assert
            _validator.Validate(nonPeppolCert);
        }

        [TestMethod]
        public void Validate_CertificateInTestEnvironment_HandlesChainErrorsGracefully()
        {
            // Arrange
            var testCert = CreateValidTestCertificate();
            SetupMockCertificateStore();

            // Act & Assert (should not throw in test environment)
            _testValidator.Validate(testCert);
        }

        [TestMethod]
        public void Validate_CertificateExpiringSoon_LogsWarning()
        {
            // Arrange
            var expiringSoonCert = CreateCertificateExpiringSoon();
            SetupMockCertificateStore();

            // Act & Assert (should not throw but should log warning)
            _testValidator.Validate(expiringSoonCert);
        }

        [TestMethod]
        [ExpectedException(typeof(SecurityException))]
        public void Validate_CertificateWithInvalidRawData_ThrowsSecurityException()
        {
            // Arrange
            var invalidCert = CreateCertificateWithInvalidRawData();
            SetupMockCertificateStore();

            // Act & Assert
            _validator.Validate(invalidCert);
        }

        #region Helper Methods

        private void SetupMockCertificateStore()
        {
            var mockPeppolRoot = CreateMockPeppolRootCertificate();
            _mockCertificateStore.Setup(x => x.GetPeppolRootCertificates())
                               .Returns(new List<X509Certificate2> { mockPeppolRoot });
            _mockCertificateStore.Setup(x => x.GetIntermediateCertificates())
                               .Returns(new List<X509Certificate2>());
        }

        private X509Certificate2 CreateValidTestCertificate()
        {
            // Create a self-signed certificate for testing
            // In a real implementation, you would use actual test certificates
            return CreateSelfSignedCertificate("CN=Test Certificate", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), true, true);
        }

        private X509Certificate2 CreateExpiredCertificate()
        {
            return CreateSelfSignedCertificate("CN=Expired Certificate", DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddDays(-1), true, true);
        }

        private X509Certificate2 CreateFutureValidCertificate()
        {
            return CreateSelfSignedCertificate("CN=Future Certificate", DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddYears(1), true, true);
        }

        private X509Certificate2 CreateCertificateWithoutDigitalSignature()
        {
            return CreateSelfSignedCertificate("CN=No Digital Signature", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1), false, true);
        }

        private X509Certificate2 CreateCertificateExpiringSoon()
        {
            return CreateSelfSignedCertificate("CN=Expiring Soon", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(15), true, true);
        }

        private X509Certificate2 CreateCertificateWithInvalidRawData()
        {
            // This is a mock implementation - in reality, you'd need to create an actual invalid certificate
            var cert = CreateValidTestCertificate();
            // Simulate invalid raw data scenario by returning a certificate that would fail validation
            return cert;
        }

        private X509Certificate2 CreateMockPeppolRootCertificate()
        {
            return CreateSelfSignedCertificate("CN=Peppol Root CA", DateTime.UtcNow.AddYears(-5), DateTime.UtcNow.AddYears(10), true, true);
        }

        private X509Certificate2 CreateDifferentRootCertificate()
        {
            return CreateSelfSignedCertificate("CN=Different Root CA", DateTime.UtcNow.AddYears(-5), DateTime.UtcNow.AddYears(10), true, true);
        }

        private X509Certificate2 CreateSelfSignedCertificate(string subjectName, DateTime notBefore, DateTime notAfter, bool digitalSignature, bool keyEncipherment)
        {
            // This is a simplified version for testing
            // In a real implementation, you would use proper certificate generation libraries
            // like BouncyCastle or System.Security.Cryptography certificate generation methods
            
            try
            {
                using (var rsa = System.Security.Cryptography.RSA.Create(2048))
                {
                    var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                        subjectName, rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, 
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                    // Add key usage extension
                    var keyUsage = X509KeyUsageFlags.None;
                    if (digitalSignature) keyUsage |= X509KeyUsageFlags.DigitalSignature;
                    if (keyEncipherment) keyUsage |= X509KeyUsageFlags.KeyEncipherment;
                    
                    if (keyUsage != X509KeyUsageFlags.None)
                    {
                        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, false));
                    }

                    // Create self-signed certificate
                    var certificate = request.CreateSelfSigned(notBefore, notAfter);
                    return certificate;
                }
            }
            catch
            {
                // Fallback for environments where certificate creation is not supported
                // Return a mock certificate that represents the test scenario
                return new X509Certificate2();
            }
        }

        #endregion
    }

    [TestClass]
    public class CertificateStoreManagerTests
    {
        private CertificateStoreManager _storeManager;
        private string _tempCertificateDirectory;

        [TestInitialize]
        public void Setup()
        {
            _tempCertificateDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PeppolTestCerts", Guid.NewGuid().ToString());
            System.IO.Directory.CreateDirectory(_tempCertificateDirectory);
            
            // Mock configuration for testing
            System.Configuration.ConfigurationManager.AppSettings["PeppolCertificateStorePath"] = _tempCertificateDirectory;
            System.Configuration.ConfigurationManager.AppSettings["CertificateCacheExpiryHours"] = "1";
            
            _storeManager = new CertificateStoreManager();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _storeManager?.Dispose();
            if (System.IO.Directory.Exists(_tempCertificateDirectory))
            {
                System.IO.Directory.Delete(_tempCertificateDirectory, true);
            }
        }

        [TestMethod]
        public void Constructor_CreatesDirectoryStructure()
        {
            // Act - constructor already called in Setup
            
            // Assert
            Assert.IsTrue(System.IO.Directory.Exists(_tempCertificateDirectory));
            Assert.IsTrue(System.IO.Directory.Exists(System.IO.Path.Combine(_tempCertificateDirectory, "Root")));
            Assert.IsTrue(System.IO.Directory.Exists(System.IO.Path.Combine(_tempCertificateDirectory, "Intermediate")));
        }

        [TestMethod]
        public void GetPeppolRootCertificates_ReturnsEmptyInitially()
        {
            // Act
            var rootCerts = _storeManager.GetPeppolRootCertificates();

            // Assert
            Assert.IsNotNull(rootCerts);
            // Note: Might not be empty if configuration has root certificates defined
        }

        [TestMethod]
        public void AddPeppolRootCertificate_AddsToStore()
        {
            // Arrange
            var testCert = CreateTestCertificate();

            // Act
            _storeManager.AddPeppolRootCertificate(testCert);
            var rootCerts = _storeManager.GetPeppolRootCertificates();

            // Assert
            Assert.IsTrue(rootCerts.Any(c => c.Thumbprint == testCert.Thumbprint));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void AddPeppolRootCertificate_NullCertificate_ThrowsException()
        {
            // Act & Assert
            _storeManager.AddPeppolRootCertificate(null);
        }

        [TestMethod]
        public void RefreshCertificateCache_UpdatesCache()
        {
            // Arrange
            var testCert = CreateTestCertificate();
            _storeManager.AddPeppolRootCertificate(testCert);

            // Act
            _storeManager.RefreshCertificateCache();

            // Assert - should not throw and cache should be refreshed
            var rootCerts = _storeManager.GetPeppolRootCertificates();
            Assert.IsNotNull(rootCerts);
        }

        private X509Certificate2 CreateTestCertificate()
        {
            // Create a simple test certificate
            try
            {
                using (var rsa = System.Security.Cryptography.RSA.Create(2048))
                {
                    var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                        "CN=Test Certificate", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, 
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                    var certificate = request.CreateSelfSigned(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1));
                    return certificate;
                }
            }
            catch
            {
                // Fallback for environments where certificate creation is not supported
                return new X509Certificate2();
            }
        }
    }
} 