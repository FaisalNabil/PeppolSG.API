using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;
using PeppolSG.API.Controllers;
using PeppolSG.API.Tests;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class ComplianceTests
    {
        [TestMethod]
        public void MinimalValidSoap_ShouldBePeppolCompliant()
        {
            var soap = TestSoapBuilder.BuildMinimalValidSoap("comp-" + Guid.NewGuid());
            var parts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                }
            };
            var validator = new MessageValidationService();
            validator.Validate(soap, parts); // should not throw, thus compliant
        }
    }
} 