using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Web;
using System.Xml;

namespace PeppolSG.API.Service
{
    public class AttachmentSignatureTransform : Transform
    {
        public const string SwAProfileUrl = "http://docs.oasis-open.org/wss/oasis-wss-SwAProfile-1.1#Attachment-Content-Signature-Transform";
        public string ContentType { get; set; }
        private Stream _inputStream;

        public AttachmentSignatureTransform(string contentType = null)
        {
            Algorithm = SwAProfileUrl;
            ContentType = contentType;
        }

        public override void LoadInput(object obj)
        {
            if (obj is Stream s) _inputStream = s;
            else throw new ArgumentException("Must be Stream");
        }

        public override object GetOutput(Type type)
        {
            return _inputStream;
        }

        public override object GetOutput() => _inputStream;

        public override void LoadInnerXml(XmlNodeList nodeList)
        {
            // No-op
        }

        protected override XmlNodeList GetInnerXml()
        {
            return null;
        }

        public override Type[] InputTypes => new[] { typeof(Stream) };
        public override Type[] OutputTypes => new[] { typeof(Stream) };
    }
}