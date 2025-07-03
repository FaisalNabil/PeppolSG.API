using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace PeppolSG.API.Models
{
    /// <summary>
    /// Represents the ServiceGroup element in an SMP ServiceGroup response
    /// </summary>
    [XmlRoot("ServiceGroup", Namespace = "http://busdox.org/serviceMetadata/publishing/1.0/")]
    public class SmpServiceGroup
    {
        [XmlElement("ParticipantIdentifier", Namespace = "http://busdox.org/transport/identifiers/1.0/")]
        public SmpParticipantIdentifier ParticipantIdentifier { get; set; }

        [XmlArray("ServiceMetadataReferenceCollection")]
        [XmlArrayItem("ServiceMetadataReference")]
        public List<SmpServiceMetadataReference> ServiceMetadataReferenceCollection { get; set; }
    }

    /// <summary>
    /// Represents a ParticipantIdentifier element
    /// </summary>
    public class SmpParticipantIdentifier
    {
        [XmlAttribute("scheme")]
        public string Scheme { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    /// <summary>
    /// Represents a ServiceMetadataReference element
    /// </summary>
    public class SmpServiceMetadataReference
    {
        [XmlAttribute("href")]
        public string Href { get; set; }
    }

    /// <summary>
    /// Represents the complete ServiceMetadata response from an SMP server
    /// as specified in the BUSDOX SMP specification.
    /// </summary>
    [XmlRoot("SignedServiceMetadata", Namespace = "http://busdox.org/serviceMetadata/publishing/1.0/")]
    public class SmpSignedServiceMetadata
    {
        [XmlElement("ServiceMetadata")]
        public SmpServiceMetadata ServiceMetadata { get; set; }

        public SmpSignedServiceMetadata()
        {
            ServiceMetadata = new SmpServiceMetadata();
        }
    }

    /// <summary>
    /// Represents the complete ServiceMetadata response from an SMP server
    /// as specified in the BUSDOX SMP specification.
    /// </summary>
    [XmlRoot("ServiceMetadata", Namespace = "http://busdox.org/serviceMetadata/publishing/1.0/")]
    public class SmpServiceMetadata
    {
        [XmlElement("ServiceInformation")]
        public SmpServiceInformation ServiceInformation { get; set; }

        public SmpServiceMetadata()
        {
            ServiceInformation = new SmpServiceInformation();
        }
    }

    /// <summary>
    /// Contains information about the service, including participant, document type, and processes.
    /// </summary>
    public class SmpServiceInformation
    {
        [XmlElement("ParticipantIdentifier", Namespace = "http://busdox.org/transport/identifiers/1.0/")]
        public SmpParticipantIdentifier ParticipantIdentifier { get; set; }

        [XmlElement("DocumentIdentifier", Namespace = "http://busdox.org/transport/identifiers/1.0/")]
        public SmpDocumentIdentifier DocumentIdentifier { get; set; }

        [XmlArray("ProcessList")]
        [XmlArrayItem("Process")]
        public List<SmpProcess> ProcessList { get; set; }

        public SmpServiceInformation()
        {
            ProcessList = new List<SmpProcess>();
        }
    }

    /// <summary>
    /// Represents a DocumentIdentifier element with its scheme.
    /// </summary>
    public class SmpDocumentIdentifier
    {
        [XmlAttribute("scheme")]
        public string Scheme { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    /// <summary>
    /// Represents a Process element containing the process identifier and endpoints.
    /// </summary>
    public class SmpProcess
    {
        [XmlElement("ProcessIdentifier", Namespace = "http://busdox.org/transport/identifiers/1.0/")]
        public SmpProcessIdentifier ProcessIdentifier { get; set; }

        [XmlElement("ServiceEndpointList")]
        public SmpServiceEndpointList ServiceEndpointList { get; set; }

        public SmpProcess()
        {
            ServiceEndpointList = new SmpServiceEndpointList();
        }
    }

    /// <summary>
    /// Represents a ProcessIdentifier element with its scheme.
    /// </summary>
    public class SmpProcessIdentifier
    {
        [XmlAttribute("scheme")]
        public string Scheme { get; set; }

        [XmlText]
        public string Value { get; set; }
    }

    /// <summary>
    /// Contains a list of service endpoints.
    /// </summary>
    public class SmpServiceEndpointList
    {
        [XmlElement("Endpoint")]
        public List<SmpEndpoint> Endpoints { get; set; }

        public SmpServiceEndpointList()
        {
            Endpoints = new List<SmpEndpoint>();
        }
    }

    /// <summary>
    /// Represents an Endpoint with its transport profile, address, and certificate.
    /// </summary>
    public class SmpEndpoint
    {
        [XmlAttribute("transportProfile")]
        public string TransportProfile { get; set; }

        [XmlElement("EndpointReference", Namespace = "http://www.w3.org/2005/08/addressing")]
        public SmpEndpointReference EndpointReference { get; set; }

        [XmlElement("RequireBusinessLevelSignature")]
        public bool RequireBusinessLevelSignature { get; set; }

        [XmlElement("Certificate")]
        public string Certificate { get; set; }

        [XmlElement("ServiceDescription")]
        public string ServiceDescription { get; set; }

        [XmlElement("TechnicalContactUrl")]
        public string TechnicalContactUrl { get; set; }
    }

    /// <summary>
    /// Represents the EndpointReference element containing the network address.
    /// </summary>
    public class SmpEndpointReference
    {
        [XmlElement("Address", Namespace = "http://www.w3.org/2005/08/addressing")]
        public string Address { get; set; }
    }
} 