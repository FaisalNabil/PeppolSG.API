using System;
using log4net;
using Newtonsoft.Json;

namespace PeppolSG.API.Logging
{
    public static class AuditLogService
    {
        private static readonly ILog audit = LogManager.GetLogger("Audit");

        public static void Record(string action, object subject, object data = null)
        {
            var correlationId = log4net.ThreadContext.Properties["corr"]?.ToString();
            var payload = new
            {
                timestampUtc = DateTime.UtcNow,
                correlationId,
                action,
                subject,
                data
            };
            audit.Info(JsonConvert.SerializeObject(payload));
        }
    }
} 