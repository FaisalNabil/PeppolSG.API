using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security;
using log4net;
using PeppolSG.API.Controllers;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Enhanced certificate validator that implements comprehensive certificate validation
    /// for Peppol AS4 messaging including chain validation, revocation checking, and PKI validation
    /// </summary>
    public class EnhancedCertificateValidator : ICertificateValidator
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(EnhancedCertificateValidator));
        private readonly ICertificateStoreManager _certificateStore;
        private readonly bool _isTestEnvironment;

        public EnhancedCertificateValidator(ICertificateStoreManager certificateStore = null, bool isTestEnvironment = false)
        {
            _certificateStore = certificateStore ?? new CertificateStoreManager();
            _isTestEnvironment = isTestEnvironment;
        }

        /// <summary>
        /// Validates certificate according to Peppol AS4 Profile requirements
        /// </summary>
        /// <param name="cert">Certificate to validate</param>
        /// <exception cref="SecurityException">Thrown when certificate validation fails</exception>
        public void Validate(X509Certificate2 cert)
        {
            if (cert == null)
                throw new SecurityException("Certificate cannot be null");

            try
            {
                log.Info($"Starting certificate validation for certificate with thumbprint: {cert.Thumbprint}");

                // 1. Basic certificate validation
                ValidateBasicCertificateProperties(cert);

                // 2. Certificate expiry validation
                ValidateCertificateExpiry(cert);

                // 3. Certificate chain validation
                ValidateCertificateChain(cert);

                // 4. Peppol PKI root certificate validation
                ValidatePeppolPkiChain(cert);

                // 5. Certificate usage validation
                ValidateCertificateUsage(cert);

                log.Info($"Certificate validation successful for thumbprint: {cert.Thumbprint}");
            }
            catch (Exception ex)
            {
                log.Error($"Certificate validation failed for thumbprint: {cert?.Thumbprint}", ex);
                throw;
            }
        }

        /// <summary>
        /// Validates basic certificate properties
        /// </summary>
        private void ValidateBasicCertificateProperties(X509Certificate2 cert)
        {
            // Check if certificate has a public key
            if (cert.PublicKey == null)
                throw new SecurityException("Certificate must have a valid public key");

            // Check certificate format
            if (cert.RawData == null || cert.RawData.Length == 0)
                throw new SecurityException("Certificate has invalid raw data");

            // Check subject
            if (string.IsNullOrEmpty(cert.Subject))
                throw new SecurityException("Certificate must have a valid subject");

            log.Debug($"Basic certificate properties validated for subject: {cert.Subject}");
        }

        /// <summary>
        /// Validates certificate expiry dates
        /// </summary>
        private void ValidateCertificateExpiry(X509Certificate2 cert)
        {
            var now = DateTime.UtcNow;

            if (cert.NotBefore > now)
                throw new SecurityException($"Certificate is not yet valid. Valid from: {cert.NotBefore}");

            if (cert.NotAfter < now)
                throw new SecurityException($"Certificate has expired. Expired on: {cert.NotAfter}");

            // Warning for certificates expiring soon (30 days)
            if (cert.NotAfter < now.AddDays(30))
                log.Warn($"Certificate expires soon: {cert.NotAfter}. Subject: {cert.Subject}");

            log.Debug($"Certificate expiry validation passed. Valid until: {cert.NotAfter}");
        }

        /// <summary>
        /// Validates the certificate chain and revocation status
        /// </summary>
        private void ValidateCertificateChain(X509Certificate2 cert)
        {
            using (var chain = new X509Chain())
            {
                // Configure chain policy
                chain.ChainPolicy.RevocationMode = _isTestEnvironment ? X509RevocationMode.NoCheck : X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(30);

                // Add extra certificates from certificate store if available
                var extraCerts = _certificateStore.GetIntermediateCertificates();
                foreach (var extraCert in extraCerts)
                {
                    chain.ChainPolicy.ExtraStore.Add(extraCert);
                }

                // Build and validate chain
                bool isValid = chain.Build(cert);

                if (!isValid)
                {
                    var errors = chain.ChainStatus
                        .Where(status => status.Status != X509ChainStatusFlags.NoError)
                        .Select(status => $"{status.Status}: {status.StatusInformation}")
                        .ToList();

                    var errorMessage = $"Certificate chain validation failed: {string.Join(", ", errors)}";
                    
                    // In test environment, log warnings instead of throwing for certain non-critical errors
                    if (_isTestEnvironment && IsNonCriticalChainError(chain.ChainStatus))
                    {
                        log.Warn($"Certificate chain validation issues in test environment: {errorMessage}");
                        return;
                    }

                    throw new SecurityException(errorMessage);
                }

                log.Debug($"Certificate chain validation passed with {chain.ChainElements.Count} elements");
            }
        }

        /// <summary>
        /// Validates that the certificate is part of the Peppol PKI
        /// </summary>
        private void ValidatePeppolPkiChain(X509Certificate2 cert)
        {
            var peppolRootCerts = _certificateStore.GetPeppolRootCertificates();
            
            if (!peppolRootCerts.Any())
            {
                log.Warn("No Peppol root certificates configured. Skipping Peppol PKI validation.");
                return;
            }

            using (var chain = new X509Chain())
            {
                // Add Peppol root certificates to trusted roots
                foreach (var rootCert in peppolRootCerts)
                {
                    chain.ChainPolicy.ExtraStore.Add(rootCert);
                }

                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Peppol handles revocation differently
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                if (!chain.Build(cert))
                {
                    throw new SecurityException("Certificate is not part of the Peppol PKI chain");
                }

                // Verify that the root certificate is actually a Peppol root
                var rootElement = chain.ChainElements[chain.ChainElements.Count - 1];
                var isPeppolRoot = peppolRootCerts.Any(root => 
                    root.Thumbprint.Equals(rootElement.Certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));

                if (!isPeppolRoot)
                {
                    throw new SecurityException("Certificate chain does not terminate with a valid Peppol root certificate");
                }

                log.Debug("Peppol PKI chain validation passed");
            }
        }

        /// <summary>
        /// Validates certificate usage and key usage extensions
        /// </summary>
        private void ValidateCertificateUsage(X509Certificate2 cert)
        {
            // Check for digital signature capability
            bool hasDigitalSignature = false;
            bool hasKeyEncipherment = false;

            foreach (var extension in cert.Extensions)
            {
                if (extension is X509KeyUsageExtension keyUsage)
                {
                    hasDigitalSignature = keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature);
                    hasKeyEncipherment = keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment);
                }
            }

            if (!hasDigitalSignature)
                throw new SecurityException("Certificate must have digital signature capability for AS4 messaging");

            if (!hasKeyEncipherment)
                log.Warn("Certificate does not have key encipherment capability - encryption may not be supported");

            log.Debug("Certificate usage validation passed");
        }

        /// <summary>
        /// Determines if chain errors are non-critical in test environment
        /// </summary>
        private bool IsNonCriticalChainError(X509ChainStatus[] chainStatus)
        {
            var criticalErrors = new[]
            {
                X509ChainStatusFlags.NotTimeValid,
                X509ChainStatusFlags.Revoked,
                X509ChainStatusFlags.NotSignatureValid
            };

            return !chainStatus.Any(status => criticalErrors.Contains(status.Status));
        }
    }

    /// <summary>
    /// Interface for certificate store management
    /// </summary>
    public interface ICertificateStoreManager
    {
        IEnumerable<X509Certificate2> GetPeppolRootCertificates();
        IEnumerable<X509Certificate2> GetIntermediateCertificates();
        void AddPeppolRootCertificate(X509Certificate2 certificate);
        void RefreshCertificateCache();
    }
} 