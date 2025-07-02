using System.Xml.Linq;

namespace PeppolSG.API.Service.Interfaces
{
    public interface IPeppolAs4MessageValidator
    {
        ValidationResult ValidateIncomingMessage(XDocument soapMessage, string messageType = "UserMessage");
    }
} 