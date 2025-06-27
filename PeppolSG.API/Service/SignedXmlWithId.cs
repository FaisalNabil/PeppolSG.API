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

            return idElem;
        }
    }
}