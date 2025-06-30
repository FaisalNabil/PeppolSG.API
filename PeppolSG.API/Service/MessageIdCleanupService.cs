using System;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Background service for automatic message ID cleanup and maintenance
    /// Implements IRegisteredObject to ensure proper cleanup during application shutdown
    /// </summary>
    public class MessageIdCleanupService : IRegisteredObject, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MessageIdCleanupService));
        private static MessageIdCleanupService _instance;
        private static readonly object _lock = new object();

        private readonly IMessageIdManager _messageIdManager;
        private readonly Timer _maintenanceTimer;
        private readonly Timer _statisticsTimer;
        private readonly TimeSpan _maintenanceInterval;
        private readonly TimeSpan _statisticsInterval;
        private bool _isShuttingDown = false;
        private bool _disposed = false;

        private MessageIdCleanupService()
        {
            _messageIdManager = new MessageIdManager();
            
            // Maintenance runs every 30 minutes
            _maintenanceInterval = TimeSpan.FromMinutes(30);
            
            // Statistics logging every 15 minutes  
            _statisticsInterval = TimeSpan.FromMinutes(15);

            // Start timers
            _maintenanceTimer = new Timer(PerformMaintenance, null, _maintenanceInterval, _maintenanceInterval);
            _statisticsTimer = new Timer(LogStatistics, null, _statisticsInterval, _statisticsInterval);

            // Register with hosting environment for graceful shutdown
            HostingEnvironment.RegisterObject(this);

            log.Info($"MessageIdCleanupService started with maintenance interval: {_maintenanceInterval}");
        }

        /// <summary>
        /// Gets the singleton instance of the cleanup service
        /// </summary>
        public static MessageIdCleanupService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MessageIdCleanupService();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Gets the message ID manager instance
        /// </summary>
        public IMessageIdManager MessageIdManager => _messageIdManager;

        /// <summary>
        /// Initializes the cleanup service (called from Global.asax)
        /// </summary>
        public static void Initialize()
        {
            try
            {
                var instance = Instance;
                log.Info("MessageIdCleanupService initialized successfully");
            }
            catch (Exception ex)
            {
                log.Error("Failed to initialize MessageIdCleanupService", ex);
                throw;
            }
        }

        /// <summary>
        /// Performs maintenance tasks
        /// </summary>
        private void PerformMaintenance(object state)
        {
            if (_isShuttingDown)
                return;

            try
            {
                log.Debug("Starting message ID maintenance");

                // Cleanup expired message IDs
                var removedCount = _messageIdManager.CleanupExpiredMessages();
                
                if (removedCount > 0)
                {
                    log.Info($"Maintenance completed: removed {removedCount} expired message IDs");
                }
                else
                {
                    log.Debug("Maintenance completed: no expired message IDs found");
                }

                // Force garbage collection if we removed a significant number of items
                if (removedCount > 1000)
                {
                    log.Debug("Forcing garbage collection after large cleanup");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
            }
            catch (Exception ex)
            {
                log.Error("Error during message ID maintenance", ex);
            }
        }

        /// <summary>
        /// Logs statistics about message ID processing
        /// </summary>
        private void LogStatistics(object state)
        {
            if (_isShuttingDown)
                return;

            try
            {
                var stats = _messageIdManager.GetStatistics();
                
                log.Info($"Message ID Statistics: " +
                        $"Processed={stats.TotalMessagesProcessed}, " +
                        $"Duplicates={stats.DuplicatesDetected} ({stats.DuplicateRate:F2}%), " +
                        $"Expired={stats.ExpiredMessagesRemoved}, " +
                        $"InMemory={stats.CurrentInMemoryCount}/{stats.MaxInMemoryEntries}");

                // Check for potential memory issues
                if (stats.CurrentInMemoryCount > stats.MaxInMemoryEntries * 0.8)
                {
                    log.Warn($"Message ID memory usage high: {stats.CurrentInMemoryCount}/{stats.MaxInMemoryEntries} " +
                            $"({(double)stats.CurrentInMemoryCount / stats.MaxInMemoryEntries * 100:F1}%)");
                }

                // Check duplicate rate
                if (stats.DuplicateRate > 10) // More than 10% duplicates might indicate an issue
                {
                    log.Warn($"High duplicate rate detected: {stats.DuplicateRate:F2}%");
                }
            }
            catch (Exception ex)
            {
                log.Error("Error logging message ID statistics", ex);
            }
        }

        /// <summary>
        /// Performs immediate maintenance (for manual triggers)
        /// </summary>
        public void PerformImmediateMaintenance()
        {
            log.Info("Immediate maintenance requested");
            Task.Run(() => PerformMaintenance(null));
        }

        /// <summary>
        /// Gets current statistics
        /// </summary>
        public MessageIdStatistics GetCurrentStatistics()
        {
            return _messageIdManager.GetStatistics();
        }

        #region IRegisteredObject Implementation

        public void Stop(bool immediate)
        {
            log.Info($"MessageIdCleanupService stopping (immediate: {immediate})");
            _isShuttingDown = true;

            if (!immediate)
            {
                // Perform final maintenance
                try
                {
                    PerformMaintenance(null);
                    log.Info("Final maintenance completed during shutdown");
                }
                catch (Exception ex)
                {
                    log.Error("Error during final maintenance", ex);
                }
            }

            // Unregister from hosting environment
            HostingEnvironment.UnregisterObject(this);
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _isShuttingDown = true;

                    _maintenanceTimer?.Dispose();
                    _statisticsTimer?.Dispose();

                    if (_messageIdManager is IDisposable disposableManager)
                    {
                        disposableManager.Dispose();
                    }

                    log.Info("MessageIdCleanupService disposed");
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Static helper class for accessing message ID services
    /// </summary>
    public static class MessageIdServices
    {
        /// <summary>
        /// Gets the global message ID manager instance
        /// </summary>
        public static IMessageIdManager GetManager()
        {
            return MessageIdCleanupService.Instance.MessageIdManager;
        }

        /// <summary>
        /// Gets current message ID statistics
        /// </summary>
        public static MessageIdStatistics GetStatistics()
        {
            return MessageIdCleanupService.Instance.GetCurrentStatistics();
        }

        /// <summary>
        /// Triggers immediate maintenance
        /// </summary>
        public static void TriggerMaintenance()
        {
            MessageIdCleanupService.Instance.PerformImmediateMaintenance();
        }
    }
} 