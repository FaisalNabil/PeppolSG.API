using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.Xml;
using System.Web;
using System.Xml;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Implements the SwA (SOAP with Attachments) Profile transform for signing attachments.
    /// The digest is calculated on the raw attachment bytes, so this transform just passes data through.
    /// </summary>
    public class AttachmentSignatureTransform : Transform
    {
        public const string SwAProfileUrl = "http://docs.oasis-open.org/wss/oasis-wss-SwAProfile-1.1#Attachment-Content-Signature-Transform";
        private Stream _inputStream;

        public AttachmentSignatureTransform()
        {
            Algorithm = SwAProfileUrl;
        }

        public AttachmentSignatureTransform(string contentType)
        {
            Algorithm = SwAProfileUrl;
        }

        public override Type[] InputTypes => new[] { typeof(Stream) };
        public override Type[] OutputTypes => new[] { typeof(Stream) };

        public override void LoadInput(object obj)
        {
            if (obj is Stream stream)
            {
                _inputStream = stream;
            }
            else
            {
                throw new ArgumentException("Input must be a Stream", nameof(obj));
            }
        }

        public override object GetOutput()
        {
            return _inputStream;
        }

        public override object GetOutput(Type type)
        {
            if (type != typeof(Stream))
                throw new ArgumentException("Output type must be Stream", nameof(type));
            return GetOutput();
        }

        protected override XmlNodeList GetInnerXml()
        {
            return null;
        }

        public override void LoadInnerXml(XmlNodeList nodeList)
        {
            // No inner XML for this transform
        }
    }
}