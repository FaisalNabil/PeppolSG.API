using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Web;
using System.Xml;

namespace PeppolSG.API.Service
{
    public class SignedXmlWithId : SignedXml
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        // Static constructor to register custom transforms
        static SignedXmlWithId()
        {
            try
            {
                // Register the AttachmentSignatureTransform for SwA Profile compatibility
                SignedXml.AddAlgorithm(
                    AttachmentSignatureTransform.SwAProfileUrl,
                    typeof(AttachmentSignatureTransform));
                
                log.Info("Successfully registered AttachmentSignatureTransform for SwA Profile support");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to register AttachmentSignatureTransform: {ex.Message}", ex);
                // Continue execution - this is not fatal for most operations
            }
        }

        public SignedXmlWithId(XmlDocument xml) : base(xml) { }
        public SignedXmlWithId(XmlElement xmlElement) : base(xmlElement) { }

        public override XmlElement GetIdElement(XmlDocument doc, string id)
        {
            // Try standard ID first (for non-namespaced "Id")
            XmlElement idElem = base.GetIdElement(doc, id);

            if (idElem == null)
            {
                // Now try for wsu:Id (namespace-aware)
                XmlNamespaceManager nsManager = new XmlNamespaceManager(doc.NameTable);
                nsManager.AddNamespace("wsu", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd");
                idElem = doc.SelectSingleNode("//*[@wsu:Id='" + id + "']", nsManager) as XmlElement;
            }

            if (idElem == null)
            {
                // Also try for plain Id attribute without namespace
                idElem = doc.SelectSingleNode("//*[@Id='" + id + "']") as XmlElement;
            }

            if (idElem != null)
            {
                log.Debug($"Successfully resolved element ID: {id}");
            }
            else
            {
                log.Warn($"Could not resolve element ID: {id}");
            }

            return idElem;
        }

        /// <summary>
        /// Removes unknown transforms from signature references to prevent verification failures
        /// This is used for incoming message signature verification where unknown transforms should be ignored
        /// </summary>
        public static void RemoveUnknownTransforms(XmlElement signatureElement)
        {
            if (signatureElement == null) return;

            try
            {
                var nsManager = new XmlNamespaceManager(signatureElement.OwnerDocument.NameTable);
                nsManager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

                // Find all Transform elements in the signature
                var transforms = signatureElement.SelectNodes(".//ds:Transform", nsManager);
                if (transforms == null) return;

                var transformsToRemove = new List<XmlNode>();

                foreach (XmlElement transform in transforms)
                {
                    var algorithm = transform.GetAttribute("Algorithm");
                    
                    // Check if this is an unknown/unsupported transform
                    if (!string.IsNullOrEmpty(algorithm) && IsUnknownTransform(algorithm))
                    {
                        log.Warn($"Removing unknown transform: {algorithm}");
                        transformsToRemove.Add(transform);
                    }
                }

                // Remove unknown transforms
                foreach (var transform in transformsToRemove)
                {
                    transform.ParentNode?.RemoveChild(transform);
                }

                if (transformsToRemove.Count > 0)
                {
                    log.Info($"Removed {transformsToRemove.Count} unknown transforms from signature");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error removing unknown transforms: {ex.Message}", ex);
                // Continue execution - this is not fatal
            }
        }

        /// <summary>
        /// Determines if a transform algorithm is unknown/unsupported
        /// </summary>
        private static bool IsUnknownTransform(string algorithm)
        {
            // List of known/supported transforms
            var knownTransforms = new[]
            {
                "http://www.w3.org/2000/09/xmldsig#base64",
                "http://www.w3.org/2000/09/xmldsig#enveloped-signature",
                "http://www.w3.org/2001/10/xml-exc-c14n#",
                "http://www.w3.org/2001/10/xml-exc-c14n#WithComments",
                "http://www.w3.org/TR/2001/REC-xml-c14n-20010315",
                "http://www.w3.org/TR/2001/REC-xml-c14n-20010315#WithComments",
                "http://www.w3.org/TR/1999/REC-xpath-19991116",
                "http://www.w3.org/2002/06/xmldsig-filter2",
                AttachmentSignatureTransform.SwAProfileUrl // Our custom transform
            };

            return !knownTransforms.Contains(algorithm);
        }
    }
}