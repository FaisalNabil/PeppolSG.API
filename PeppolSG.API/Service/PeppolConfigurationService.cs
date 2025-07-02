using System;
using System.Configuration;
using System.Security.Cryptography.X509Certificates;
using log4net;
using PeppolSG.API.Service.Interfaces;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Handles Peppol-specific configuration and P-Mode parameters
    /// According to eDelivery AS4 Profile specifications
    /// </summary>
    public class PeppolConfigurationService : IPeppolConfigurationService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(PeppolConfigurationService));

        #region Certificate Configuration

        public string PeppolP12FilePath => ConfigurationManager.AppSettings["PeppolP12FilePath"];
        public string PeppolP12Password => ConfigurationManager.AppSettings["PeppolP12Password"];
        public string PeppolCertificateThumbprint => ConfigurationManager.AppSettings["PeppolCertificateThumbprint"];

        #endregion

        #region Peppol Network Configuration

        public string PeppolDomain => ConfigurationManager.AppSettings["PeppolDomain"];
        public string PeppolAccessPointId => ConfigurationManager.AppSettings["PeppolAccessPointId"];
        public string PeppolPartyType => ConfigurationManager.AppSettings["PeppolPartyType"] ?? "urn:fdc:peppol.eu:2017:identifiers:ap";

        #endregion

        #region SMP/SML Configuration

        public string SmpDomain => ConfigurationManager.AppSettings["SmpDomain"] ?? "smp-test.peppol.org";
        public string SmlDomain => ConfigurationManager.AppSettings["SmlDomain"] ?? "smk.test.peppol.org";
        public bool UsePeppolTestNetwork => bool.Parse(ConfigurationManager.AppSettings["UsePeppolTestNetwork"] ?? "true");

        #endregion

        #region AS4 P-Mode Parameters (eDelivery AS4 Profile Compliant)

        // MEP Configuration
        public string OneWayMEP => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/oneWay";
        public string TwoWayMEP => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/twoWay";
        public string PushMEPBinding => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/push";
        public string PushAndPushMEPBinding => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/pushAndPush";

        // Peppol Agreement
        public string PeppolAgreement => "urn:fdc:peppol.eu:2017:agreements:tia:ap_provider";

        // Roles
        public string InitiatorRole => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/initiator";
        public string ResponderRole => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/responder";

        // Default MPC
        public string DefaultMPC => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/defaultMPC";

        // Test Service
        public string TestService => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/service";
        public string TestAction => "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/test";

        #endregion

        #region Security Configuration (eDelivery AS4 Profile Compliant)

        // WS-Security Version
        public string WSSVersion => "1.1.1";

        // Signature Configuration
        public string SignatureAlgorithm => ConfigurationManager.AppSettings["SignatureAlgorithm"] ?? "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        public string HashFunction => ConfigurationManager.AppSettings["HashFunction"] ?? "http://www.w3.org/2001/04/xmlenc#sha256";

        // Encryption Configuration  
        public string EncryptionAlgorithm => ConfigurationManager.AppSettings["EncryptionAlgorithm"] ?? "http://www.w3.org/2009/xmlenc11#aes128-gcm";
        public string KeyTransportAlgorithm => ConfigurationManager.AppSettings["KeyTransportAlgorithm"] ?? "http://www.w3.org/2009/xmlenc11#rsa-oaep";
        public string MGFAlgorithm => "http://www.w3.org/2009/xmlenc11#mgf1sha256";

        // Security flags
        public bool EnableSignature => bool.Parse(ConfigurationManager.AppSettings["EnableSignature"] ?? "true");
        public bool EnableEncryption => bool.Parse(ConfigurationManager.AppSettings["EnableEncryption"] ?? "true");

        #endregion

        #region Reception Awareness Configuration

        public bool ReceptionAwareness => bool.Parse(ConfigurationManager.AppSettings["ReceptionAwareness"] ?? "true");
        public bool RetryEnabled => bool.Parse(ConfigurationManager.AppSettings["RetryEnabled"] ?? "true");
        public int RetryCount => int.Parse(ConfigurationManager.AppSettings["RetryCount"] ?? "3");
        public int RetryInterval => int.Parse(ConfigurationManager.AppSettings["RetryInterval"] ?? "5000");
        public bool DuplicateDetection => bool.Parse(ConfigurationManager.AppSettings["DuplicateDetection"] ?? "true");
        public int DuplicateDetectionPeriod => int.Parse(ConfigurationManager.AppSettings["DuplicateDetectionPeriod"] ?? "86400");

        #endregion

        #region Error Handling Configuration

        public bool SendReceiptSynchronously => bool.Parse(ConfigurationManager.AppSettings["SendReceiptSynchronously"] ?? "true");
        public bool SendErrorSynchronously => bool.Parse(ConfigurationManager.AppSettings["SendErrorSynchronously"] ?? "true");
        public bool NonRepudiationReceipts => bool.Parse(ConfigurationManager.AppSettings["NonRepudiationReceipts"] ?? "true");

        #endregion

        #region Compression Configuration

        public bool EnableCompression => bool.Parse(ConfigurationManager.AppSettings["EnableCompression"] ?? "true");
        public string CompressionType => "application/gzip";

        #endregion

        #region Storage Configuration

        public string MessageStorePath => ConfigurationManager.AppSettings["MessageStorePath"] ?? "~/App_Data/messages";
        public string MetadataStorePath => ConfigurationManager.AppSettings["MetadataStorePath"] ?? "~/App_Data/metadata";
        public string LogStorePath => ConfigurationManager.AppSettings["LogStorePath"] ?? "~/App_Data/logs";

        #endregion

        /// <summary>
        /// Loads and validates the Peppol certificate
        /// </summary>
        public X509Certificate2 LoadPeppolCertificate()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(PeppolP12FilePath) || string.IsNullOrWhiteSpace(PeppolP12Password))
                {
                    throw new ConfigurationErrorsException("Peppol certificate configuration is missing");
                }

                var certPath = System.Web.HttpContext.Current?.Server.MapPath(PeppolP12FilePath) ?? PeppolP12FilePath;
                
                if (!System.IO.File.Exists(certPath))
                {
                    throw new ConfigurationErrorsException($"Peppol certificate file not found: {certPath}");
                }

                var certificate = new X509Certificate2(certPath, PeppolP12Password, 
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

                // Validate certificate
                ValidatePeppolCertificate(certificate);

                log.Info($"Peppol certificate loaded successfully. Subject: {certificate.Subject}");
                return certificate;
            }
            catch (Exception ex)
            {
                log.Error("Failed to load Peppol certificate", ex);
                throw;
            }
        }

        /// <summary>
        /// Validates that the certificate meets Peppol requirements
        /// </summary>
        private void ValidatePeppolCertificate(X509Certificate2 certificate)
        {
            if (certificate == null)
                throw new ArgumentNullException(nameof(certificate));

            // Check if certificate has private key
            if (!certificate.HasPrivateKey)
                throw new InvalidOperationException("Peppol certificate must have a private key");

            // Check certificate validity
            if (DateTime.Now < certificate.NotBefore || DateTime.Now > certificate.NotAfter)
                throw new InvalidOperationException($"Peppol certificate is not valid. Valid from {certificate.NotBefore} to {certificate.NotAfter}");

            // Check if it's a Peppol certificate (basic validation)
            var subject = certificate.Subject;
            if (!subject.Contains("CN="))
                throw new InvalidOperationException("Invalid Peppol certificate: Missing Common Name");

            log.Debug($"Certificate validation passed for: {subject}");
        }

        /// <summary>
        /// Validates the complete Peppol configuration
        /// </summary>
        public void ValidateConfiguration()
        {
            var errors = new System.Collections.Generic.List<string>();

            // Validate required settings
            if (string.IsNullOrWhiteSpace(PeppolDomain))
                errors.Add("PeppolDomain is required");

            if (string.IsNullOrWhiteSpace(PeppolAccessPointId))
                errors.Add("PeppolAccessPointId is required");

            if (string.IsNullOrWhiteSpace(SmpDomain))
                errors.Add("SmpDomain is required");

            // Validate certificate configuration
            try
            {
                LoadPeppolCertificate();
            }
            catch (Exception ex)
            {
                errors.Add($"Certificate configuration error: {ex.Message}");
            }

            if (errors.Count > 0)
            {
                var errorMessage = "Peppol configuration validation failed:\n" + string.Join("\n", errors);
                log.Error(errorMessage);
                throw new ConfigurationErrorsException(errorMessage);
            }

            log.Info("Peppol configuration validation completed successfully");
        }

        /// <summary>
        /// Gets the P-Mode configuration for a specific message exchange
        /// </summary>
        public PeppolPModeConfiguration GetPModeConfiguration(string service, string action, string fromParty, string toParty)
        {
            return new PeppolPModeConfiguration
            {
                // Basic P-Mode info
                Service = service,
                Action = action,
                FromParty = fromParty,
                ToParty = toParty,
                
                // MEP Configuration
                MEP = OneWayMEP,
                MEPBinding = PushMEPBinding,
                
                // Roles
                InitiatorRole = this.InitiatorRole,
                ResponderRole = this.ResponderRole,
                
                // Agreement
                Agreement = PeppolAgreement,
                
                // MPC
                MPC = DefaultMPC,
                
                // Security
                WSSVersion = this.WSSVersion,
                EnableSignature = this.EnableSignature,
                SignatureAlgorithm = this.SignatureAlgorithm,
                HashFunction = this.HashFunction,
                EnableEncryption = this.EnableEncryption,
                EncryptionAlgorithm = this.EncryptionAlgorithm,
                KeyTransportAlgorithm = this.KeyTransportAlgorithm,
                
                // Reception Awareness
                ReceptionAwareness = this.ReceptionAwareness,
                RetryEnabled = this.RetryEnabled,
                RetryCount = this.RetryCount,
                RetryInterval = this.RetryInterval,
                DuplicateDetection = this.DuplicateDetection,
                DuplicateDetectionPeriod = this.DuplicateDetectionPeriod,
                
                // Error Handling
                SendReceiptSynchronously = this.SendReceiptSynchronously,
                SendErrorSynchronously = this.SendErrorSynchronously,
                NonRepudiationReceipts = this.NonRepudiationReceipts,
                
                // Compression
                EnableCompression = this.EnableCompression,
                CompressionType = this.CompressionType
            };
        }
    }

    /// <summary>
    /// Represents a complete P-Mode configuration for Peppol AS4
    /// </summary>
    public class PeppolPModeConfiguration
    {
        public string Service { get; set; }
        public string Action { get; set; }
        public string FromParty { get; set; }
        public string ToParty { get; set; }
        public string MEP { get; set; }
        public string MEPBinding { get; set; }
        public string InitiatorRole { get; set; }
        public string ResponderRole { get; set; }
        public string Agreement { get; set; }
        public string MPC { get; set; }
        public string WSSVersion { get; set; }
        public bool EnableSignature { get; set; }
        public string SignatureAlgorithm { get; set; }
        public string HashFunction { get; set; }
        public bool EnableEncryption { get; set; }
        public string EncryptionAlgorithm { get; set; }
        public string KeyTransportAlgorithm { get; set; }
        public bool ReceptionAwareness { get; set; }
        public bool RetryEnabled { get; set; }
        public int RetryCount { get; set; }
        public int RetryInterval { get; set; }
        public bool DuplicateDetection { get; set; }
        public int DuplicateDetectionPeriod { get; set; }
        public bool SendReceiptSynchronously { get; set; }
        public bool SendErrorSynchronously { get; set; }
        public bool NonRepudiationReceipts { get; set; }
        public bool EnableCompression { get; set; }
        public string CompressionType { get; set; }
    }
} 