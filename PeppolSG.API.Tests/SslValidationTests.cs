using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PeppolSG.API.Service;
using System.Collections.Generic;
using System.Linq;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class SslValidationTests
    {
        private Mock<ICertificateStoreManager> _mockCertificateStore;
        private SecureSslValidationService _sslValidator;
        private SecureSslValidationService _testSslValidator;

        [TestInitialize]
        public void Setup()
        {
            _mockCertificateStore = new Mock<ICertificateStoreManager>();
            _sslValidator = new SecureSslValidationService(_mockCertificateStore.Object, isTestEnvironment: false);
            _testSslValidator = new SecureSslValidationService(_mockCertificateStore.Object, isTestEnvironment: true);
            
            SetupMockCertificateStore();
        }

        #region SSL Certificate Validation Tests

        [TestMethod]
        public void ValidateServerCertificate_ValidCertificate_ReturnsTrue()
        {
            // Arrange
            var validCert = CreateValidSslCertificate("peppol.example.com");
            var chain = new X509Chain();
            var uri = new Uri("https://peppol.example.com");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, validCert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateServerCertificate_ExpiredCertificate_ReturnsFalse()
        {
            // Arrange
            var expiredCert = CreateExpiredSslCertificate("peppol.example.com");
            var chain = new X509Chain();
            var uri = new Uri("https://peppol.example.com");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, expiredCert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateServerCertificate_CertificateNotAvailable_ReturnsFalse()
        {
            // Arrange
            var uri = new Uri("https://peppol.example.com");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, null, null, SslPolicyErrors.RemoteCertificateNotAvailable, uri);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateServerCertificate_HostnameMismatch_ReturnsFalse()
        {
            // Arrange
            var cert = CreateValidSslCertificate("different.example.com");
            var chain = new X509Chain();
            var uri = new Uri("https://peppol.example.com");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.RemoteCertificateNameMismatch, uri);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region Environment-Aware SSL Validation Tests

        [TestMethod]
        public void ValidateServerCertificate_TestEnvironmentWithChainErrors_AllowsKnownTestEndpoints()
        {
            // Arrange
            var testCert = CreateValidSslCertificate("acc.edelivery.tech.ec.europa.eu");
            var chain = new X509Chain();
            var uri = new Uri("https://acc.edelivery.tech.ec.europa.eu");

            // Act
            var result = _testSslValidator.ValidateServerCertificate(null, testCert, chain, SslPolicyErrors.RemoteCertificateChainErrors, uri);

            // Assert
            Assert.IsTrue(result); // Should allow in test environment
        }

        [TestMethod]
        public void ValidateServerCertificate_ProductionEnvironmentWithChainErrors_ReturnsFalse()
        {
            // Arrange
            var cert = CreateValidSslCertificate("acc.edelivery.tech.ec.europa.eu");
            var chain = new X509Chain();
            var uri = new Uri("https://acc.edelivery.tech.ec.europa.eu");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.RemoteCertificateChainErrors, uri);

            // Assert
            Assert.IsFalse(result); // Should reject in production environment
        }

        [TestMethod]
        public void ValidateServerCertificate_TestEnvironmentLocalhost_AllowsNameMismatch()
        {
            // Arrange
            var cert = CreateValidSslCertificate("test-certificate");
            var chain = new X509Chain();
            var uri = new Uri("https://localhost:8080");

            // Act
            var result = _testSslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.RemoteCertificateNameMismatch, uri);

            // Assert
            Assert.IsTrue(result); // Should allow localhost in test environment
        }

        #endregion

        #region SSL Pinning Tests

        [TestMethod]
        public void ValidateServerCertificate_NonPeppolEndpoint_SkipsPinningValidation()
        {
            // Arrange
            var cert = CreateValidSslCertificate("external.example.com");
            var chain = new X509Chain();
            var uri = new Uri("https://external.example.com");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsTrue(result); // Should pass validation for non-Peppol endpoints
        }

        [TestMethod]
        public void ValidateServerCertificate_PeppolEndpointNoPinning_AllowsValidCertificate()
        {
            // Arrange
            var cert = CreateValidSslCertificate("smp-test.peppol.org");
            var chain = new X509Chain();
            var uri = new Uri("https://smp-test.peppol.org");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsTrue(result); // Should allow when no pinning is configured
        }

        #endregion

        #region Hostname Matching Tests

        [TestMethod]
        public void ValidateServerCertificate_ExactHostnameMatch_ReturnsTrue()
        {
            // Arrange
            var cert = CreateValidSslCertificate("smp-test.peppol.org");
            var chain = new X509Chain();
            var uri = new Uri("https://smp-test.peppol.org");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateServerCertificate_WildcardCertificate_MatchesSubdomain()
        {
            // Arrange
            var cert = CreateValidSslCertificate("*.peppol.org");
            var chain = new X509Chain();
            var uri = new Uri("https://smp.peppol.org");

            // Act
            var result = _sslValidator.ValidateServerCertificate(null, cert, chain, SslPolicyErrors.None, uri);

            // Assert
            Assert.IsTrue(result);
        }

        #endregion

        #region Helper Methods

        private void SetupMockCertificateStore()
        {
            _mockCertificateStore.Setup(x => x.GetIntermediateCertificates())
                               .Returns(new List<X509Certificate2>());
        }

        private X509Certificate2 CreateValidSslCertificate(string commonName)
        {
            return CreateSslCertificate(commonName, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddYears(1));
        }

        private X509Certificate2 CreateExpiredSslCertificate(string commonName)
        {
            return CreateSslCertificate(commonName, DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddDays(-1));
        }

        private X509Certificate2 CreateSslCertificate(string commonName, DateTime notBefore, DateTime notAfter)
        {
            try
            {
                using (var rsa = System.Security.Cryptography.RSA.Create(2048))
                {
                    var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                        $"CN={commonName}", rsa, System.Security.Cryptography.HashAlgorithmName.SHA256, 
                        System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                    // Add server authentication usage
                    request.CertificateExtensions.Add(new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

                    // Add Subject Alternative Name if it's a wildcard or specific hostname
                    if (commonName.Contains("."))
                    {
                        var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
                        sanBuilder.AddDnsName(commonName);
                        request.CertificateExtensions.Add(sanBuilder.Build());
                    }

                    var certificate = request.CreateSelfSigned(notBefore, notAfter);
                    return certificate;
                }
            }
            catch
            {
                // Fallback for environments where certificate creation is not supported
                return new X509Certificate2();
            }
        }

        #endregion
    }

    [TestClass]
    public class TlsConfigurationTests
    {
        [TestMethod]
        public void InitializeTlsConfiguration_CallMultipleTimes_DoesNotThrow()
        {
            // Act & Assert - should not throw
            TlsConfigurationService.InitializeTlsConfiguration();
            TlsConfigurationService.InitializeTlsConfiguration(); // Second call should be ignored
        }

        [TestMethod]
        public void ValidateConfiguration_ReturnsValidStatus()
        {
            // Arrange
            TlsConfigurationService.InitializeTlsConfiguration();

            // Act
            var status = TlsConfigurationService.ValidateConfiguration();

            // Assert
            Assert.IsNotNull(status);
            Assert.IsNotNull(status.SupportedProtocols);
            Assert.IsTrue(status.IsTls12Enabled, "TLS 1.2 should be enabled");
            Assert.IsFalse(status.HasLegacyProtocols, "Legacy protocols should be disabled");
        }

        [TestMethod]
        public void GetConfigurationSummary_ReturnsNonEmptyString()
        {
            // Arrange
            TlsConfigurationService.InitializeTlsConfiguration();

            // Act
            var summary = TlsConfigurationService.GetConfigurationSummary();

            // Assert
            Assert.IsFalse(string.IsNullOrEmpty(summary));
            Assert.IsTrue(summary.Contains("TLS"), "Summary should contain TLS information");
        }

        [TestMethod]
        public void TlsConfigurationStatus_DefaultValues_AreValid()
        {
            // Act
            var status = new TlsConfigurationStatus();

            // Assert
            Assert.IsNotNull(status);
            Assert.IsFalse(status.IsSecure); // Should be false by default
        }
    }
} 