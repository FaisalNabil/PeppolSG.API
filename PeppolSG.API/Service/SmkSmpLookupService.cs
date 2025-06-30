using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using System.Collections.Concurrent;
using PeppolSG.API.Logging;

namespace PeppolSG.API.Service
{
    public class SmkSmpLookupService
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(SmkSmpLookupService));
        private static readonly ConcurrentDictionary<string, (PeppolEndpointMetadata Meta, DateTime Expiry)> _cache = new();
        private readonly string _smpDomain;
        private readonly string _certPath;
        private readonly string _certPwd;
        private readonly SecureSslValidationService _sslValidator;
        private readonly bool _isTestEnvironment;
        private readonly List<string> _smpDomains;
        private readonly int _retryCount;
        private readonly int _initialDelayMs;
        private readonly int _timeoutSec;

        /// <param name="smpDomain">e.g. acc.edelivery.tech.ec.europa.eu (test), edelivery.tech.ec.europa.eu (prod)</param>
        public SmkSmpLookupService(string smpDomain = "smp-test.peppol.org",
                                    string certPath = null,
                                    string certPwd = null,
                                    SecureSslValidationService sslValidator = null)
        {
            _smpDomain = smpDomain;
            _certPath = certPath ?? ConfigurationManager.AppSettings["PeppolP12FilePath"];
            _certPwd = certPwd ?? ConfigurationManager.AppSettings["PeppolP12Password"];
            _isTestEnvironment = bool.Parse(ConfigurationManager.AppSettings["IsTestEnvironment"] ?? "false");
            _sslValidator = sslValidator ?? new SecureSslValidationService(isTestEnvironment: _isTestEnvironment);
            _smpDomains = new List<string> { smpDomain };
            _retryCount = int.Parse(ConfigurationManager.AppSettings["RetryCount"] ?? "3");
            _initialDelayMs = int.Parse(ConfigurationManager.AppSettings["InitialDelayMs"] ?? "500");
            _timeoutSec = int.Parse(ConfigurationManager.AppSettings["TimeoutSec"] ?? "10");

            var failover = ConfigurationManager.AppSettings["SmpFailoverDomains"];
            if (!string.IsNullOrWhiteSpace(failover))
            {
                foreach (var d in failover.Split(',').Select(s=>s.Trim()).Where(s=>!string.IsNullOrEmpty(s)))
                    if(!_smpDomains.Contains(d)) _smpDomains.Add(d);
            }
        }

        /// <summary>
        /// EDN SMP ServiceMetadata endpoint for a given participant/docTypeId (no MD5/B- stuff!)
        /// </summary>
        public string BuildEdnSmpServiceMetadataUrl(string participantScheme, string participantId, string documentTypeId)
        {
            // Compose: scheme::id
            var fullPid = $"{participantScheme}::{participantId}";
            var encodedPid = HttpUtility.UrlEncode(fullPid);
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            var url = $"http://{_smpDomain}/{encodedPid}/services/{encodedDocType}";
            //log.Info($"Constructed Peppol EDN SMP ServiceMetadata URL: {url}");
            return url;
        }

        private string BuildSmpUrl(string domain, string participantScheme, string participantId, string documentTypeId)
        {
            var fullPid = $"{participantScheme}::{participantId}";
            var encodedPid = HttpUtility.UrlEncode(fullPid);
            var encodedDocType = HttpUtility.UrlEncode(documentTypeId);
            return $"http://{domain}/{encodedPid}/services/{encodedDocType}";
        }

        /// <summary>
        /// SMP lookup for AS4 endpoint and certificate (EDN/EDelivery SMP style)
        /// </summary>
        /// <param name="participantId">e.g., "9922:NGTBCNTRLP1001"</param>
        /// <param name="participantScheme">e.g., "iso6523-actorid-upis"</param>
        /// <param name="documentTypeId">e.g., "busdox-docid-qns::urn:..."</param>
        /// <param name="processId">e.g., "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0"</param>
        public async Task<PeppolEndpointMetadata> LookupEndpointMetadata(string participantId, string participantScheme, string documentTypeId, string processId)
        {
            string cacheKey = participantId + "|" + documentTypeId + "|" + processId;
            if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                log.Debug($"SMP cache hit for {cacheKey}");
                return cached.Meta;
            }

            Exception lastEx = null;
            foreach (var domain in _smpDomains)
            {
                var url = BuildSmpUrl(domain, participantScheme, participantId, documentTypeId);
                for (int attempt = 0; attempt < _retryCount; attempt++)
                {
                    var delay = TimeSpan.FromMilliseconds(_initialDelayMs * Math.Pow(2, attempt));
                    if (attempt > 0)
                        await Task.Delay(delay);

                    try
                    {
                        var meta = await QuerySmp(url, processId);
                        MetricsService.Increment("smp_calls_success");
                        _cache[cacheKey] = (meta, DateTime.UtcNow.AddMinutes(60));
                        return meta;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        log.Warn($"SMP attempt {attempt+1}/{_retryCount} failed for domain {domain}: {ex.Message}");
                        MetricsService.Increment("smp_calls_failed");
                    }
                }

                log.Warn($"Failing over to next SMP domain after exhausting retries on {domain}");
            }

            // Graceful fallback: if cached metadata exists (even if expired), return it rather than failing hard
            if (_cache.TryGetValue(cacheKey, out var stale))
            {
                log.Error($"SMP lookup failed; returning stale cache for {cacheKey}");
                return stale.Meta;
            }

            throw new Exception("SMP lookup failed after retries and failover", lastEx);
        }

        private async Task<PeppolEndpointMetadata> QuerySmp(string smpUrl, string processId)
        {
            var requestUri = new Uri(smpUrl);
            log.Info($"Querying SMP {smpUrl}");

            var cert = new X509Certificate2(_certPath, _certPwd, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual
            };
            handler.ClientCertificates.Add(cert);
            handler.ServerCertificateCustomValidationCallback = (sender, serverCert, chain, sslErrors) =>
                _sslValidator.ValidateServerCertificate(sender, serverCert, chain, sslErrors, requestUri);
            handler.SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;

            using var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_timeoutSec)
            };

            var response = await httpClient.GetAsync(requestUri);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"SMP HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            var xml = await response.Content.ReadAsStringAsync();
            var doc = XDocument.Parse(xml);
            XNamespace smp = "http://busdox.org/serviceMetadata/publishing/1.0/";
            XNamespace id = "http://busdox.org/transport/identifiers/1.0/";
            XNamespace wsa = "http://www.w3.org/2005/08/addressing";

            var processList = doc.Descendants(smp + "Process").ToList();
            if (!processList.Any())
                throw new Exception("No <Process> element in SMP");
            XElement endpoint = null;
            foreach (var process in processList)
            {
                var pidVal = process.Element(id + "ProcessIdentifier")?.Value ?? "";
                if (!string.IsNullOrEmpty(processId) && !pidVal.Contains(processId))
                    continue;
                endpoint = process.Descendants(smp + "Endpoint")
                    .FirstOrDefault(e => ((string)e.Attribute("transportProfile"))?.Contains("peppol-transport-as4") == true);
                if (endpoint != null) break;
            }
            if (endpoint == null) throw new Exception("No AS4 endpoint in SMP");
            var endpointUrl = endpoint.Element(wsa + "EndpointReference")?.Element(wsa + "Address")?.Value;
            var certificate = endpoint.Element(smp + "Certificate")?.Value;
            if (string.IsNullOrEmpty(endpointUrl) || string.IsNullOrEmpty(certificate))
                throw new Exception("Endpoint URL or certificate missing in SMP");
            return new PeppolEndpointMetadata { EndpointUrl = endpointUrl.Trim(), Certificate = certificate.Trim() };
        }
    }

    public class PeppolEndpointMetadata
    {
        public string EndpointUrl { get; set; }
        public string Certificate { get; set; }
    }
}