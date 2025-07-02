using System;
using System.Runtime.Serialization;

namespace PeppolSG.API.Models
{
    /// <summary>
    /// Base exception for all Peppol-related errors with correlation ID support
    /// </summary>
    [Serializable]
    public abstract class PeppolException : Exception
    {
        public string CorrelationId { get; }
        public string ErrorCode { get; }

        protected PeppolException(string message, string correlationId = null, string errorCode = null) 
            : base(message)
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N");
            ErrorCode = errorCode;
        }

        protected PeppolException(string message, Exception innerException, string correlationId = null, string errorCode = null) 
            : base(message, innerException)
        {
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("N");
            ErrorCode = errorCode;
        }

        protected PeppolException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            CorrelationId = info.GetString("CorrelationId");
            ErrorCode = info.GetString("ErrorCode");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("CorrelationId", CorrelationId);
            info.AddValue("ErrorCode", ErrorCode);
        }
    }

    /// <summary>
    /// Exception for AS4 message validation failures
    /// </summary>
    [Serializable]
    public class PeppolAs4ValidationException : PeppolException
    {
        public string MessageId { get; }

        public PeppolAs4ValidationException(string message, string correlationId = null, string messageId = null, string errorCode = "EBMS:0004") 
            : base(message, correlationId, errorCode)
        {
            MessageId = messageId;
        }

        public PeppolAs4ValidationException(string message, Exception innerException, string correlationId = null, string messageId = null, string errorCode = "EBMS:0004") 
            : base(message, innerException, correlationId, errorCode)
        {
            MessageId = messageId;
        }

        protected PeppolAs4ValidationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            MessageId = info.GetString("MessageId");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("MessageId", MessageId);
        }
    }

    /// <summary>
    /// Exception for AS4 security-related failures (certificates, signatures, etc.)
    /// </summary>
    [Serializable]
    public class PeppolAs4SecurityException : PeppolException
    {
        public string CertificateThumbprint { get; }

        public PeppolAs4SecurityException(string message, string correlationId = null, string certificateThumbprint = null, string errorCode = "EBMS:0101") 
            : base(message, correlationId, errorCode)
        {
            CertificateThumbprint = certificateThumbprint;
        }

        public PeppolAs4SecurityException(string message, Exception innerException, string correlationId = null, string certificateThumbprint = null, string errorCode = "EBMS:0101") 
            : base(message, innerException, correlationId, errorCode)
        {
            CertificateThumbprint = certificateThumbprint;
        }

        protected PeppolAs4SecurityException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            CertificateThumbprint = info.GetString("CertificateThumbprint");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("CertificateThumbprint", CertificateThumbprint);
        }
    }

    /// <summary>
    /// Exception for SMP lookup failures
    /// </summary>
    [Serializable]
    public class PeppolSmpException : PeppolException
    {
        public string ParticipantId { get; }
        public string SmpUrl { get; }

        public PeppolSmpException(string message, string correlationId = null, string participantId = null, string smpUrl = null, string errorCode = "EBMS:0010") 
            : base(message, correlationId, errorCode)
        {
            ParticipantId = participantId;
            SmpUrl = smpUrl;
        }

        public PeppolSmpException(string message, Exception innerException, string correlationId = null, string participantId = null, string smpUrl = null, string errorCode = "EBMS:0010") 
            : base(message, innerException, correlationId, errorCode)
        {
            ParticipantId = participantId;
            SmpUrl = smpUrl;
        }

        protected PeppolSmpException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            ParticipantId = info.GetString("ParticipantId");
            SmpUrl = info.GetString("SmpUrl");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("ParticipantId", ParticipantId);
            info.AddValue("SmpUrl", SmpUrl);
        }
    }

    /// <summary>
    /// Exception for configuration-related errors
    /// </summary>
    [Serializable]
    public class PeppolConfigurationException : PeppolException
    {
        public string ConfigurationKey { get; }

        public PeppolConfigurationException(string message, string correlationId = null, string configurationKey = null, string errorCode = "CONFIG:0001") 
            : base(message, correlationId, errorCode)
        {
            ConfigurationKey = configurationKey;
        }

        public PeppolConfigurationException(string message, Exception innerException, string correlationId = null, string configurationKey = null, string errorCode = "CONFIG:0001") 
            : base(message, innerException, correlationId, errorCode)
        {
            ConfigurationKey = configurationKey;
        }

        protected PeppolConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            ConfigurationKey = info.GetString("ConfigurationKey");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("ConfigurationKey", ConfigurationKey);
        }
    }
} 