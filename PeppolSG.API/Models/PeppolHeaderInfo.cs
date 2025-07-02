namespace PeppolSG.API.Models
{
    public class PeppolHeaderInfo
    {
        public string SenderId { get; set; }
        public string ReceiverId { get; set; }
        public string SenderScheme { get; set; }
        public string ReceiverScheme { get; set; }
        public string DocTypeId { get; set; } 
        public string DocumentScheme { get; set; }
        public string ProcessId { get; set; }
        public string InstanceId { get; set; }
    }
} 