using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using log4net;
using PeppolSG.API.Service.Interfaces;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Manages Peppol certificates, including loading, validation, and trust chain verification.
    /// Implements certificate handling requirements for Peppol testbed compliance.
    /// </summary>
    public class CertificateManager : ICertificateManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(CertificateManager));
        private readonly IPeppolConfigurationService _configService;

        public CertificateManager(IPeppolConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        /// <summary>
        /// Loads the signing certificate from the configured path and password.
        /// </summary>
        public X509Certificate2 LoadSigningCertificate()
        {
            var certPath = _configService.PeppolP12FilePath;
            var certPassword = _configService.PeppolP12Password;
            log.Info($"Loading signing certificate from: {certPath}");

            try
            {
                var certificate = new X509Certificate2(certPath, certPassword,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
                
                log.Info($"Successfully loaded certificate '{certificate.Subject}' with thumbprint '{certificate.Thumbprint}'");
                return certificate;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to load signing certificate from '{certPath}': {ex.Message}", ex);
                throw new CryptographicException($"Certificate loading failed for path '{certPath}'", ex);
            }
        }

        /// <summary>
        /// Validates a certificate against Peppol PKI requirements.
        /// </summary>
        public bool ValidateCertificate(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                log.Warn("Certificate validation failed: Certificate is null");
                return false;
            }

            log.Info($"Validating certificate: {certificate.Subject}");

            // 1. Check certificate usage
            if (!IsPeppolCertificateUsageValid(certificate))
            {
                log.Warn($"Certificate usage validation failed for {certificate.Subject}");
                return false;
            }

            // 2. Validate against trust chain
            if (!IsCertificateChainValid(certificate))
            {
                log.Warn($"Certificate trust chain validation failed for {certificate.Subject}");
                return false;
            }
            
            log.Info($"Certificate '{certificate.Subject}' successfully validated");
            return true;
        }

        /// <summary>
        /// Validates certificate for Peppol-specific key usage and validity period.
        /// </summary>
        private bool IsPeppolCertificateUsageValid(X509Certificate2 certificate)
        {
            try
            {
                // Check validity period
                if (DateTime.UtcNow < certificate.NotBefore || DateTime.UtcNow > certificate.NotAfter)
                {
                    log.Warn($"Certificate is outside its validity period: NotBefore={certificate.NotBefore}, NotAfter={certificate.NotAfter}");
                    return false;
                }

                // Check key usage for digital signature
                var keyUsageExtension = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
                if (keyUsageExtension != null && !keyUsageExtension.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
                {
                    log.Warn("Certificate does not have 'DigitalSignature' key usage");
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Error validating Peppol certificate usage: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Validates the certificate chain against the machine's trust store.
        /// For Peppol, this should include the Peppol Root CAs.
        /// </summary>
        private bool IsCertificateChainValid(X509Certificate2 certificate)
        {
            try
            {
                using (var chain = new X509Chain())
                {
                    // Configure chain validation policy for Peppol
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                    chain.ChainPolicy.VerificationTime = DateTime.UtcNow;
                    chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0); // 1-minute timeout

                    // Build and validate the chain
                    if (!chain.Build(certificate))
                    {
                        log.Warn($"Certificate chain validation failed for '{certificate.Subject}'");
                        foreach (var status in chain.ChainStatus)
                        {
                            log.Warn($"  - Status: {status.Status}, Info: {status.StatusInformation}");
                        }
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Error during certificate chain validation: {ex.Message}", ex);
                return false;
            }
        }
    }
} 