using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using log4net;
using PeppolSG.API.Service.Interfaces;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Validates AS4 messages for Peppol compliance according to CEF eDelivery AS4 Profile
    /// </summary>
    public class PeppolAs4MessageValidator : IPeppolAs4MessageValidator
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(PeppolAs4MessageValidator));
        private readonly IPeppolConfigurationService _config;

        // XML Namespaces used in AS4/ebMS3
        private static readonly XNamespace EB = "http://docs.oasis-open.org/ebxml-msg/ebms/v3.0/ns/core/200704/";
        private static readonly XNamespace S12 = "http://www.w3.org/2003/05/soap-envelope";
        private static readonly XNamespace WSSE = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        private static readonly XNamespace WSU = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        private static readonly XNamespace DS = "http://www.w3.org/2000/09/xmldsig#";

        public PeppolAs4MessageValidator(IPeppolConfigurationService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Validates an incoming AS4 message for Peppol compliance
        /// </summary>
        public ValidationResult ValidateIncomingMessage(XDocument soapMessage, string messageType = "UserMessage")
        {
            var result = new ValidationResult();
            
            try
            {
                log.Debug($"Starting validation for {messageType}");

                // 1. Validate SOAP envelope structure
                ValidateSoapEnvelope(soapMessage, result);

                // 2. Validate ebMS3 messaging header
                ValidateEbMs3Header(soapMessage, result);

                // 3. Validate WS-Security header
                ValidateWsSecurityHeader(soapMessage, result);

                // 4. Validate message type specific requirements
                if (messageType == "UserMessage")
                {
                    ValidateUserMessage(soapMessage, result);
                }
                else if (messageType == "SignalMessage")
                {
                    ValidateSignalMessage(soapMessage, result);
                }

                // 5. Validate Peppol-specific requirements
                ValidatePeppolRequirements(soapMessage, result);

                if (result.IsValid)
                {
                    log.Info("AS4 message validation passed");
                }
                else
                {
                    log.Warn($"AS4 message validation failed with {result.Errors.Count} errors and {result.Warnings.Count} warnings");
                }
            }
            catch (Exception ex)
            {
                log.Error("Exception during message validation", ex);
                result.AddError("VALIDATION_EXCEPTION", $"Validation failed with exception: {ex.Message}");
            }

            return result;
        }

        private void ValidateSoapEnvelope(XDocument soapMessage, ValidationResult result)
        {
            var envelope = soapMessage.Root;
            
            if (envelope == null)
            {
                result.AddError("SOAP_MISSING_ENVELOPE", "SOAP envelope is missing");
                return;
            }

            // Check SOAP namespace
            if (envelope.Name.Namespace != S12)
            {
                result.AddError("SOAP_INVALID_NAMESPACE", $"Invalid SOAP namespace. Expected: {S12}, Found: {envelope.Name.Namespace}");
            }

            // Check for SOAP Header
            var header = envelope.Element(S12 + "Header");
            if (header == null)
            {
                result.AddError("SOAP_MISSING_HEADER", "SOAP Header is missing");
            }

            // Check for SOAP Body
            var body = envelope.Element(S12 + "Body");
            if (body == null)
            {
                result.AddError("SOAP_MISSING_BODY", "SOAP Body is missing");
            }
            else
            {
                // For Peppol AS4, SOAP Body should be empty (payloads in attachments)
                if (body.HasElements && !body.Elements().All(e => e.Name.LocalName == "Fault"))
                {
                    result.AddWarning("SOAP_BODY_NOT_EMPTY", "SOAP Body should be empty for Peppol AS4 (payloads should be in attachments)");
                }
            }
        }

        private void ValidateEbMs3Header(XDocument soapMessage, ValidationResult result)
        {
            var messaging = soapMessage.Descendants(EB + "Messaging").FirstOrDefault();
            
            if (messaging == null)
            {
                result.AddError("EBMS3_MISSING_MESSAGING", "ebMS3 Messaging element is missing");
                return;
            }

            // Check required namespaces
            var expectedNamespaces = new Dictionary<string, string>
            {
                { "eb", EB.NamespaceName },
                { "ds", DS.NamespaceName },
                { "wsu", WSU.NamespaceName }
            };

            foreach (var ns in expectedNamespaces)
            {
                var nsAttr = messaging.GetNamespaceOfPrefix(ns.Key);
                if (nsAttr == null || nsAttr.NamespaceName != ns.Value)
                {
                    result.AddError($"EBMS3_MISSING_NAMESPACE_{ns.Key.ToUpper()}", 
                        $"Missing or incorrect namespace declaration for {ns.Key}: expected {ns.Value}");
                }
            }

            // Validate wsu:Id on Messaging element
            var messagingId = messaging.Attribute(WSU + "Id")?.Value;
            if (string.IsNullOrWhiteSpace(messagingId))
            {
                result.AddError("EBMS3_MISSING_MESSAGING_ID", "Messaging element must have wsu:Id attribute");
            }
        }

        private void ValidateUserMessage(XDocument soapMessage, ValidationResult result)
        {
            var userMessage = soapMessage.Descendants(EB + "UserMessage").FirstOrDefault();
            
            if (userMessage == null)
            {
                result.AddError("EBMS3_MISSING_USERMESSAGE", "UserMessage element is missing");
                return;
            }

            // Validate MessageInfo
            ValidateMessageInfo(userMessage, result);

            // Validate PartyInfo
            ValidatePartyInfo(userMessage, result);

            // Validate CollaborationInfo
            ValidateCollaborationInfo(userMessage, result);

            // Validate MessageProperties (required for Peppol)
            ValidateMessageProperties(userMessage, result);

            // Validate PayloadInfo
            ValidatePayloadInfo(userMessage, result);
        }

        private void ValidateMessageInfo(XElement userMessage, ValidationResult result)
        {
            var messageInfo = userMessage.Element(EB + "MessageInfo");
            if (messageInfo == null)
            {
                result.AddError("EBMS3_MISSING_MESSAGEINFO", "MessageInfo is missing");
                return;
            }

            // Validate Timestamp
            var timestamp = messageInfo.Element(EB + "Timestamp")?.Value;
            if (string.IsNullOrWhiteSpace(timestamp))
            {
                result.AddError("EBMS3_MISSING_TIMESTAMP", "Timestamp is missing");
            }
            else
            {
                if (!DateTime.TryParse(timestamp, out _))
                {
                    result.AddError("EBMS3_INVALID_TIMESTAMP", "Timestamp format is invalid");
                }
            }

            // Validate MessageId
            var messageId = messageInfo.Element(EB + "MessageId")?.Value;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                result.AddError("EBMS3_MISSING_MESSAGEID", "MessageId is missing");
            }
            else
            {
                // MessageId should conform to RFC2822 format
                var emailPattern = @"^[^@]+@[^@]+$";
                if (!Regex.IsMatch(messageId, emailPattern))
                {
                    result.AddWarning("EBMS3_MESSAGEID_FORMAT", "MessageId should follow RFC2822 format (user@domain)");
                }
            }
        }

        private void ValidatePartyInfo(XElement userMessage, ValidationResult result)
        {
            var partyInfo = userMessage.Element(EB + "PartyInfo");
            if (partyInfo == null)
            {
                result.AddError("EBMS3_MISSING_PARTYINFO", "PartyInfo is missing");
                return;
            }

            // Validate From party
            var from = partyInfo.Element(EB + "From");
            ValidateParty(from, "From", result);

            // Validate To party
            var to = partyInfo.Element(EB + "To");
            ValidateParty(to, "To", result);
        }

        private void ValidateParty(XElement party, string partyType, ValidationResult result)
        {
            if (party == null)
            {
                result.AddError($"EBMS3_MISSING_{partyType.ToUpper()}_PARTY", $"{partyType} party is missing");
                return;
            }

            // Validate PartyId
            var partyId = party.Element(EB + "PartyId");
            if (partyId == null)
            {
                result.AddError($"EBMS3_MISSING_{partyType.ToUpper()}_PARTYID", $"{partyType} PartyId is missing");
            }
            else
            {
                var partyIdValue = partyId.Value;
                var partyIdType = partyId.Attribute("type")?.Value;

                if (string.IsNullOrWhiteSpace(partyIdValue))
                {
                    result.AddError($"EBMS3_EMPTY_{partyType.ToUpper()}_PARTYID", $"{partyType} PartyId value is empty");
                }

                // For Peppol, PartyId type should be the Peppol AP identifier type
                if (!string.IsNullOrWhiteSpace(partyIdType) && partyIdType != _config.PeppolPartyType)
                {
                    result.AddWarning($"PEPPOL_INVALID_{partyType.ToUpper()}_PARTYID_TYPE", 
                        $"{partyType} PartyId type should be {_config.PeppolPartyType} for Peppol");
                }
            }

            // Validate Role
            var role = party.Element(EB + "Role");
            if (role == null)
            {
                result.AddError($"EBMS3_MISSING_{partyType.ToUpper()}_ROLE", $"{partyType} Role is missing");
            }
            else
            {
                var roleValue = role.Value;
                var expectedRole = partyType == "From" ? _config.InitiatorRole : _config.ResponderRole;
                
                if (roleValue != expectedRole)
                {
                    result.AddError($"EBMS3_INVALID_{partyType.ToUpper()}_ROLE", 
                        $"Invalid {partyType} Role. Expected: {expectedRole}, Found: {roleValue}");
                }
            }
        }

        private void ValidateCollaborationInfo(XElement userMessage, ValidationResult result)
        {
            var collaborationInfo = userMessage.Element(EB + "CollaborationInfo");
            if (collaborationInfo == null)
            {
                result.AddError("EBMS3_MISSING_COLLABORATIONINFO", "CollaborationInfo is missing");
                return;
            }

            // Validate AgreementRef
            var agreementRef = collaborationInfo.Element(EB + "AgreementRef");
            if (agreementRef == null)
            {
                result.AddError("EBMS3_MISSING_AGREEMENTREF", "AgreementRef is missing");
            }
            else
            {
                var agreementValue = agreementRef.Value;
                if (agreementValue != _config.PeppolAgreement)
                {
                    result.AddError("PEPPOL_INVALID_AGREEMENT", 
                        $"Invalid Agreement. Expected: {_config.PeppolAgreement}, Found: {agreementValue}");
                }

                // pmode attribute should not be present
                var pmodeAttr = agreementRef.Attribute("pmode");
                if (pmodeAttr != null)
                {
                    result.AddWarning("EBMS3_UNEXPECTED_PMODE", "AgreementRef should not have pmode attribute");
                }
            }

            // Validate Service
            var service = collaborationInfo.Element(EB + "Service");
            if (service == null)
            {
                result.AddError("EBMS3_MISSING_SERVICE", "Service is missing");
            }

            // Validate Action
            var action = collaborationInfo.Element(EB + "Action");
            if (action == null)
            {
                result.AddError("EBMS3_MISSING_ACTION", "Action is missing");
            }

            // Validate ConversationId
            var conversationId = collaborationInfo.Element(EB + "ConversationId");
            if (conversationId == null)
            {
                result.AddError("EBMS3_MISSING_CONVERSATIONID", "ConversationId is missing");
            }
        }

        private void ValidateMessageProperties(XElement userMessage, ValidationResult result)
        {
            var messageProperties = userMessage.Element(EB + "MessageProperties");
            if (messageProperties == null)
            {
                result.AddError("EBMS3_MISSING_MESSAGEPROPERTIES", "MessageProperties is missing");
                return;
            }

            var properties = messageProperties.Elements(EB + "Property").ToList();
            
            // For Four Corner topology, check for required Peppol properties
            var originalSender = properties.FirstOrDefault(p => p.Attribute("name")?.Value == "originalSender");
            var finalRecipient = properties.FirstOrDefault(p => p.Attribute("name")?.Value == "finalRecipient");

            if (originalSender == null)
            {
                result.AddWarning("PEPPOL_MISSING_ORIGINALSENDER", "originalSender property is missing (required for Four Corner topology)");
            }

            if (finalRecipient == null)
            {
                result.AddWarning("PEPPOL_MISSING_FINALRECIPIENT", "finalRecipient property is missing (required for Four Corner topology)");
            }
        }

        private void ValidatePayloadInfo(XElement userMessage, ValidationResult result)
        {
            var payloadInfo = userMessage.Element(EB + "PayloadInfo");
            
            // PayloadInfo is optional, but if present, validate structure
            if (payloadInfo != null)
            {
                var partInfos = payloadInfo.Elements(EB + "PartInfo").ToList();
                
                foreach (var partInfo in partInfos)
                {
                    var href = partInfo.Attribute("href")?.Value;
                    if (string.IsNullOrWhiteSpace(href))
                    {
                        result.AddError("EBMS3_MISSING_PARTINFO_HREF", "PartInfo must have href attribute");
                    }
                    else if (!href.StartsWith("cid:"))
                    {
                        result.AddError("EBMS3_INVALID_PARTINFO_HREF", "PartInfo href must start with 'cid:'");
                    }

                    // Validate PartProperties if present
                    var partProperties = partInfo.Element(EB + "PartProperties");
                    if (partProperties != null)
                    {
                        ValidatePartProperties(partProperties, result);
                    }
                }
            }
        }

        private void ValidatePartProperties(XElement partProperties, ValidationResult result)
        {
            var properties = partProperties.Elements(EB + "Property").ToList();
            
            foreach (var property in properties)
            {
                var name = property.Attribute("name")?.Value;
                var value = property.Value;

                if (string.IsNullOrWhiteSpace(name))
                {
                    result.AddError("EBMS3_MISSING_PROPERTY_NAME", "Property must have name attribute");
                }

                // Validate compression property if present
                if (name == "CompressionType")
                {
                    if (value != _config.CompressionType)
                    {
                        result.AddError("PEPPOL_INVALID_COMPRESSION", 
                            $"Invalid CompressionType. Expected: {_config.CompressionType}, Found: {value}");
                    }
                }
            }
        }

        private void ValidateSignalMessage(XDocument soapMessage, ValidationResult result)
        {
            var signalMessage = soapMessage.Descendants(EB + "SignalMessage").FirstOrDefault();
            
            if (signalMessage == null)
            {
                result.AddError("EBMS3_MISSING_SIGNALMESSAGE", "SignalMessage element is missing");
                return;
            }

            // Validate MessageInfo
            var messageInfo = signalMessage.Element(EB + "MessageInfo");
            if (messageInfo == null)
            {
                result.AddError("EBMS3_MISSING_SIGNAL_MESSAGEINFO", "SignalMessage MessageInfo is missing");
                return;
            }

            // Validate RefToMessageId for receipts
            var receipt = signalMessage.Element(EB + "Receipt");
            if (receipt != null)
            {
                var refToMessageId = messageInfo.Element(EB + "RefToMessageId");
                if (refToMessageId == null)
                {
                    result.AddError("EBMS3_MISSING_REFTOMESSAGEID", "Receipt must have RefToMessageId");
                }
            }
        }

        private void ValidateWsSecurityHeader(XDocument soapMessage, ValidationResult result)
        {
            var security = soapMessage.Descendants(WSSE + "Security").FirstOrDefault();
            
            if (security == null)
            {
                result.AddError("WSS_MISSING_SECURITY", "WS-Security header is missing");
                return;
            }

            // Validate Timestamp
            ValidateWsSecurityTimestamp(security, result);

            // Validate BinarySecurityToken
            ValidateWsSecurityBinarySecurityToken(security, result);

            // Validate Signature
            ValidateWsSecuritySignature(security, result);
        }

        private void ValidateWsSecurityTimestamp(XElement security, ValidationResult result)
        {
            var timestamp = security.Descendants(WSU + "Timestamp").FirstOrDefault();
            if (timestamp == null)
            {
                result.AddError("WSS_MISSING_TIMESTAMP", "WS-Security Timestamp is missing");
                return;
            }

            var created = timestamp.Element(WSU + "Created");
            var expires = timestamp.Element(WSU + "Expires");

            if (created == null)
            {
                result.AddError("WSS_MISSING_TIMESTAMP_CREATED", "Timestamp Created is missing");
            }

            if (expires == null)
            {
                result.AddWarning("WSS_MISSING_TIMESTAMP_EXPIRES", "Timestamp Expires is missing");
            }
        }

        private void ValidateWsSecurityBinarySecurityToken(XElement security, ValidationResult result)
        {
            var bst = security.Descendants(WSSE + "BinarySecurityToken").FirstOrDefault();
            if (bst == null)
            {
                result.AddError("WSS_MISSING_BST", "BinarySecurityToken is missing");
                return;
            }

            var valueType = bst.Attribute("ValueType")?.Value;
            var expectedValueType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";
            
            if (valueType != expectedValueType)
            {
                result.AddError("WSS_INVALID_BST_VALUETYPE", 
                    $"Invalid BinarySecurityToken ValueType. Expected: {expectedValueType}, Found: {valueType}");
            }

            var encodingType = bst.Attribute("EncodingType")?.Value;
            var expectedEncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
            
            if (encodingType != expectedEncodingType)
            {
                result.AddError("WSS_INVALID_BST_ENCODINGTYPE", 
                    $"Invalid BinarySecurityToken EncodingType. Expected: {expectedEncodingType}, Found: {encodingType}");
            }
        }

        private void ValidateWsSecuritySignature(XElement security, ValidationResult result)
        {
            var signature = security.Descendants(DS + "Signature").FirstOrDefault();
            if (signature == null)
            {
                result.AddError("WSS_MISSING_SIGNATURE", "XML Signature is missing");
                return;
            }

            // Validate SignatureMethod
            var signatureMethod = signature.Descendants(DS + "SignatureMethod").FirstOrDefault();
            if (signatureMethod != null)
            {
                var algorithm = signatureMethod.Attribute("Algorithm")?.Value;
                if (algorithm != _config.SignatureAlgorithm)
                {
                    result.AddError("WSS_INVALID_SIGNATURE_ALGORITHM", 
                        $"Invalid signature algorithm. Expected: {_config.SignatureAlgorithm}, Found: {algorithm}");
                }
            }

            // Validate DigestMethod
            var digestMethods = signature.Descendants(DS + "DigestMethod").ToList();
            foreach (var digestMethod in digestMethods)
            {
                var algorithm = digestMethod.Attribute("Algorithm")?.Value;
                if (algorithm != _config.HashFunction)
                {
                    result.AddError("WSS_INVALID_DIGEST_ALGORITHM", 
                        $"Invalid digest algorithm. Expected: {_config.HashFunction}, Found: {algorithm}");
                }
            }
        }

        private void ValidatePeppolRequirements(XDocument soapMessage, ValidationResult result)
        {
            // Validate that message conforms to Peppol-specific requirements
            
            // 1. Check for proper SOAP version (1.2)
            var envelope = soapMessage.Root;
            if (envelope?.Name.Namespace != S12)
            {
                result.AddError("PEPPOL_INVALID_SOAP_VERSION", "Peppol requires SOAP 1.2");
            }

            // 2. Validate that required Peppol P-Mode parameters are satisfied
            // This would be checked during actual message processing with P-Mode configuration

            // 3. Validate message structure for Peppol AS4 profile
            // Empty SOAP body (payloads in attachments)
            var body = envelope?.Element(S12 + "Body");
            if (body?.HasElements == true && body.Elements().Any(e => e.Name.LocalName != "Fault"))
            {
                result.AddError("PEPPOL_SOAP_BODY_NOT_EMPTY", "Peppol AS4 requires empty SOAP body (payloads must be in MIME attachments)");
            }
        }
    }

    /// <summary>
    /// Represents the result of AS4 message validation
    /// </summary>
    public class ValidationResult
    {
        public List<ValidationError> Errors { get; } = new List<ValidationError>();
        public List<ValidationError> Warnings { get; } = new List<ValidationError>();

        public bool IsValid => Errors.Count == 0;

        public void AddError(string code, string message)
        {
            Errors.Add(new ValidationError { Code = code, Message = message, Severity = "Error" });
        }

        public void AddWarning(string code, string message)
        {
            Warnings.Add(new ValidationError { Code = code, Message = message, Severity = "Warning" });
        }

        public override string ToString()
        {
            var result = $"Validation Result: {(IsValid ? "VALID" : "INVALID")}\n";
            
            if (Errors.Any())
            {
                result += $"Errors ({Errors.Count}):\n";
                result += string.Join("\n", Errors.Select(e => $"  {e.Code}: {e.Message}"));
            }

            if (Warnings.Any())
            {
                result += $"\nWarnings ({Warnings.Count}):\n";
                result += string.Join("\n", Warnings.Select(w => $"  {w.Code}: {w.Message}"));
            }

            return result;
        }
    }

    /// <summary>
    /// Represents a validation error or warning
    /// </summary>
    public class ValidationError
    {
        public string Code { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
    }
} 