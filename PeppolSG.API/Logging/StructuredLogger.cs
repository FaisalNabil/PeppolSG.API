using System;
using log4net;
using Newtonsoft.Json;

namespace PeppolSG.API.Logging
{
    /// <summary>
    /// Wraps log4net, emitting JSON strings with correlation and structured extra data.
    /// </summary>
    public static class StructuredLogger
    {
        private static readonly ILog log = LogManager.GetLogger("Structured");

        private static string Build(object level, string message, object data)
        {
            var correlationId = log4net.ThreadContext.Properties["corr"]?.ToString();
            var payload = new
            {
                level,
                timestampUtc = DateTime.UtcNow,
                correlationId,
                message,
                data
            };
            return JsonConvert.SerializeObject(payload);
        }

        public static void Info(string message, object data = null) => log.Info(Build("INFO", message, data));
        public static void Warn(string message, object data = null) => log.Warn(Build("WARN", message, data));
        public static void Error(string message, object data = null, Exception ex = null)
        {
            var json = Build("ERROR", message, data);
            if (ex != null) log.Error(json, ex);
            else log.Error(json);
        }
    }
} 