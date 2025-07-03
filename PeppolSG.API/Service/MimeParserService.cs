using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeppolSG.API.Models;
using PeppolSG.API.Service.Interfaces;

namespace PeppolSG.API.Service
{
    public class MimeParserService : IMimeParserService
    {
        public async Task<List<MimePart>> ParseMultipartRequest(Stream stream, string contentType)
        {
            var boundary = ExtractBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
            {
                throw new ArgumentException("MIME boundary not found in Content-Type header.");
            }

            // The stream reader will close the underlying stream, which is what we want.
            using (var reader = new StreamReader(stream))
            {
                var content = await reader.ReadToEndAsync();
                return ParseMultipartContent(content, boundary);
            }
        }

        private List<MimePart> ParseMultipartContent(string content, string boundary)
        {
            var parts = new List<MimePart>();
            var partStrings = content.Split(new[] { $"--{boundary}" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var partString in partStrings)
            {
                if (string.IsNullOrWhiteSpace(partString) || partString.Trim() == "--")
                    continue;

                var part = new MimePart();
                var headerEndIndex = partString.IndexOf("\r\n\r\n");
                if (headerEndIndex == -1) continue;

                var headerSection = partString.Substring(0, headerEndIndex);
                var bodySection = partString.Substring(headerEndIndex + 4);

                var headerLines = headerSection.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var headerLine in headerLines)
                {
                    var headerParts = headerLine.Split(new[] { ':' }, 2);
                    if (headerParts.Length == 2)
                    {
                        part.Headers[headerParts[0].Trim()] = headerParts[1].Trim();
                    }
                }
                
                // CRITICAL FIX: Handle binary content properly for AS4 attachments
                var contentType = part.ContentType.ToLowerInvariant();
                var contentTransferEncoding = part.Headers.TryGetValue("content-transfer-encoding", out var encoding) 
                    ? encoding.ToLowerInvariant() 
                    : "binary";

                if (contentType.Contains("xml") || contentType.Contains("text") || contentType.Contains("soap"))
                {
                    // Text content - treat as string
                    part.ContentText = bodySection.TrimEnd('\r', '\n');
                    part.ContentBytes = Encoding.UTF8.GetBytes(part.ContentText);
                }
                else
                {
                    // Binary content (encrypted attachments, etc.) - treat as raw bytes
                    // CRITICAL: Don't use UTF-8 encoding for binary data
                    if (contentTransferEncoding == "base64")
                    {
                        // Handle Base64 encoded binary content
                        var base64Content = bodySection.TrimEnd('\r', '\n');
                        part.ContentBytes = Convert.FromBase64String(base64Content);
                        part.ContentText = null; // No text representation for binary
                    }
                    else
                    {
                        // Handle raw binary content
                        part.ContentBytes = Encoding.UTF8.GetBytes(bodySection.TrimEnd('\r', '\n'));
                        part.ContentText = null; // No text representation for binary
                    }
                }
                
                parts.Add(part);
            }

            return parts;
        }

        private string ExtractBoundary(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return null;

            var parts = contentType.Split(';');
            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                if (trimmedPart.StartsWith("boundary=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmedPart.Substring("boundary=".Length).Trim('"');
                }
            }
            return null;
        }
    }
} 