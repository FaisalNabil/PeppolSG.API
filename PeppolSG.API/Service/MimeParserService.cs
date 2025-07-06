using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PeppolSG.API.Models;
using PeppolSG.API.Service.Interfaces;
using MimeKit;

namespace PeppolSG.API.Service
{
    public class MimeParserService : IMimeParserService
    {
        public async Task<List<MimePart>> ParseMultipartRequest(Stream stream, string contentType)
        {
            var parts = new List<MimePart>();
            var options = ParserOptions.Default;

            try
            {
                // CRITICAL FIX: Use MimeKit to properly parse MIME without corrupting binary data
                // This avoids the string conversion that corrupts binary attachments
                var message = await MimeMessage.LoadAsync(options, stream);

                if (message.Body is Multipart multipart)
                {
                    foreach (var entity in multipart)
                    {
                        if (entity is MimeKit.MimePart mimePart)
                        {
                            var appPart = new Models.MimePart
                            {
                                ContentId = mimePart.ContentId,
                                ContentType = mimePart.ContentType.MimeType,
                                Headers = new Dictionary<string, string>()
                            };

                            // Copy all headers
                            foreach (var header in mimePart.Headers)
                            {
                                appPart.Headers[header.Id.ToString().ToLowerInvariant()] = header.Value;
                            }

                            // CRITICAL: Properly decode binary content without string conversion
                            using (var ms = new MemoryStream())
                            {
                                await mimePart.Content.DecodeToAsync(ms);
                                appPart.ContentBytes = ms.ToArray();
                            }
                            
                            // For text-based parts, also provide text representation
                            if (mimePart.IsText)
                            {
                                appPart.ContentText = mimePart.Text;
                            }
                            else
                            {
                                appPart.ContentText = null; // No text representation for binary data
                            }

                            parts.Add(appPart);
                        }
                    }
                }
                else if (message.Body is MimeKit.MimePart singlePart)
                {
                    // Handle single part message
                    var appPart = new Models.MimePart
                    {
                        ContentId = singlePart.ContentId,
                        ContentType = singlePart.ContentType.MimeType,
                        Headers = new Dictionary<string, string>()
                    };

                    // Copy all headers
                    foreach (var header in singlePart.Headers)
                    {
                        appPart.Headers[header.Id.ToString().ToLowerInvariant()] = header.Value;
                    }

                    // Properly decode content
                    using (var ms = new MemoryStream())
                    {
                        await singlePart.Content.DecodeToAsync(ms);
                        appPart.ContentBytes = ms.ToArray();
                    }
                    
                    if (singlePart.IsText)
                    {
                        appPart.ContentText = singlePart.Text;
                    }

                    parts.Add(appPart);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse MIME multipart content: {ex.Message}", ex);
            }

            return parts;
        }
    }
} 