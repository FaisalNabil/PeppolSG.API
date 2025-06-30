using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Linq;
using PeppolSG.API.Service;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class IntegrationTests
    {
        private MessageValidationService _msgValidator;
        private MessageIdManager _idManager;

        [TestInitialize]
        public void Setup()
        {
            _msgValidator = new MessageValidationService();
            _idManager = new MessageIdManager();
        }

        [TestMethod]
        public void EndToEnd_ValidateAndRegisterMessage_ShouldSucceed()
        {
            var soap = MessageValidationTests.BuildMinimalValidSoap("endtoend@domain");
            var mimeParts = new List<As4Controller.MimePartManual>
            {
                new As4Controller.MimePartManual
                {
                    ContentText = soap.ToString(),
                    ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                    Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                }
            };
            _msgValidator.Validate(soap, mimeParts);
            bool dup = _idManager.IsDuplicate("endtoend@domain");
            Assert.IsFalse(dup);
        }

        [TestMethod]
        public void Performance_Process100Messages_Under5Seconds()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i=0;i<100;i++)
            {
                string id = $"perf-{i}@domain";
                var soap = MessageValidationTests.BuildMinimalValidSoap(id);
                var mimeParts = new List<As4Controller.MimePartManual>
                {
                    new As4Controller.MimePartManual
                    {
                        ContentText = soap.ToString(),
                        ContentBytes = Encoding.UTF8.GetBytes(soap.ToString()),
                        Headers = new Dictionary<string,string>{{"content-type","application/soap+xml"}}
                    }
                };
                _msgValidator.Validate(soap, mimeParts);
                _idManager.IsDuplicate(id);
            }
            sw.Stop();
            Assert.IsTrue(sw.ElapsedMilliseconds < 5000, "Processing exceeded 5 seconds");
        }
    }
} 