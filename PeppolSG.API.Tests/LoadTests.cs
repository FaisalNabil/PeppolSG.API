using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;
using PeppolSG.API.Controllers;
using PeppolSG.API.Tests;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class LoadTests
    {
        [TestMethod]
        public void MessageValidation_PerformanceBenchmark_100MessagesWithin5Seconds()
        {
            var validator = new MessageValidationService();
            var sw = Stopwatch.StartNew();
            for (int i=0;i<100;i++)
            {
                var soap = TestSoapBuilder.BuildMinimalValidSoap($"load-{i}");
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
            Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5), $"Benchmark exceeded: {sw.Elapsed}");
        }
    }
} 