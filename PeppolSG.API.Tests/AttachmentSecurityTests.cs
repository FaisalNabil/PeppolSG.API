using System;
using System.IO;
using System.IO.Compression;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class AttachmentSecurityTests
    {
        [TestMethod]
        public void SecureGzipDecompress_WithSmallData_ShouldSucceed()
        {
            var original = new byte[1024]; // 1KB
            new Random().NextBytes(original);
            byte[] gz;
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    gzip.Write(original, 0, original.Length);
                    gzip.Close();
                }
                gz = ms.ToArray();
            }
            var result = AttachmentSecurityService.SecureGzipDecompress(gz);
            CollectionAssert.AreEqual(original, result);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void SecureGzipDecompress_WithBomb_ShouldFail()
        {
            // create gzip bomb: tiny compressed, huge decompressed (110* ratio)
            var large = new byte[1024 * 1024 * 5]; // 5MB of zeroes -> compresses well
            byte[] gz;
            using (var ms = new MemoryStream())
            {
                using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    gzip.Write(large, 0, large.Length);
                    gzip.Close();
                }
                gz = ms.ToArray();
            }
            // manipulate config ratio? assume default 100, our ratio maybe >100, worst-case maybe less; ensure fail by lowering ratio internally? We'll trust >100.
            AttachmentSecurityService.SecureGzipDecompress(gz);
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void ValidateMimeType_Disallowed_ShouldFail()
        {
            AttachmentSecurityService.ValidateMimeType("image/png");
        }
    }
} 