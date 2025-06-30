using System;
using System.Configuration;
using System.Net;
using System.Net.Security;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// TLS configuration service that enforces secure TLS protocols
    /// and configures system-wide TLS settings for Peppol AS4 messaging
    /// </summary>
    public static class TlsConfigurationService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(TlsConfigurationService));
        private static bool _isInitialized = false;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Initializes TLS configuration with secure protocols
        /// Should be called during application startup
        /// </summary>
        public static void InitializeTlsConfiguration()
        {
            lock (_lockObject)
            {
                if (_isInitialized)
                {
                    log.Debug("TLS configuration already initialized");
                    return;
                }

                try
                {
                    log.Info("Initializing TLS configuration for secure communication");

                    // 1. Configure supported TLS protocols
                    ConfigureTlsProtocols();

                    // 2. Configure certificate validation settings
                    ConfigureCertificateValidation();

                    // 3. Configure cipher suites (if supported)
                    ConfigureCipherSuites();

                    // 4. Configure additional security settings
                    ConfigureSecuritySettings();

                    _isInitialized = true;
                    log.Info("TLS configuration initialized successfully");
                }
                catch (Exception ex)
                {
                    log.Error("Failed to initialize TLS configuration", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Configures TLS protocols to use only secure versions (TLS 1.2+)
        /// </summary>
        private static void ConfigureTlsProtocols()
        {
            // Get minimum TLS version from configuration
            var minTlsVersionConfig = ConfigurationManager.AppSettings["MinimumTlsVersion"] ?? "Tls12";
            var allowTls13 = bool.Parse(ConfigurationManager.AppSettings["AllowTls13"] ?? "true");
            var isTestEnvironment = bool.Parse(ConfigurationManager.AppSettings["IsTestEnvironment"] ?? "false");

            SecurityProtocolType supportedProtocols = SecurityProtocolType.SystemDefault;

            // Parse minimum TLS version
            switch (minTlsVersionConfig.ToLowerInvariant())
            {
                case "tls12":
                    supportedProtocols = SecurityProtocolType.Tls12;
                    break;
                case "tls13":
                    // TLS 1.3 support depends on .NET version and OS
                    supportedProtocols = (SecurityProtocolType)12288; // TLS 1.3
                    break;
                default:
                    supportedProtocols = SecurityProtocolType.Tls12;
                    log.Warn($"Unknown TLS version '{minTlsVersionConfig}', defaulting to TLS 1.2");
                    break;
            }

            // Add TLS 1.3 if supported and allowed
            if (allowTls13)
            {
                try
                {
                    supportedProtocols |= (SecurityProtocolType)12288; // TLS 1.3
                    log.Debug("TLS 1.3 support enabled");
                }
                catch (Exception ex)
                {
                    log.Warn("TLS 1.3 not supported on this platform", ex);
                }
            }

            // In test environment, we might need to allow TLS 1.1 for certain test endpoints
            if (isTestEnvironment)
            {
                var allowLegacyTls = bool.Parse(ConfigurationManager.AppSettings["AllowLegacyTlsInTest"] ?? "false");
                if (allowLegacyTls)
                {
                    log.Warn("Legacy TLS protocols enabled in test environment");
                    supportedProtocols |= SecurityProtocolType.Tls11;
                }
            }

            // Set the security protocol
            ServicePointManager.SecurityProtocol = supportedProtocols;
            
            log.Info($"TLS protocols configured: {ServicePointManager.SecurityProtocol}");

            // Disable unsafe legacy protocols explicitly
            DisableLegacyProtocols();
        }

        /// <summary>
        /// Disables unsafe legacy protocols
        /// </summary>
        private static void DisableLegacyProtocols()
        {
            try
            {
                // Remove unsafe protocols from current configuration
                var currentProtocols = ServicePointManager.SecurityProtocol;
                
                // Remove SSL 3.0, TLS 1.0 if they're somehow enabled
                if ((currentProtocols & SecurityProtocolType.Ssl3) != 0)
                {
                    ServicePointManager.SecurityProtocol &= ~SecurityProtocolType.Ssl3;
                    log.Info("Disabled SSL 3.0 protocol");
                }

                if ((currentProtocols & SecurityProtocolType.Tls) != 0)
                {
                    ServicePointManager.SecurityProtocol &= ~SecurityProtocolType.Tls;
                    log.Info("Disabled TLS 1.0 protocol");
                }

                // Optionally disable TLS 1.1 in production
                var isTestEnvironment = bool.Parse(ConfigurationManager.AppSettings["IsTestEnvironment"] ?? "false");
                var allowTls11 = bool.Parse(ConfigurationManager.AppSettings["AllowTls11"] ?? isTestEnvironment.ToString());
                
                if (!allowTls11 && (currentProtocols & SecurityProtocolType.Tls11) != 0)
                {
                    ServicePointManager.SecurityProtocol &= ~SecurityProtocolType.Tls11;
                    log.Info("Disabled TLS 1.1 protocol");
                }
            }
            catch (Exception ex)
            {
                log.Warn("Error while disabling legacy protocols", ex);
            }
        }

        /// <summary>
        /// Configures certificate validation settings
        /// </summary>
        private static void ConfigureCertificateValidation()
        {
            // Set certificate validation callback to null to use default validation
            // This ensures that our custom validation in SecureSslValidationService takes precedence
            ServicePointManager.ServerCertificateValidationCallback = null;

            // Configure certificate revocation checking
            var checkCertificateRevocation = bool.Parse(ConfigurationManager.AppSettings["CheckCertificateRevocation"] ?? "true");
            ServicePointManager.CheckCertificateRevocationList = checkCertificateRevocation;

            log.Info($"Certificate revocation checking: {checkCertificateRevocation}");

            // Set connection limits for better performance
            var maxServicePointIdleTime = int.Parse(ConfigurationManager.AppSettings["MaxServicePointIdleTime"] ?? "100000");
            ServicePointManager.MaxServicePointIdleTime = maxServicePointIdleTime;

            // Set DNS refresh timeout
            var dnsRefreshTimeout = int.Parse(ConfigurationManager.AppSettings["DnsRefreshTimeout"] ?? "120000");
            ServicePointManager.DnsRefreshTimeout = dnsRefreshTimeout;

            log.Debug($"ServicePoint idle time: {maxServicePointIdleTime}ms, DNS refresh: {dnsRefreshTimeout}ms");
        }

        /// <summary>
        /// Configures cipher suites for enhanced security
        /// </summary>
        private static void ConfigureCipherSuites()
        {
            try
            {
                // Configure strong cipher suites preference
                // Note: .NET Framework cipher suite configuration is limited
                // Most cipher suite configuration needs to be done at OS level
                
                var preferStrongCiphers = bool.Parse(ConfigurationManager.AppSettings["PreferStrongCiphers"] ?? "true");
                if (preferStrongCiphers)
                {
                    log.Info("Strong cipher suites preference enabled");
                    // This is primarily informational as .NET Framework uses OS settings
                }

                // Log supported cipher suites for debugging
                LogSupportedCipherSuites();
            }
            catch (Exception ex)
            {
                log.Warn("Error configuring cipher suites", ex);
            }
        }

        /// <summary>
        /// Configures additional security settings
        /// </summary>
        private static void ConfigureSecuritySettings()
        {
            try
            {
                // Enable strong cryptography if available (.NET 4.7+)
                AppContext.SetSwitch("Switch.System.Net.DontEnableSchUseStrongCrypto", false);
                AppContext.SetSwitch("Switch.System.Net.DontEnableSystemDefaultTlsVersions", false);
                
                log.Info("Strong cryptography switches enabled");
            }
            catch (Exception ex)
            {
                log.Debug("Strong cryptography switches not available on this .NET version", ex);
            }

            // Configure expect 100 continue behavior
            var expect100Continue = bool.Parse(ConfigurationManager.AppSettings["Expect100Continue"] ?? "false");
            ServicePointManager.Expect100Continue = expect100Continue;

            // Configure Nagle algorithm
            var useNagleAlgorithm = bool.Parse(ConfigurationManager.AppSettings["UseNagleAlgorithm"] ?? "true");
            ServicePointManager.UseNagleAlgorithm = useNagleAlgorithm;

            log.Debug($"Expect100Continue: {expect100Continue}, UseNagleAlgorithm: {useNagleAlgorithm}");
        }

        /// <summary>
        /// Logs supported cipher suites for debugging purposes
        /// </summary>
        private static void LogSupportedCipherSuites()
        {
            try
            {
                // This is informational only as .NET Framework doesn't provide
                // programmatic access to cipher suite configuration
                log.Debug("Cipher suite configuration is managed by the operating system");
                log.Debug("Ensure strong cipher suites are enabled at OS level for production environments");
            }
            catch (Exception ex)
            {
                log.Debug("Unable to log cipher suite information", ex);
            }
        }

        /// <summary>
        /// Validates current TLS configuration
        /// </summary>
        public static TlsConfigurationStatus ValidateConfiguration()
        {
            var status = new TlsConfigurationStatus();

            try
            {
                // Check TLS version
                var protocols = ServicePointManager.SecurityProtocol;
                status.SupportedProtocols = protocols.ToString();
                status.IsTls12Enabled = (protocols & SecurityProtocolType.Tls12) != 0;
                status.IsTls13Enabled = (protocols & (SecurityProtocolType)12288) != 0;
                status.HasLegacyProtocols = (protocols & (SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11)) != 0;

                // Check certificate validation
                status.CertificateRevocationEnabled = ServicePointManager.CheckCertificateRevocationList;
                status.HasCustomCertificateValidation = ServicePointManager.ServerCertificateValidationCallback != null;

                // Overall security assessment
                status.IsSecure = status.IsTls12Enabled && !status.HasLegacyProtocols && status.CertificateRevocationEnabled;

                log.Info($"TLS Configuration Status: Secure={status.IsSecure}, TLS1.2={status.IsTls12Enabled}, TLS1.3={status.IsTls13Enabled}, Legacy={status.HasLegacyProtocols}");
            }
            catch (Exception ex)
            {
                log.Error("Error validating TLS configuration", ex);
                status.ValidationError = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// Gets the current TLS configuration summary
        /// </summary>
        public static string GetConfigurationSummary()
        {
            try
            {
                var status = ValidateConfiguration();
                return $"TLS Protocols: {status.SupportedProtocols}, " +
                       $"Secure: {status.IsSecure}, " +
                       $"Revocation Check: {status.CertificateRevocationEnabled}";
            }
            catch (Exception ex)
            {
                return $"Error getting TLS configuration: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// TLS configuration status information
    /// </summary>
    public class TlsConfigurationStatus
    {
        public string SupportedProtocols { get; set; }
        public bool IsTls12Enabled { get; set; }
        public bool IsTls13Enabled { get; set; }
        public bool HasLegacyProtocols { get; set; }
        public bool CertificateRevocationEnabled { get; set; }
        public bool HasCustomCertificateValidation { get; set; }
        public bool IsSecure { get; set; }
        public string ValidationError { get; set; }
    }
} 