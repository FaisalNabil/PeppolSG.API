using System;
using System.Collections.Generic;

namespace PeppolSG.API.Models
{
    public class As4InboundMetadata
    {
        public string MessageId { get; set; }
        public string Timestamp { get; set; }
        public string Sender { get; set; }
        public string Receiver { get; set; }
        public string DocumentType { get; set; }
        public List<string> PayloadPaths { get; set; }
        public string RawSoapXml { get; set; }
        public object EnvelopeHeader { get; set; }
        public string CertificateThumbprint { get; set; }
        public string CertificateBase64 { get; set; }
        public string CorrelationId { get; set; }
        public DateTime ProcessingStartTime { get; set; }
        public DateTime ProcessingEndTime { get; set; }
    }
} 