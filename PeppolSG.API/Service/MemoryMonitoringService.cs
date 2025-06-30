using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Web.Hosting;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Periodically checks process memory usage and logs warnings / critical alerts.
    /// </summary>
    public class MemoryMonitoringService : IRegisteredObject, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MemoryMonitoringService));
        private static MemoryMonitoringService _instance;
        private static readonly object _lock = new object();

        private readonly Timer _timer;
        private readonly TimeSpan _interval;
        private readonly long _warnThresholdBytes;
        private readonly long _criticalThresholdBytes;
        private bool _disposed;
        private bool _shuttingDown;

        private MemoryMonitoringService()
        {
            _interval = TimeSpan.FromSeconds(int.Parse(ConfigurationManager.AppSettings["MemoryCheckIntervalSec"] ?? "60"));
            var warnMb = int.Parse(ConfigurationManager.AppSettings["MemoryWarnThresholdMb"] ?? "256");
            var critMb = int.Parse(ConfigurationManager.AppSettings["MemoryCriticalThresholdMb"] ?? "512");
            _warnThresholdBytes = warnMb * 1024L * 1024L;
            _criticalThresholdBytes = critMb * 1024L * 1024L;

            _timer = new Timer(CheckMemory, null, _interval, _interval);
            HostingEnvironment.RegisterObject(this);
            log.Info($"MemoryMonitoringService started – interval {_interval.TotalSeconds}s, warn {warnMb} MB, critical {critMb} MB");
        }

        public static void Initialize()
        {
            lock (_lock)
            {
                _instance ??= new MemoryMonitoringService();
            }
        }

        private void CheckMemory(object state)
        {
            if (_shuttingDown) return;

            var proc = Process.GetCurrentProcess();
            long privateBytes = proc.PrivateMemorySize64;
            string msg = $"Memory usage: {privateBytes / (1024 * 1024):N0} MB";

            if (privateBytes >= _criticalThresholdBytes)
            {
                log.Error("[MEMORY-CRITICAL] " + msg);
            }
            else if (privateBytes >= _warnThresholdBytes)
            {
                log.Warn("[MEMORY-WARN] " + msg);
            }
            else
            {
                log.Debug(msg);
            }
        }

        #region IRegisteredObject
        public void Stop(bool immediate)
        {
            _shuttingDown = true;
            _timer?.Dispose();
            HostingEnvironment.UnregisterObject(this);
        }
        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Dispose();
                _disposed = true;
            }
        }
    }
} 