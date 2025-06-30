using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;
using PeppolSG.API.Controllers;
using System.Xml.Linq;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class SecurityPenetrationTests
    {
        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void XmlExternalEntity_ShouldBeRejected()
        {
            // Build SOAP with XXE attempt
            var xml = "<?xml version=\"1.0\"?><!DOCTYPE foo [<!ELEMENT foo ANY ><!ENTITY xxe SYSTEM \"file:///etc/passwd\" >]><Envelope><Header/><Body><foo>&xxe;</foo></Body></Envelope>";
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = doc.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(doc.ToString()),
                    Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                }
            };
            var validator = new MessageValidationService();
            validator.Validate(doc, parts);
        }
    }
} 