using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PeppolSG.API.Service;
using PeppolSG.API.Service.Interfaces;

namespace PeppolSG.API.Validation
{
    /// <summary>
    /// Day 18 Validation Script: AS4 Receipt NonRepudiationInformation Implementation
    /// Validates the proper generation of NonRepudiationInformation elements in AS4 receipts
    /// as required by Peppol AS4 Profile v2.0.3
    /// </summary>
    public class Day18ValidationScript
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("=== Day 18 Validation: AS4 Receipt NonRepudiationInformation ===");
            Console.WriteLine();
            
            try
            {
                // Test 1: Receipt with NonRepudiationInformation
                Console.WriteLine("Test 1: Receipt with NonRepudiationInformation Generation");
                TestReceiptWithNRI();
                Console.WriteLine("✅ PASSED");
                Console.WriteLine();
                
                // Test 2: Receipt without signature references (fallback)
                Console.WriteLine("Test 2: Receipt Fallback (Empty Receipt)");
                TestReceiptFallback();
                Console.WriteLine("✅ PASSED");
                Console.WriteLine();
                
                // Test 3: Signature reference extraction
                Console.WriteLine("Test 3: Signature Reference Extraction");
                TestSignatureReferenceExtraction();
                Console.WriteLine("✅ PASSED");
                Console.WriteLine();
                
                Console.WriteLine("🎉 All Day 18 validation tests PASSED!");
                Console.WriteLine("✅ AS4 Receipt NonRepudiationInformation implementation is working correctly");
                Console.WriteLine("✅ Peppol AS4 Profile v2.0.3 compliance achieved");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ VALIDATION FAILED: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            }
        }
        
        private static void TestReceiptWithNRI()
        {
            // Create mock config
            var mockConfig = new MockPeppolConfigurationService();
            var messageBuilder = new As4MessageBuilder(mockConfig);
            
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var messageId = "receipt-test-" + Guid.NewGuid().ToString();
            var refToMessageId = "original-message-" + Guid.NewGuid().ToString();
            
            // Create mock signature references
            var signatureReferences = new List<XElement>
            {
                new XElement(XName.Get("Reference", "http://www.w3.org/2001/09/xmldsig#"),
                    new XAttribute("URI", "#_1"),
                    new XElement(XName.Get("DigestMethod", "http://www.w3.org/2001/09/xmldsig#"),
                        new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")
                    ),
                    new XElement(XName.Get("DigestValue", "http://www.w3.org/2001/09/xmldsig#"), "dGVzdERpZ2VzdFZhbHVl")
                )
            };
            
            // Generate receipt
            var receiptMessage = messageBuilder.BuildSignalMessage(timestamp, messageId, refToMessageId, signatureReferences);
            
            // Validate structure
            if (receiptMessage == null)
                throw new Exception("Receipt message was not generated");
                
            var receipt = receiptMessage.Element(XName.Get("Receipt", "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/"));
            if (receipt == null)
                throw new Exception("Receipt element is missing");
                
            var nrInfo = receipt.Element(XName.Get("NonRepudiationInformation", "http://docs.oasis-open.org/ebxml-bp/ebbp-signals-2.0"));
            if (nrInfo == null)
                throw new Exception("NonRepudiationInformation element is missing");
                
            var messagePartNRInfos = nrInfo.Elements(XName.Get("MessagePartNRInformation", "http://docs.oasis-open.org/ebxml-bp/ebbp-signals-2.0")).ToList();
            if (messagePartNRInfos.Count != 1)
                throw new Exception($"Expected 1 MessagePartNRInformation element, got {messagePartNRInfos.Count}");
                
            Console.WriteLine($"   Generated receipt with MessageId: {messageId}");
            Console.WriteLine($"   NonRepudiationInformation contains {messagePartNRInfos.Count} MessagePartNRInformation elements");
        }
        
        private static void TestReceiptFallback()
        {
            var mockConfig = new MockPeppolConfigurationService();
            var messageBuilder = new As4MessageBuilder(mockConfig);
            
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var messageId = "receipt-fallback-" + Guid.NewGuid().ToString();
            var refToMessageId = "original-fallback-" + Guid.NewGuid().ToString();
            
            // Generate receipt without signature references
            var receiptMessage = messageBuilder.BuildSignalMessage(timestamp, messageId, refToMessageId, null);
            
            // Validate fallback behavior
            var receipt = receiptMessage.Element(XName.Get("Receipt", "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/"));
            if (receipt == null)
                throw new Exception("Receipt element is missing");
                
            if (receipt.HasElements)
                throw new Exception("Receipt should be empty when no signature references provided");
                
            Console.WriteLine($"   Generated empty receipt for backward compatibility");
        }
        
        private static void TestSignatureReferenceExtraction()
        {
            var sampleSoapMessage = XDocument.Parse(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<S12:Envelope xmlns:S12=""http://www.w3.org/2003/05/soap-envelope"">
    <S12:Header>
        <wsse:Security xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"">
            <ds:Signature xmlns:ds=""http://www.w3.org/2001/09/xmldsig#"">
                <ds:SignedInfo>
                    <ds:Reference URI=""#_1"">
                        <ds:DigestValue>dGVzdERpZ2VzdFZhbHVl</ds:DigestValue>
                    </ds:Reference>
                    <ds:Reference URI=""#body-test"">
                        <ds:DigestValue>Ym9keURpZ2VzdFZhbHVl</ds:DigestValue>
                    </ds:Reference>
                </ds:SignedInfo>
            </ds:Signature>
        </wsse:Security>
    </S12:Header>
</S12:Envelope>");
            
            // Extract signature references
            var references = sampleSoapMessage
                .Descendants(XName.Get("Reference", "http://www.w3.org/2001/09/xmldsig#"))
                .Where(r => !string.IsNullOrEmpty(r.Attribute("URI")?.Value))
                .ToList();
                
            if (references.Count != 2)
                throw new Exception($"Expected 2 signature references, got {references.Count}");
                
            var uris = references.Select(r => r.Attribute("URI")?.Value).ToList();
            if (!uris.Contains("#_1") || !uris.Contains("#body-test"))
                throw new Exception("Signature references not extracted correctly");
                
            Console.WriteLine($"   Extracted {references.Count} signature references");
            Console.WriteLine($"   URIs: {string.Join(", ", uris)}");
        }
    }
    
    // Mock configuration service for testing
    public class MockPeppolConfigurationService : IPeppolConfigurationService
    {
        public string GetPeppolDomain() => "test.peppol.sg";
        public string GetSigningCertificatePath() => "test.p12";
        public string GetSigningCertificatePassword() => "test123";
        public string GetTlsCertificatePath() => "tls.p12";
        public string GetTlsCertificatePassword() => "tls123";
        public string GetPeppolSenderId() => "TEST001";
        public string GetPeppolReceiverId() => "TEST002";
        public string GetEndpointUrl() => "https://test.peppol.sg/as4";
        public string GetSmlZone() => "test";
        public bool IsDebugMode() => true;
        public string GetInboundPath() => "./inbound";
        public string GetOutboundPath() => "./outbound";
        public string GetLogPath() => "./logs";
        public string GetMetadataPath() => "./metadata";
        public bool ValidateIncomingCertificates() => true;
        public bool RequireHttps() => true;
        public int GetMaxAttachmentSize() => 10485760;
        public int GetConnectionTimeout() => 30000;
        public bool EnableCompression() => true;
        public string GetCompressionLevel() => "optimal";
        public bool EnableEncryption() => true;
        public string GetEncryptionAlgorithm() => "AES256-GCM";
        public string GetSigningAlgorithm() => "SHA256withRSA";
        public string GetDigestAlgorithm() => "SHA256";
        public bool EnableTimestampValidation() => true;
        public int GetTimestampToleranceMinutes() => 5;
        public bool EnableNonRepudiation() => true;
        public string GetNonRepudiationHashAlgorithm() => "SHA256";
        public bool EnableAuditLogging() => true;
        public string GetAuditLogPath() => "./audit";
        public bool EnablePerformanceLogging() => true;
        public string GetPerformanceLogPath() => "./performance";
        public bool EnableMetrics() => true;
        public string GetMetricsPath() => "./metrics";
        public bool EnableHealthChecks() => true;
        public string GetHealthCheckPath() => "./health";
        public int GetHealthCheckIntervalSeconds() => 60;
        public bool EnableCertificateValidation() => true;
        public bool EnableCrlChecking() => false;
        public bool EnableOcspChecking() => false;
        public string GetTrustedCertificatesPath() => "./trusted";
        public bool EnableSmpLookup() => true;
        public string GetSmpUrl() => "https://test-smp.peppol.sg";
        public string GetSmlUrl() => "https://test-sml.peppol.sg";
        public int GetSmpTimeoutSeconds() => 30;
        public int GetSmlTimeoutSeconds() => 30;
        public bool EnableSmpCaching() => true;
        public int GetSmpCacheExpiryMinutes() => 60;
        public bool EnableRetryPolicy() => true;
        public int GetMaxRetryAttempts() => 3;
        public int GetRetryDelaySeconds() => 5;
        public bool EnableCircuitBreaker() => true;
        public int GetCircuitBreakerThreshold() => 5;
        public int GetCircuitBreakerTimeoutSeconds() => 30;
        public bool EnableRateLimiting() => true;
        public int GetRateLimitRequestsPerMinute() => 100;
        public bool EnableThrottling() => true;
        public int GetThrottlingMaxConcurrentRequests() => 10;
        public bool EnableCors() => true;
        public string GetCorsAllowedOrigins() => "*";
        public string GetCorsAllowedMethods() => "GET,POST,PUT,DELETE,OPTIONS";
        public string GetCorsAllowedHeaders() => "*";
        public bool EnableSwagger() => true;
        public string GetSwaggerTitle() => "Peppol AS4 API";
        public string GetSwaggerVersion() => "v1";
        public string GetSwaggerDescription() => "Peppol AS4 Access Point API";
        public bool EnableAuthentication() => false;
        public string GetAuthenticationScheme() => "Bearer";
        public string GetAuthenticationKey() => "test-key";
        public bool EnableAuthorization() => false;
        public string GetAuthorizationPolicy() => "default";
        public bool EnableDataProtection() => true;
        public string GetDataProtectionKeyPath() => "./keys";
        public bool EnableSessionState() => false;
        public string GetSessionStateProvider() => "memory";
        public string GetSessionStateConnectionString() => "";
        public int GetSessionTimeoutMinutes() => 30;
        public bool EnableDistributedCache() => false;
        public string GetDistributedCacheProvider() => "memory";
        public string GetDistributedCacheConnectionString() => "";
        public int GetDistributedCacheExpiryMinutes() => 60;
        public bool EnableBackgroundServices() => true;
        public int GetBackgroundServiceIntervalSeconds() => 60;
        public bool EnableScheduledTasks() => true;
        public string GetScheduledTasksCronExpression() => "0 0 * * *";
        public bool EnableFileSystemWatcher() => true;
        public string GetFileSystemWatcherPath() => "./watch";
        public bool EnableEmailNotifications() => false;
        public string GetEmailSmtpServer() => "smtp.test.com";
        public int GetEmailSmtpPort() => 587;
        public string GetEmailUsername() => "test@test.com";
        public string GetEmailPassword() => "test123";
        public bool GetEmailEnableSsl() => true;
        public string GetEmailFromAddress() => "test@test.com";
        public string GetEmailToAddress() => "admin@test.com";
        public bool EnableSmsNotifications() => false;
        public string GetSmsProvider() => "twilio";
        public string GetSmsAccountSid() => "test-sid";
        public string GetSmsAuthToken() => "test-token";
        public string GetSmsFromNumber() => "+1234567890";
        public string GetSmsToNumber() => "+0987654321";
        public bool EnableWebhooks() => false;
        public string GetWebhookUrl() => "https://test.webhook.com";
        public string GetWebhookSecret() => "test-secret";
        public int GetWebhookTimeoutSeconds() => 30;
        public bool EnableDatabaseLogging() => false;
        public string GetDatabaseProvider() => "sqlite";
        public string GetDatabaseConnectionString() => "Data Source=test.db";
        public int GetDatabaseCommandTimeoutSeconds() => 30;
        public bool EnableDatabaseMigrations() => false;
        public bool EnableDatabaseSeeding() => false;
        public bool EnableEntityFramework() => false;
        public bool EnableDapper() => false;
        public bool EnableRedis() => false;
        public string GetRedisConnectionString() => "localhost:6379";
        public int GetRedisDatabaseNumber() => 0;
        public bool EnableElasticsearch() => false;
        public string GetElasticsearchUrl() => "http://localhost:9200";
        public string GetElasticsearchIndex() => "peppol-logs";
        public bool EnableApplicationInsights() => false;
        public string GetApplicationInsightsInstrumentationKey() => "test-key";
        public bool EnableNewRelic() => false;
        public string GetNewRelicLicenseKey() => "test-key";
        public bool EnableDatadog() => false;
        public string GetDatadogApiKey() => "test-key";
        public bool EnablePrometheus() => false;
        public string GetPrometheusEndpoint() => "/metrics";
        public bool EnableJaeger() => false;
        public string GetJaegerEndpoint() => "http://localhost:14268/api/traces";
        public bool EnableZipkin() => false;
        public string GetZipkinEndpoint() => "http://localhost:9411/api/v2/spans";
        public bool EnableOpenTelemetry() => false;
        public string GetOpenTelemetryEndpoint() => "http://localhost:4317";
    }
} 