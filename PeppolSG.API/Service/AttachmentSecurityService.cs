using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.IO.Compression;
using log4net;
using System.Buffers;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Provides secure handling utilities for inbound attachments – size limits, GZIP-bomb protection and MIME filtering.
    /// </summary>
    public static class AttachmentSecurityService
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(AttachmentSecurityService));

        private static readonly int MaxAttachmentSizeMb = int.Parse(ConfigurationManager.AppSettings["MaxAttachmentSizeMb"] ?? "50");
        private static readonly int MaxDecompressedSizeMb = int.Parse(ConfigurationManager.AppSettings["MaxDecompressedAttachmentSizeMb"] ?? "200");
        private static readonly int MaxExpansionRatio = int.Parse(ConfigurationManager.AppSettings["GzipExpansionRatio"] ?? "100");

        private static readonly HashSet<string> AllowedMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/xml",
            "text/xml",
            "application/gzip",
            "application/pdf",
            "application/octet-stream" // encrypted attachments typically octet-stream
        };

        /// <summary>
        /// Validates MIME type against allowed list.
        /// </summary>
        public static void ValidateMimeType(string mime)
        {
            if (string.IsNullOrWhiteSpace(mime))
                throw new Exception("Attachment MIME type missing.");
            if (!AllowedMimeTypes.Contains(mime))
                throw new Exception($"MIME type '{mime}' not permitted.");
        }

        /// <summary>
        /// Securely decompresses a GZIP blob while enforcing expansion-ratio and absolute size limits.
        /// </summary>
        public static byte[] SecureGzipDecompress(byte[] gzBytes)
        {
            if (gzBytes == null) throw new ArgumentNullException(nameof(gzBytes));
            if (gzBytes.Length > MaxAttachmentSizeMb * 1024L * 1024L)
                throw new Exception("Compressed data exceeds attachment size limit.");

            long maxDecompressedBytes = MaxDecompressedSizeMb * 1024L * 1024L;
            long expansionThreshold = gzBytes.Length * MaxExpansionRatio;
            if (expansionThreshold > maxDecompressedBytes)
                expansionThreshold = maxDecompressedBytes;

            using var input = new MemoryStream(gzBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int read;
                long total = 0;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > expansionThreshold)
                        throw new Exception("GZIP bomb detected – expansion ratio exceeded.");
                    output.Write(buffer, 0, read);
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }

            if (total > maxDecompressedBytes)
                throw new Exception("Decompressed data exceeds maximum allowed size.");

            log.Debug($"SecureGzipDecompress – compressed {gzBytes.Length} bytes → {total} bytes");
            return output.ToArray();
        }
    }
} 