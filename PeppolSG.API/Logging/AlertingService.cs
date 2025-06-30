using System;
using System.Configuration;

namespace PeppolSG.API.Logging
{
    public static class AlertingService
    {
        private static readonly string alertEmail = ConfigurationManager.AppSettings["AlertEmail"];

        public static void SendCritical(string subject, string body)
        {
            // For now, just log to structured logger; placeholder for SMTP/integrations
            StructuredLogger.Error($"ALERT: {subject}", new { Body = body });
            // TODO: integrate with email/SNS etc.
        }
    }
} 