using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Secure SSL validation service that provides proper certificate validation
    /// for Peppol AS4 messaging without bypassing SSL security
    /// </summary>
    public class SecureSslValidationService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SecureSslValidationService));
        private readonly bool _isTestEnvironment;
        private readonly HashSet<string> _pinnedCertificateThumbprints;
        private readonly HashSet<string> _trustedEndpoints;
        private readonly ICertificateStoreManager _certificateStore;

        public SecureSslValidationService(ICertificateStoreManager certificateStore = null, bool isTestEnvironment = false)
        {
            _certificateStore = certificateStore ?? new CertificateStoreManager();
            _isTestEnvironment = isTestEnvironment;
            _pinnedCertificateThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _trustedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            InitializePinnedCertificates();
            InitializeTrustedEndpoints();
        }

        /// <summary>
        /// Validates SSL certificate with comprehensive security checks
        /// </summary>
        /// <param name="sender">The HttpWebRequest or HttpClient</param>
        /// <param name="certificate">The server certificate</param>
        /// <param name="chain">The certificate chain</param>
        /// <param name="sslPolicyErrors">SSL policy errors</param>
        /// <param name="requestUri">The request URI for context</param>
        /// <returns>True if certificate is valid, false otherwise</returns>
        public bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors, Uri requestUri = null)
        {
            try
            {
                log.Debug($"Validating SSL certificate for {requestUri?.Host ?? "unknown host"}");

                // Convert to X509Certificate2 for enhanced validation
                var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

                // 1. Check for SSL policy errors
                if (sslPolicyErrors != SslPolicyErrors.None)
                {
                    if (!HandleSslPolicyErrors(sslPolicyErrors, cert2, requestUri))
                    {
                        log.Error($"SSL policy errors detected for {requestUri?.Host}: {sslPolicyErrors}");
                        return false;
                    }
                }

                // 2. Validate certificate expiry
                if (!ValidateCertificateExpiry(cert2))
                {
                    log.Error($"Certificate expired or not yet valid for {requestUri?.Host}");
                    return false;
                }

                // 3. Validate certificate chain
                if (!ValidateCertificateChain(cert2, chain))
                {
                    log.Error($"Certificate chain validation failed for {requestUri?.Host}");
                    return false;
                }

                // 4. Check SSL pinning for known Peppol endpoints
                if (!ValidateSslPinning(cert2, requestUri))
                {
                    log.Error($"SSL pinning validation failed for {requestUri?.Host}");
                    return false;
                }

                // 5. Validate hostname matching
                if (!ValidateHostnameMatching(cert2, requestUri))
                {
                    log.Error($"Hostname validation failed for {requestUri?.Host}");
                    return false;
                }

                log.Info($"SSL certificate validation successful for {requestUri?.Host}");
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"SSL certificate validation error for {requestUri?.Host}: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Handles SSL policy errors based on environment and configuration
        /// </summary>
        private bool HandleSslPolicyErrors(SslPolicyErrors sslPolicyErrors, X509Certificate2 certificate, Uri requestUri)
        {
            // Critical errors that should never be ignored
            var criticalErrors = SslPolicyErrors.RemoteCertificateNotAvailable;
            
            if ((sslPolicyErrors & criticalErrors) != 0)
            {
                log.Error($"Critical SSL error detected: {sslPolicyErrors}");
                return false;
            }

            // In test environment, we may be more lenient with certain errors
            if (_isTestEnvironment)
            {
                // Allow self-signed certificates in test environment for known test endpoints
                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0 && IsTestEndpoint(requestUri))
                {
                    log.Warn($"Allowing certificate chain errors in test environment for {requestUri?.Host}");
                    return true;
                }

                // Allow name mismatch for localhost/test endpoints
                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 && IsLocalOrTestEndpoint(requestUri))
                {
                    log.Warn($"Allowing certificate name mismatch in test environment for {requestUri?.Host}");
                    return true;
                }
            }

            // In production, be strict about SSL errors
            return false;
        }

        /// <summary>
        /// Validates certificate expiry dates
        /// </summary>
        private bool ValidateCertificateExpiry(X509Certificate2 certificate)
        {
            var now = DateTime.UtcNow;
            
            if (certificate.NotBefore > now)
            {
                log.Error($"Certificate is not yet valid. Valid from: {certificate.NotBefore}");
                return false;
            }

            if (certificate.NotAfter < now)
            {
                log.Error($"Certificate has expired. Expired on: {certificate.NotAfter}");
                return false;
            }

            // Warning for certificates expiring soon (7 days for SSL certificates)
            if (certificate.NotAfter < now.AddDays(7))
            {
                log.Warn($"SSL certificate expires soon: {certificate.NotAfter}. Subject: {certificate.Subject}");
            }

            return true;
        }

        /// <summary>
        /// Validates the certificate chain
        /// </summary>
        private bool ValidateCertificateChain(X509Certificate2 certificate, X509Chain chain)
        {
            // Use provided chain or create new one
            var chainToValidate = chain ?? new X509Chain();
            bool shouldDisposeChain = chain == null;

            try
            {
                // Configure chain policy for SSL validation
                chainToValidate.ChainPolicy.RevocationMode = _isTestEnvironment ? X509RevocationMode.NoCheck : X509RevocationMode.Online;
                chainToValidate.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chainToValidate.ChainPolicy.VerificationFlags = _isTestEnvironment ? 
                    X509VerificationFlags.AllowUnknownCertificateAuthority : X509VerificationFlags.NoFlag;
                chainToValidate.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(30);

                // Add intermediate certificates from store
                var intermediateCerts = _certificateStore.GetIntermediateCertificates();
                foreach (var intermediateCert in intermediateCerts)
                {
                    chainToValidate.ChainPolicy.ExtraStore.Add(intermediateCert);
                }

                bool isValid = chainToValidate.Build(certificate);

                if (!isValid)
                {
                    var errors = chainToValidate.ChainStatus
                        .Where(status => status.Status != X509ChainStatusFlags.NoError)
                        .Select(status => $"{status.Status}: {status.StatusInformation}")
                        .ToList();

                    log.Error($"SSL certificate chain validation failed: {string.Join(", ", errors)}");
                }

                return isValid;
            }
            finally
            {
                if (shouldDisposeChain)
                {
                    chainToValidate?.Dispose();
                }
            }
        }

        /// <summary>
        /// Validates SSL pinning for known Peppol endpoints
        /// </summary>
        private bool ValidateSslPinning(X509Certificate2 certificate, Uri requestUri)
        {
            if (requestUri == null || !IsPeppolEndpoint(requestUri))
            {
                // Not a Peppol endpoint, skip pinning validation
                return true;
            }

            // Check if certificate is pinned
            var thumbprint = certificate.Thumbprint;
            if (_pinnedCertificateThumbprints.Contains(thumbprint))
            {
                log.Debug($"SSL certificate matches pinned certificate for {requestUri.Host}");
                return true;
            }

            // If pinning is configured but certificate doesn't match, reject
            if (_pinnedCertificateThumbprints.Any())
            {
                log.Error($"SSL certificate does not match any pinned certificates for Peppol endpoint {requestUri.Host}");
                return false;
            }

            // No pinning configured, allow
            log.Debug($"No SSL pinning configured for {requestUri.Host}");
            return true;
        }

        /// <summary>
        /// Validates hostname matching
        /// </summary>
        private bool ValidateHostnameMatching(X509Certificate2 certificate, Uri requestUri)
        {
            if (requestUri == null)
            {
                return true; // Cannot validate without URI
            }

            var hostname = requestUri.Host;
            
            // Check Subject Alternative Names
            foreach (var extension in certificate.Extensions)
            {
                if (extension.Oid.Value == "2.5.29.17") // Subject Alternative Name
                {
                    var sanExtension = extension as X509SubjectAlternativeNameExtension;
                    if (sanExtension != null)
                    {
                        var dnsNames = sanExtension.EnumerateDnsNames();
                        if (dnsNames.Any(dns => MatchesHostname(dns, hostname)))
                        {
                            return true;
                        }
                    }
                }
            }

            // Check certificate subject CN
            var subject = certificate.Subject;
            var cnMatch = System.Text.RegularExpressions.Regex.Match(subject, @"CN=([^,]+)");
            if (cnMatch.Success)
            {
                var cn = cnMatch.Groups[1].Value.Trim();
                if (MatchesHostname(cn, hostname))
                {
                    return true;
                }
            }

            log.Error($"Certificate hostname validation failed. Certificate: {subject}, Requested: {hostname}");
            return false;
        }

        /// <summary>
        /// Checks if a certificate name matches the hostname (supports wildcards)
        /// </summary>
        private bool MatchesHostname(string certificateName, string hostname)
        {
            if (string.IsNullOrEmpty(certificateName) || string.IsNullOrEmpty(hostname))
                return false;

            // Exact match
            if (certificateName.Equals(hostname, StringComparison.OrdinalIgnoreCase))
                return true;

            // Wildcard match (*.example.com matches sub.example.com but not example.com)
            if (certificateName.StartsWith("*."))
            {
                var domain = certificateName.Substring(2);
                var hostParts = hostname.Split('.');
                if (hostParts.Length > 1)
                {
                    var hostDomain = string.Join(".", hostParts.Skip(1));
                    return domain.Equals(hostDomain, StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        /// <summary>
        /// Initializes pinned certificate thumbprints from configuration
        /// </summary>
        private void InitializePinnedCertificates()
        {
            var pinnedCerts = ConfigurationManager.AppSettings["PeppolSslPinnedCertificates"];
            if (!string.IsNullOrEmpty(pinnedCerts))
            {
                var thumbprints = pinnedCerts.Split(';', ',')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t));

                foreach (var thumbprint in thumbprints)
                {
                    _pinnedCertificateThumbprints.Add(thumbprint);
                    log.Info($"Added pinned SSL certificate: {thumbprint}");
                }
            }
        }

        /// <summary>
        /// Initializes trusted endpoints from configuration
        /// </summary>
        private void InitializeTrustedEndpoints()
        {
            var trustedEndpoints = ConfigurationManager.AppSettings["PeppolTrustedEndpoints"];
            if (!string.IsNullOrEmpty(trustedEndpoints))
            {
                var endpoints = trustedEndpoints.Split(';', ',')
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrEmpty(e));

                foreach (var endpoint in endpoints)
                {
                    _trustedEndpoints.Add(endpoint);
                    log.Info($"Added trusted endpoint: {endpoint}");
                }
            }

            // Add default Peppol endpoints
            _trustedEndpoints.Add("smp-test.peppol.org");
            _trustedEndpoints.Add("smp.peppol.org"); 
            _trustedEndpoints.Add("acc.edelivery.tech.ec.europa.eu");
            _trustedEndpoints.Add("edelivery.tech.ec.europa.eu");
        }

        /// <summary>
        /// Checks if the URI is a Peppol endpoint
        /// </summary>
        private bool IsPeppolEndpoint(Uri uri)
        {
            if (uri == null) return false;
            
            var host = uri.Host.ToLowerInvariant();
            return host.Contains("peppol") || 
                   host.Contains("edelivery") || 
                   _trustedEndpoints.Contains(host);
        }

        /// <summary>
        /// Checks if the URI is a test endpoint
        /// </summary>
        private bool IsTestEndpoint(Uri uri)
        {
            if (uri == null) return false;
            
            var host = uri.Host.ToLowerInvariant();
            return host.Contains("test") || 
                   host.Contains("acc") || 
                   host.Contains("staging") ||
                   host.Contains("dev");
        }

        /// <summary>
        /// Checks if the URI is a local or test endpoint
        /// </summary>
        private bool IsLocalOrTestEndpoint(Uri uri)
        {
            if (uri == null) return false;
            
            var host = uri.Host.ToLowerInvariant();
            return host == "localhost" || 
                   host == "127.0.0.1" || 
                   host.StartsWith("192.168.") ||
                   host.StartsWith("10.") ||
                   IsTestEndpoint(uri);
        }
    }
} 