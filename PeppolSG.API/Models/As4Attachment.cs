namespace PeppolSG.API.Models
{
    /// <summary>
    /// Represents an AS4 message attachment according to eDelivery AS4 Profile v1.1.0
    /// </summary>
    public class As4Attachment
    {
        /// <summary>
        /// Content-ID header value for the attachment part
        /// </summary>
        public string ContentId { get; set; }

        /// <summary>
        /// MIME Content-Type for the attachment
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Binary content of the attachment
        /// </summary>
        public byte[] Bytes { get; set; }
    }
} 