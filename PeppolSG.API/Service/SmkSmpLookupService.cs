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

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Enhanced SMP Lookup Service for Peppol participant discovery.
    /// Implements BUSDOX SMP v1.0 and Peppol SMP Profile v1.1.0 specifications.
    /// Includes caching and robust error handling for testbed compliance.
    /// </summary>
    public class SmkSmpLookupService : ISmkSmpLookupService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SmkSmpLookupService));
        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly ObjectCache cache = MemoryCache.Default;

        private readonly IPeppolConfigurationService _configService;
        private readonly ICertificateManager _certificateManager;

        public SmkSmpLookupService(IPeppolConfigurationService configService, ICertificateManager certificateManager)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _certificateManager = certificateManager ?? throw new ArgumentNullException(nameof(certificateManager));

            // Configure HttpClient for SMP lookups
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PeppolSG-AccessPoint/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30); // 30-second timeout
        }

        /// <summary>
        /// Main method to look up endpoint metadata for a Peppol participant.
        /// Caches results for performance.
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

            log.Info($"Performing live SMP lookup for {participantId}");

            // Perform SMP lookup
            var smpUrl = BuildSmpUrl(participantScheme, participantId, documentTypeId);
            var serviceMetadata = await GetServiceMetadata(smpUrl);

            if (serviceMetadata == null || serviceMetadata.ServiceInformation == null)
            {
                throw new InvalidOperationException($"No valid ServiceInformation found at SMP URL: {smpUrl}");
            }

            // Find the correct endpoint for the given process
            var endpoint = FindPeppolAs4Endpoint(serviceMetadata, processId);

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
        private string BuildSmpUrl(string participantScheme, string participantId, string documentTypeId)
        {
            // 1. Create MD5 hash of the participant identifier
            var fullIdentifier = $"{participantScheme}::{participantId}";
            var hash = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(fullIdentifier.ToLower()));
            var hashedIdentifier = BitConverter.ToString(hash).Replace("-", "").ToLower();

            // 2. Construct the URL
            var smpDomain = _configService.SmpDomain;
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            var url = $"https://{smpDomain}/{hashedIdentifier}/services/{encodedDocType}";
            
            log.Debug($"Constructed SMP URL: {url}");
            return url;
        }

        /// <summary>
        /// Retrieves and deserializes ServiceMetadata from the SMP server.
        /// </summary>
        private async Task<SmpServiceMetadata> GetServiceMetadata(string smpUrl)
        {
            try
            {
                var response = await httpClient.GetAsync(smpUrl);
                response.EnsureSuccessStatusCode();

                var xmlContent = await response.Content.ReadAsStringAsync();
                
                // Deserialize XML into SmpServiceMetadata object
                var serializer = new XmlSerializer(typeof(SmpServiceMetadata));
                using (var reader = new StringReader(xmlContent))
                {
                    return (SmpServiceMetadata)serializer.Deserialize(reader);
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