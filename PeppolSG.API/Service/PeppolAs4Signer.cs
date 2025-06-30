using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Security.Cryptography;
using System.Web;
using System.Xml.Linq;
using System.Xml;

namespace PeppolSG.API.Service
{
    public static class PeppolAs4Signer
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(
            System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly XNamespace XENC = "http://www.w3.org/2001/04/xmlenc#";
        private static readonly XNamespace XENC11 = "http://www.w3.org/2009/xmlenc11#";
        private static readonly XNamespace DS = SignedXml.XmlDsigNamespaceUrl;
        private static readonly XNamespace WSSE = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0.xsd";
        private static readonly XNamespace WSU = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

        /// <summary>
        /// Sign the SOAP envelope, encrypting an attachment if requested.
        /// Note how we now call the builder methods above.
        /// </summary>
        public static void SignEnvelope(
            XDocument envelopeXml,
            string pfxPath,
            string pfxPassword,
            string bstId,
            string messagingId,
            string bodyId,
            string attachmentCid = null,
            byte[] encryptedAttachment = null)
        {
            var cert = new X509Certificate2(
                pfxPath, pfxPassword,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

            // 1) Load into XmlDocument
            var xmlDoc = new XmlDocument { PreserveWhitespace = true };
            using (var reader = envelopeXml.CreateReader())
                xmlDoc.Load(reader);

            // 2) SignedXml setup
            var sxml = new SignedXmlWithId(xmlDoc)
            {
                SigningKey = cert.GetRSAPrivateKey()
            };
            sxml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            sxml.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

            // 3) Reference Messaging + Body
            sxml.AddReference(CreateExcC14NReference("#" + messagingId));
            sxml.AddReference(CreateExcC14NReference("#" + bodyId));

            // 4) If there's an attachment, encrypt it and add its EncryptedKey/Data
            if (!string.IsNullOrEmpty(attachmentCid) && encryptedAttachment != null)
            {
                var attachRef = new Reference(attachmentCid)
                {
                    DigestMethod = SignedXml.XmlDsigSHA256Url
                };
                attachRef.AddTransform(new AttachmentSignatureTransform("application/gzip"));

                // bind the in-memory encrypted bytes to that Reference
                var fieldData = typeof(Reference).GetField("m_refTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                var fieldType = typeof(Reference).GetField("m_refTargetType", BindingFlags.NonPublic | BindingFlags.Instance);
                fieldData.SetValue(attachRef, new MemoryStream(encryptedAttachment));
                fieldType.SetValue(attachRef, 0); // stream

                sxml.AddReference(attachRef);
            }

            // 5) KeyInfo → wsse:SecurityTokenReference
            sxml.KeyInfo = BuildKeyInfo(bstId);

            // 6) Compute + patch
            sxml.ComputeSignature();
            var signatureXml = sxml.GetXml();

            // remove any <ec:InclusiveNamespaces> leftovers if you like...
            // patch transforms for attachment refs to use the SwA‐profile URI, if needed.

            // 7) Replace the placeholder <ds:Signature/> in the envelope
            ReplaceSignature(xmlDoc, signatureXml);

            // 8) Save back into XDocument
            using (var ms = new MemoryStream())
            {
                xmlDoc.Save(ms);
                ms.Position = 0;
                envelopeXml.Root.ReplaceWith(XDocument.Load(ms).Root);
            }
        }

        private static Reference CreateExcC14NReference(string uri)
        {
            var r = new Reference(uri);
            r.AddTransform(new XmlDsigExcC14NTransform());
            r.DigestMethod = SignedXml.XmlDsigSHA256Url;
            return r;
        }

        private static KeyInfo BuildKeyInfo(string bstId)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };

            // Create the <ds:KeyInfo> wrapper
            var keyInfoElement = doc.CreateElement("ds", "KeyInfo", SignedXml.XmlDsigNamespaceUrl);
            keyInfoElement.SetAttribute("Id", "KI-" + Guid.NewGuid().ToString("N"));

            // WS-Security and WS-Utility namespace URIs as strings:
            const string wsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0.xsd";
            const string wsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

            // Build the <wsse:SecurityTokenReference>
            var strElement = doc.CreateElement("wsse", "SecurityTokenReference");
            // !!! here’s the fix: use the namespace URI string, not an XName !!!
            strElement.SetAttribute("Id", wsuNs, "STR-" + Guid.NewGuid().ToString("N"));

            // Build the inner <wsse:Reference>
            var refElement = doc.CreateElement("wsse", "Reference", wsseNs);
            refElement.SetAttribute("URI", "#" + bstId);
            refElement.SetAttribute(
                "ValueType",
                "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3"
            );
            strElement.AppendChild(refElement);

            keyInfoElement.AppendChild(strElement);

            var keyInfo = new KeyInfo();
            keyInfo.LoadXml(keyInfoElement);
            return keyInfo;
        }

        private static void ReplaceSignature(XmlDocument xmlDoc, XmlElement newSig)
        {
            var nsm = new XmlNamespaceManager(xmlDoc.NameTable);
            nsm.AddNamespace("ds", DS.NamespaceName);
            var oldSig = xmlDoc.SelectSingleNode("//ds:Signature", nsm);
            if (oldSig == null)
                throw new InvalidOperationException("No <ds:Signature> placeholder found.");
            oldSig.ParentNode.ReplaceChild(xmlDoc.ImportNode(newSig, true), oldSig);
        }


        /// <summary>
        /// Recursively sets the prefix for all ds:Signature nodes.
        /// </summary>
        public static void SetSignaturePrefix(XmlElement node, string prefix)
        {
            if (node.NamespaceURI == "http://www.w3.org/2000/09/xmldsig#")
                node.Prefix = prefix;
            foreach (XmlNode child in node.ChildNodes)
            {
                if (child is XmlElement childElem)
                    SetSignaturePrefix(childElem, prefix);
            }
        }


    }

    static class XElementExtensions
    {
        public static XmlElement ToXmlElement(this XElement x)
        {
            var doc = new XmlDocument();
            doc.LoadXml(x.ToString());
            return doc.DocumentElement;
        }
    }

    public static class CryptoUtil
    {

        //------------------------------------------------------------------
        // Classic out‑parameters API (works nicely in C# 7.3 projects)
        //------------------------------------------------------------------
        public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (plain == null) throw new ArgumentNullException(nameof(plain));

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var enc = aes.CreateEncryptor();
            return enc.TransformFinalBlock(plain, 0, plain.Length);
        }
        public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] cipher)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));

            var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            var decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        }
        // ------------------------------------------------------------------
        // AES-GCM helpers used for eDelivery AS4 2.0 profile
        // ------------------------------------------------------------------
        public static byte[] AesGcmEncrypt(byte[] key, byte[] iv, byte[] plain, out byte[] tag)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (plain == null) throw new ArgumentNullException(nameof(plain));

            var cipher = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            var parameters = new Org.BouncyCastle.Crypto.Parameters.AeadParameters(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), 128, iv);
            cipher.Init(true, parameters);

            var output = new byte[cipher.GetOutputSize(plain.Length)];
            int len = cipher.ProcessBytes(plain, 0, plain.Length, output, 0);
            len += cipher.DoFinal(output, len);

            int tagLen = 16; // 128 bit tag
            int cipherLen = len - tagLen;
            var ciphertext = new byte[cipherLen];
            tag = new byte[tagLen];
            Array.Copy(output, 0, ciphertext, 0, cipherLen);
            Array.Copy(output, cipherLen, tag, 0, tagLen);
            return ciphertext;
        }

        public static byte[] AesGcmDecrypt(byte[] key, byte[] iv, byte[] cipher, byte[] tag)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (tag == null) throw new ArgumentNullException(nameof(tag));

            var input = new byte[cipher.Length + tag.Length];
            Array.Copy(cipher, 0, input, 0, cipher.Length);
            Array.Copy(tag, 0, input, cipher.Length, tag.Length);

            var gcm = new Org.BouncyCastle.Crypto.Modes.GcmBlockCipher(new Org.BouncyCastle.Crypto.Engines.AesEngine());
            var parameters = new Org.BouncyCastle.Crypto.Parameters.AeadParameters(new Org.BouncyCastle.Crypto.Parameters.KeyParameter(key), tag.Length * 8, iv);
            gcm.Init(false, parameters);

            var output = new byte[gcm.GetOutputSize(input.Length)];
            int len = gcm.ProcessBytes(input, 0, input.Length, output, 0);
            len += gcm.DoFinal(output, len);

            var plain = new byte[len];
            Array.Copy(output, 0, plain, 0, len);
            return plain;
        }
    }
    internal sealed class AttachmentResolver : XmlUrlResolver
    {
        private readonly IDictionary<string, byte[]> _map;
        public AttachmentResolver(IDictionary<string, byte[]> map) => _map = map;

        public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
        {
            if (absoluteUri.Scheme.Equals("cid", StringComparison.OrdinalIgnoreCase) &&
                _map.TryGetValue(absoluteUri.ToString(), out var bytes))
            {
                return new MemoryStream(bytes);
            }
            return base.GetEntity(absoluteUri, role, ofObjectToReturn);
        }
    }
}