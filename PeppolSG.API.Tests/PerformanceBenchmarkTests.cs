using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Controllers;
using PeppolSG.API.Service;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class PerformanceBenchmarkTests
    {
        [TestMethod]
        public void MessageValidation_ThousandMsgs_Under20Seconds()
        {
            var validator = new MessageValidationService();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                var soap = TestSoapBuilder.BuildMinimalValidSoap($"perf-{i}");
                var parts = new List<As4Controller.MimePartManual>
                {
                    new As4Controller.MimePartManual
                    {
                        ContentText = soap.ToString(),
                        ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                        Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                    }
                };
                validator.Validate(soap, parts);
            }
            sw.Stop();
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(20), $"Performance target missed: {sw.Elapsed}");
        }
    }
} 