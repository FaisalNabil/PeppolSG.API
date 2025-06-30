using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Manages certificate store operations for Peppol AS4 messaging
    /// Handles Peppol root certificates, intermediate certificates, and caching
    /// </summary>
    public class CertificateStoreManager : ICertificateStoreManager
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(CertificateStoreManager));
        
        private readonly ConcurrentDictionary<string, X509Certificate2> _peppolRootCertificates;
        private readonly ConcurrentDictionary<string, X509Certificate2> _intermediateCertificates;
        private readonly string _certificateStorePath;
        private readonly TimeSpan _cacheExpiryTime;
        private DateTime _lastCacheRefresh;

        public CertificateStoreManager()
        {
            _peppolRootCertificates = new ConcurrentDictionary<string, X509Certificate2>();
            _intermediateCertificates = new ConcurrentDictionary<string, X509Certificate2>();
            
            _certificateStorePath = ConfigurationManager.AppSettings["PeppolCertificateStorePath"] ?? 
                                   Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Certificates");
            
            _cacheExpiryTime = TimeSpan.FromHours(
                int.Parse(ConfigurationManager.AppSettings["CertificateCacheExpiryHours"] ?? "24"));
            
            InitializeCertificateStore();
        }

        /// <summary>
        /// Gets all Peppol root certificates
        /// </summary>
        public IEnumerable<X509Certificate2> GetPeppolRootCertificates()
        {
            RefreshCacheIfNeeded();
            return _peppolRootCertificates.Values.ToList();
        }

        /// <summary>
        /// Gets all intermediate certificates
        /// </summary>
        public IEnumerable<X509Certificate2> GetIntermediateCertificates()
        {
            RefreshCacheIfNeeded();
            return _intermediateCertificates.Values.ToList();
        }

        /// <summary>
        /// Adds a Peppol root certificate to the store
        /// </summary>
        public void AddPeppolRootCertificate(X509Certificate2 certificate)
        {
            if (certificate == null)
                throw new ArgumentNullException(nameof(certificate));

            var thumbprint = certificate.Thumbprint;
            _peppolRootCertificates.AddOrUpdate(thumbprint, certificate, (key, oldValue) => certificate);
            
            log.Info($"Added Peppol root certificate with thumbprint: {thumbprint}");
        }

        /// <summary>
        /// Refreshes the certificate cache from disk
        /// </summary>
        public void RefreshCertificateCache()
        {
            log.Info("Refreshing certificate cache from disk");
            
            try
            {
                _peppolRootCertificates.Clear();
                _intermediateCertificates.Clear();
                
                LoadCertificatesFromDisk();
                LoadPeppolRootCertificatesFromConfiguration();
                
                _lastCacheRefresh = DateTime.UtcNow;
                
                log.Info($"Certificate cache refreshed. Loaded {_peppolRootCertificates.Count} root certificates and {_intermediateCertificates.Count} intermediate certificates");
            }
            catch (Exception ex)
            {
                log.Error("Failed to refresh certificate cache", ex);
                throw;
            }
        }

        /// <summary>
        /// Initializes the certificate store on startup
        /// </summary>
        private void InitializeCertificateStore()
        {
            log.Info("Initializing certificate store");
            
            try
            {
                EnsureCertificateDirectoryExists();
                RefreshCertificateCache();
            }
            catch (Exception ex)
            {
                log.Error("Failed to initialize certificate store", ex);
                throw;
            }
        }

        /// <summary>
        /// Ensures the certificate directory structure exists
        /// </summary>
        private void EnsureCertificateDirectoryExists()
        {
            if (!Directory.Exists(_certificateStorePath))
            {
                Directory.CreateDirectory(_certificateStorePath);
                log.Info($"Created certificate store directory: {_certificateStorePath}");
            }

            var rootCertPath = Path.Combine(_certificateStorePath, "Root");
            var intermediatePath = Path.Combine(_certificateStorePath, "Intermediate");

            Directory.CreateDirectory(rootCertPath);
            Directory.CreateDirectory(intermediatePath);
        }

        /// <summary>
        /// Loads certificates from disk
        /// </summary>
        private void LoadCertificatesFromDisk()
        {
            // Load root certificates
            var rootCertPath = Path.Combine(_certificateStorePath, "Root");
            if (Directory.Exists(rootCertPath))
            {
                foreach (var certFile in Directory.GetFiles(rootCertPath, "*.cer").Concat(Directory.GetFiles(rootCertPath, "*.crt")))
                {
                    try
                    {
                        var cert = new X509Certificate2(certFile);
                        _peppolRootCertificates.TryAdd(cert.Thumbprint, cert);
                        log.Debug($"Loaded root certificate from {certFile}");
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to load certificate from {certFile}: {ex.Message}");
                    }
                }
            }

            // Load intermediate certificates
            var intermediatePath = Path.Combine(_certificateStorePath, "Intermediate");
            if (Directory.Exists(intermediatePath))
            {
                foreach (var certFile in Directory.GetFiles(intermediatePath, "*.cer").Concat(Directory.GetFiles(intermediatePath, "*.crt")))
                {
                    try
                    {
                        var cert = new X509Certificate2(certFile);
                        _intermediateCertificates.TryAdd(cert.Thumbprint, cert);
                        log.Debug($"Loaded intermediate certificate from {certFile}");
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to load certificate from {certFile}: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Loads Peppol root certificates from configuration
        /// </summary>
        private void LoadPeppolRootCertificatesFromConfiguration()
        {
            // Load hardcoded Peppol root certificates for different environments
            var isTestEnvironment = bool.Parse(ConfigurationManager.AppSettings["IsTestEnvironment"] ?? "false");
            
            if (isTestEnvironment)
            {
                LoadPeppolTestRootCertificates();
            }
            else
            {
                LoadPeppolProductionRootCertificates();
            }
        }

        /// <summary>
        /// Loads Peppol test environment root certificates
        /// </summary>
        private void LoadPeppolTestRootCertificates()
        {
            try
            {
                // Peppol Test PKI Root CA certificate (Base64 encoded)
                // This is a sample - replace with actual Peppol test root certificate
                var peppolTestRootCertBase64 = ConfigurationManager.AppSettings["PeppolTestRootCertificate"];
                
                if (!string.IsNullOrEmpty(peppolTestRootCertBase64))
                {
                    var certBytes = Convert.FromBase64String(peppolTestRootCertBase64);
                    var cert = new X509Certificate2(certBytes);
                    _peppolRootCertificates.TryAdd(cert.Thumbprint, cert);
                    log.Info($"Loaded Peppol test root certificate: {cert.Subject}");
                }
                else
                {
                    log.Warn("Peppol test root certificate not configured in app settings");
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed to load Peppol test root certificates", ex);
            }
        }

        /// <summary>
        /// Loads Peppol production environment root certificates
        /// </summary>
        private void LoadPeppolProductionRootCertificates()
        {
            try
            {
                // Peppol Production PKI Root CA certificate (Base64 encoded)
                // This is a sample - replace with actual Peppol production root certificate
                var peppolProdRootCertBase64 = ConfigurationManager.AppSettings["PeppolProductionRootCertificate"];
                
                if (!string.IsNullOrEmpty(peppolProdRootCertBase64))
                {
                    var certBytes = Convert.FromBase64String(peppolProdRootCertBase64);
                    var cert = new X509Certificate2(certBytes);
                    _peppolRootCertificates.TryAdd(cert.Thumbprint, cert);
                    log.Info($"Loaded Peppol production root certificate: {cert.Subject}");
                }
                else
                {
                    log.Warn("Peppol production root certificate not configured in app settings");
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed to load Peppol production root certificates", ex);
            }
        }

        /// <summary>
        /// Refreshes cache if expired
        /// </summary>
        private void RefreshCacheIfNeeded()
        {
            if (DateTime.UtcNow - _lastCacheRefresh > _cacheExpiryTime)
            {
                log.Debug("Certificate cache expired, refreshing");
                RefreshCertificateCache();
            }
        }

        /// <summary>
        /// Disposes of managed resources
        /// </summary>
        public void Dispose()
        {
            foreach (var cert in _peppolRootCertificates.Values)
            {
                cert?.Dispose();
            }
            
            foreach (var cert in _intermediateCertificates.Values)
            {
                cert?.Dispose();
            }
            
            _peppolRootCertificates.Clear();
            _intermediateCertificates.Clear();
        }
    }
} 