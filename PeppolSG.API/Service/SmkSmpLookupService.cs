using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Serialization;
using log4net;
using PeppolSG.API.Models;
using System.Runtime.Caching;
using PeppolSG.API.Service.Interfaces;
using DnsClient;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Enhanced SMP Lookup Service for Peppol participant discovery.
    /// Implements BUSDOX SMP v1.0 and Peppol SMP Profile v1.1.0 specifications.
    /// Includes caching and robust error handling for testbed compliance.
    /// </summary>
    public class SmkSmpLookupService : ISmkSmpLookupService, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SmkSmpLookupService));
        private readonly HttpClient _httpClient;
        private static readonly ObjectCache cache = MemoryCache.Default;

        private readonly IPeppolConfigurationService _configService;
        private readonly ICertificateManager _certificateManager;

        public SmkSmpLookupService(IPeppolConfigurationService configService, ICertificateManager certificateManager)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _certificateManager = certificateManager ?? throw new ArgumentNullException(nameof(certificateManager));

            // Initialize HttpClient with proper configuration
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PeppolSG-AccessPoint/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // 30-second timeout
        }
        public string BuildEdnSmpServiceMetadataUrl(string participantScheme, string participantId, string documentTypeId)
        {
            // Compose: scheme::id
            var fullPid = $"{participantScheme}::{participantId}";
            var encodedPid = HttpUtility.UrlEncode(fullPid);
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            var url = $"http://smp-test.peppol.org/{encodedPid}/services/{encodedDocType}";
            //log.Info($"Constructed Peppol EDN SMP ServiceMetadata URL: {url}");
            return url;
        }
        public string BuildPeppolSmpServiceMetadataUrl(
    string participantScheme, string participantId, string documentTypeId,
    bool useProduction = false)
        {
            // Production SML zone: sml.peppolcentral.org or sml.peppol.eu
            // Test SML: acc.edelivery.tech.ec.europa.eu
            string smlZone = useProduction
                ? "smp.peppolcentral.org"
                : "acc.edelivery.tech.ec.europa.eu";
            var bdns = ToPeppolSmlBdns(participantScheme, participantId);

            // Document type must be URL encoded
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);

            // Peppol SMP format:
            //   https://B-<hash>.<SML_ZONE>/services/<doctype>
            return $"https://{bdns}.{smlZone}/services/{encodedDocType}";
        }
        /// <summary>
        /// CORRECTED: Proper SHA-256 hashing for SML BDNS generation
        /// </summary>
        public static string ToPeppolSmlBdns(string participantScheme, string participantId)
        {
            // 1. Concatenate as per Peppol: e.g. "0088:123456789"
            var fullId = $"{participantScheme}:{participantId}";
            // 2. Remove spaces, lowercase, etc
            fullId = fullId.Replace(" ", "").ToLowerInvariant();
            // 3. CORRECTED: SHA-256 hash (was SHA-1)
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(fullId);
                var hash = sha256.ComputeHash(bytes);
                // 4. Convert to hex, lowercase (Peppol spec requires lowercase)
                var hashHex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return $"B-{hashHex}";
            }
        }
        /// <summary>
        /// NEW: Performs SML DNS lookup to discover the correct SMP hostname
        /// </summary>
        private async Task<string> ResolveSmpHostnameViaSmlAsync(string participantId, string participantScheme)
        {
            var smlZone = _configService.UsePeppolTestNetwork 
                ? "acc.edelivery.tech.ec.europa.eu" 
                : "sml.peppol.eu";

            var bdnsName = ToPeppolSmlBdns(participantScheme, participantId);
            var smlDomain = $"{bdnsName}.{smlZone}";
            
            log.Info($"Querying SML DNS for: {smlDomain}");

            try
            {
                var lookup = new LookupClient();
                var naptrResult = await lookup.QueryAsync(smlDomain, QueryType.NAPTR);

                var naptrRecord = naptrResult.Answers.NaptrRecords().FirstOrDefault();
                if (naptrRecord == null)
                {
                    throw new InvalidOperationException($"SML lookup failed: No NAPTR record found for {smlDomain}. The participant may not be registered.");
                }

                // The replacement field contains the hostname of the SMP
                var smpHostname = naptrRecord.Replacement.Value.TrimEnd('.');
                log.Info($"SML lookup successful. Resolved SMP hostname: {smpHostname}");
                return smpHostname;
            }
            catch (Exception ex)
            {
                log.Error($"SML DNS lookup failed for {smlDomain}: {ex.Message}", ex);
                throw new InvalidOperationException($"SML DNS lookup failed for participant {participantId}: {ex.Message}", ex);
            }
        }
        /// <summary>
        /// Main method to look up endpoint metadata for a Peppol participant.
        /// ENHANCED: Now uses proper SML DNS lookup instead of hardcoded SMP URL.
        /// </summary>
        public async Task<SmpEndpoint> LookupEndpointMetadata(string participantId, string participantScheme, string documentTypeId, string processId)
        {
            if (string.IsNullOrEmpty(participantId)) throw new ArgumentNullException(nameof(participantId));
            if (string.IsNullOrEmpty(documentTypeId)) throw new ArgumentNullException(nameof(documentTypeId));

            var cacheKey = $"smp::{participantId}::{documentTypeId}::{processId}";
            var cachedEndpoint = cache[cacheKey] as SmpEndpoint;

            if (cachedEndpoint != null)
            {
                log.Info($"SMP metadata found in cache for key: {cacheKey}");
                return cachedEndpoint;
            }

            log.Info($"Performing live SML/SMP lookup for {participantId}");

            // ENHANCEMENT: Discover the SMP hostname via SML DNS lookup
            var smpHostname = await ResolveSmpHostnameViaSmlAsync(participantId, participantScheme);

            // Construct the final ServiceMetadata URL for the discovered SMP
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            var encodedParticipant = HttpUtility.UrlEncode($"{participantScheme}::{participantId}");
            var smpUrl = $"https://{smpHostname}/{encodedParticipant}/services/{encodedDocType}";
            
            log.Info($"Querying discovered SMP at URL: {smpUrl}");

            var signedServiceMetadata = await GetSignedServiceMetadata(smpUrl);

            if (signedServiceMetadata == null || signedServiceMetadata.ServiceMetadata == null || signedServiceMetadata.ServiceMetadata.ServiceInformation == null)
            {
                throw new InvalidOperationException($"No valid ServiceInformation found at SMP URL: {smpUrl}");
            }

            // Find the correct endpoint for the given process
            var endpoint = FindPeppolAs4Endpoint(signedServiceMetadata.ServiceMetadata, processId);

            if (endpoint == null)
            {
                throw new InvalidOperationException($"No Peppol AS4 endpoint found for process '{processId}'");
            }

            // Validate the certificate from the endpoint
            var certificate = new X509Certificate2(Convert.FromBase64String(endpoint.Certificate));
            if (!_certificateManager.ValidateCertificate(certificate))
            {
                throw new CryptographicException("Certificate from SMP endpoint failed validation");
            }

            // Cache the result
            var cachePolicy = new CacheItemPolicy { AbsoluteExpiration = DateTimeOffset.Now.AddHours(24) };
            cache.Set(cacheKey, endpoint, cachePolicy);

            log.Info($"SMP lookup successful for {participantId} - endpoint cached.");
            return endpoint;
        }

        /// <summary>
        /// Constructs the SMP URL based on the Peppol SMP specification.
        /// </summary>
        private string BuildSmpUrl(string participantScheme, string participantId, string documentTypeId, string processId)
        {
            // 1. Create MD5 hash of the participant identifier
            var fullIdentifier = $"{participantScheme}::{participantId}";
            var hash = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(fullIdentifier.ToLower()));
            var hashedIdentifier = BitConverter.ToString(hash).Replace("-", "").ToLower();

            // 2. Construct the URL
            var smpDomain = _configService.SmpDomain;
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            var encodedProcess = HttpUtility.UrlEncode(processId);
            var url = $"https://{smpDomain}/{hashedIdentifier}/services/{encodedDocType}/{encodedProcess}";
            
            log.Debug($"Constructed SMP URL: {url}");
            return url;
        }

        /// <summary>
        /// Retrieves and deserializes ServiceMetadata from the SMP server.
        /// </summary>
        private async Task<SmpSignedServiceMetadata> GetSignedServiceMetadata(string smpUrl)
        {
            try
            {
                var response = await _httpClient.GetAsync(smpUrl);
                response.EnsureSuccessStatusCode();

                var xmlContent = await response.Content.ReadAsStringAsync();
                
                // Deserialize XML into SmpServiceMetadata object
                var serializer = new XmlSerializer(typeof(SmpSignedServiceMetadata));
                using (var reader = new StringReader(xmlContent))
                {
                    return (SmpSignedServiceMetadata)serializer.Deserialize(reader);
                }
            }
            catch (HttpRequestException ex)
            {
                log.Error($"HTTP request to SMP server failed: {ex.Message}", ex);
                throw new InvalidOperationException($"SMP lookup failed for URL '{smpUrl}'", ex);
            }
            catch (Exception ex)
            {
                log.Error($"Error processing SMP response: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// Finds the Peppol AS4 endpoint from the ServiceMetadata based on the process ID.
        /// </summary>
        private SmpEndpoint FindPeppolAs4Endpoint(SmpServiceMetadata metadata, string processId)
        {
            foreach (var process in metadata.ServiceInformation.ProcessList)
            {
                if (process.ProcessIdentifier.Value.Equals(processId, StringComparison.OrdinalIgnoreCase))
                {
                    // Find AS4 endpoint (v1 or v2)
                    var as4Endpoint = process.ServiceEndpointList.Endpoints
                        .FirstOrDefault(e => e.TransportProfile.Contains("peppol-transport-as4"));

                    if (as4Endpoint != null)
                    {
                        return as4Endpoint;
                    }
                }
            }
            return null;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Legacy model for backward compatibility with As4Controller.
    /// To be replaced with SmpEndpoint model in future refactoring.
    /// </summary>
    public class PeppolEndpointMetadata
    {
        public string EndpointUrl { get; set; }
        public string Certificate { get; set; }
    }
}