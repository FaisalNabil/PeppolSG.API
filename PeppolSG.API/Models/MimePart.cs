using System.Collections.Generic;
using System.Linq;

namespace PeppolSG.API.Models
{
    public class MimePart
    {
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public byte[] ContentBytes { get; set; }
        public string ContentText { get; set; }
        public string ContentType => Headers.TryGetValue("content-type", out var ct) ? ct : "";
        public string ContentId => Headers.TryGetValue("content-id", out var cid) ? cid.Trim('<', '>') : "";
    }
} 