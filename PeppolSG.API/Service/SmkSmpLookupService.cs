using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace PeppolSG.API.Service
{
    public class SmkSmpLookupService
    {
        //private static readonly ILog log = LogManager.GetLogger(typeof(SmkSmpLookupService));
        private readonly string _smpDomain;
        private readonly string _certPath;
        private readonly string _certPwd;

        /// <param name="smpDomain">e.g. acc.edelivery.tech.ec.europa.eu (test), edelivery.tech.ec.europa.eu (prod)</param>
        public SmkSmpLookupService(string smpDomain = "smp-test.peppol.org",
                                    string certPath = null,
                                    string certPwd = null)
        {
            _smpDomain = smpDomain;
            _certPath = certPath ?? ConfigurationManager.AppSettings["PeppolP12FilePath"];
            _certPwd = certPwd ?? ConfigurationManager.AppSettings["PeppolP12Password"];
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

        /// <summary>
        /// SMP lookup for AS4 endpoint and certificate (EDN/EDelivery SMP style)
        /// </summary>
        /// <param name="participantId">e.g., "9922:NGTBCNTRLP1001"</param>
        /// <param name="participantScheme">e.g., "iso6523-actorid-upis"</param>
        /// <param name="documentTypeId">e.g., "busdox-docid-qns::urn:..."</param>
        /// <param name="processId">e.g., "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0"</param>
        public async Task<PeppolEndpointMetadata> LookupEndpointMetadata(string participantId, string participantScheme, string documentTypeId, string processId)
        {
            var smpUrl = BuildEdnSmpServiceMetadataUrl(participantScheme, participantId, documentTypeId);

            // Load client certificate
            var cert = new X509Certificate2(_certPath, _certPwd, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);

            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            handler.ServerCertificateCustomValidationCallback = (sender, clientCert, chain, sslPolicyErrors) => true; // Accept all (test only!)

            using (var httpClient = new HttpClient(handler))
            {
                var response = await httpClient.GetAsync(smpUrl);
                //log.Info($"Response: {response}");
                if (!response.IsSuccessStatusCode)
                    throw new Exception($"SMP not found for {participantId}: {response.StatusCode}");

                var xml = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(xml);

                XNamespace smp = "http://busdox.org/serviceMetadata/publishing/1.0/";
                XNamespace id = "http://busdox.org/transport/identifiers/1.0/";
                XNamespace wsa = "http://www.w3.org/2005/08/addressing";

                var processList = doc.Descendants(smp + "Process").ToList();
                if (!processList.Any())
                    throw new Exception("No <Process> element found in SMP ServiceMetadata.");

                XElement endpoint = null;
                foreach (var process in processList)
                {
                    var processIdVal = process.Element(id + "ProcessIdentifier")?.Value ?? "";
                    if (!string.IsNullOrEmpty(processId) && !processIdVal.Contains(processId))
                        continue;

                    // Look for AS4 endpoint (either v2_0 or v1_0)
                    endpoint = process
                        .Descendants(smp + "Endpoint")
                        .FirstOrDefault(e => (string)e.Attribute("transportProfile") != null
                            && ((string)e.Attribute("transportProfile")).Contains("peppol-transport-as4"));

                    if (endpoint != null)
                        break; // found valid AS4 endpoint for process
                }

                if (endpoint == null)
                    throw new Exception("No AS4 endpoint found in SMP metadata for process " + processId);

                // The endpoint URL is in <wsa:Address>
                var endpointUrl = endpoint.Element(wsa + "EndpointReference")?.Element(wsa + "Address")?.Value;
                if (string.IsNullOrEmpty(endpointUrl))
                    throw new Exception("No Endpoint URL found in SMP metadata");

                // The cert is in <smp:Certificate>
                var certificate = endpoint.Element(smp + "Certificate")?.Value;
                if (string.IsNullOrEmpty(certificate))
                    throw new Exception("No Certificate found in SMP metadata");

                return new PeppolEndpointMetadata
                {
                    EndpointUrl = endpointUrl.Trim(),
                    Certificate = certificate.Trim()
                };
            }
        }
    }

    public class PeppolEndpointMetadata
    {
        public string EndpointUrl { get; set; }
        public string Certificate { get; set; }
    }
}