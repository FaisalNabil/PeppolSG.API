using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using log4net;

namespace PeppolSG.API.Service
{
    /// <summary>
    /// Message ID manager that provides thread-safe duplicate detection
    /// with both in-memory and persistent storage for AS4 message deduplication
    /// </summary>
    public class MessageIdManager : IMessageIdManager, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MessageIdManager));
        
        // In-memory storage for fast lookups
        private readonly ConcurrentDictionary<string, MessageIdEntry> _messageIds;
        
        // Configuration
        private readonly string _persistentStoragePath;
        private readonly TimeSpan _messageIdTtl;
        private readonly TimeSpan _cleanupInterval;
        private readonly int _maxInMemoryEntries;
        private readonly bool _persistentStorageEnabled;
        
        // Cleanup timer
        private readonly Timer _cleanupTimer;
        private readonly object _persistenceLock = new object();
        
        // Statistics
        private long _totalMessagesProcessed;
        private long _duplicatesDetected;
        private long _expiredMessagesRemoved;
        
        public MessageIdManager()
        {
            _messageIds = new ConcurrentDictionary<string, MessageIdEntry>();
            
            // Load configuration
            _persistentStoragePath = ConfigurationManager.AppSettings["MessageIdStoragePath"] ?? 
                                   Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MessageIds");
            
            _messageIdTtl = TimeSpan.FromHours(
                int.Parse(ConfigurationManager.AppSettings["MessageIdTtlHours"] ?? "24"));
            
            _cleanupInterval = TimeSpan.FromMinutes(
                int.Parse(ConfigurationManager.AppSettings["MessageIdCleanupIntervalMinutes"] ?? "30"));
            
            _maxInMemoryEntries = int.Parse(
                ConfigurationManager.AppSettings["MaxInMemoryMessageIds"] ?? "100000");
            
            _persistentStorageEnabled = bool.Parse(
                ConfigurationManager.AppSettings["MessageIdPersistentStorageEnabled"] ?? "true");
            
            // Initialize storage
            InitializeStorage();
            
            // Load existing message IDs from persistent storage
            LoadFromPersistentStorage();
            
            // Start cleanup timer
            _cleanupTimer = new Timer(state => PerformCleanup(), null, (int)_cleanupInterval.TotalMilliseconds, (int)_cleanupInterval.TotalMilliseconds);
            
            log.Info($"MessageIdManager initialized: TTL={_messageIdTtl}, CleanupInterval={_cleanupInterval}, " +
                    $"MaxEntries={_maxInMemoryEntries}, PersistentStorage={_persistentStorageEnabled}");
        }

        /// <summary>
        /// Checks if a message ID is a duplicate and registers it if not
        /// </summary>
        /// <param name="messageId">The message ID to check</param>
        /// <param name="source">Source of the message (for logging)</param>
        /// <returns>True if the message is a duplicate, false if it's new</returns>
        public bool IsDuplicate(string messageId, string source = null)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty", nameof(messageId));
            }

            Interlocked.Increment(ref _totalMessagesProcessed);
            
            var now = DateTime.UtcNow;
            var newEntry = new MessageIdEntry
            {
                MessageId = messageId,
                FirstSeen = now,
                LastSeen = now,
                Source = source ?? "Unknown",
                HitCount = 1
            };

            // Try to add the new entry - if it already exists, it's a duplicate
            var existingEntry = _messageIds.AddOrUpdate(
                messageId,
                newEntry,
                (key, existing) =>
                {
                    // Update existing entry
                    existing.LastSeen = now;
                    existing.HitCount++;
                    return existing;
                });

            bool isDuplicate = existingEntry != newEntry;
            
            if (isDuplicate)
            {
                Interlocked.Increment(ref _duplicatesDetected);
                log.Warn($"Duplicate message detected: {messageId} from {source}. " +
                        $"First seen: {existingEntry.FirstSeen}, Hit count: {existingEntry.HitCount}");
            }
            else
            {
                log.Debug($"New message registered: {messageId} from {source}");
                
                // Persist to storage if enabled
                if (_persistentStorageEnabled)
                {
                    Task.Run(() => PersistMessageId(newEntry));
                }
                
                // Check if we need to perform in-memory cleanup
                if (_messageIds.Count > _maxInMemoryEntries * 1.1) // 10% buffer
                {
                    Task.Run(() => PerformInMemoryCleanup());
                }
            }

            return isDuplicate;
        }

        /// <summary>
        /// Validates message ID format according to RFC 2822 and ebMS 3.0 requirements
        /// </summary>
        /// <param name="messageId">Message ID to validate</param>
        /// <exception cref="ArgumentException">Thrown when message ID is invalid</exception>
        public void ValidateMessageIdFormat(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                throw new ArgumentException("Message ID cannot be null or empty");
            }

            // Basic length check (reasonable limits)
            if (messageId.Length > 255)
            {
                throw new ArgumentException($"Message ID too long: {messageId.Length} characters (max 255)");
            }

            if (messageId.Length < 3)
            {
                throw new ArgumentException($"Message ID too short: {messageId.Length} characters (min 3)");
            }

            // Check for required @ symbol (RFC 2822 Message-ID format)
            if (!messageId.Contains("@"))
            {
                log.Warn($"Message ID does not contain '@' symbol (non-standard): {messageId}");
                // Don't throw - some systems may use non-standard formats
            }

            // Check for prohibited characters
            var prohibitedChars = new char[] { '<', '>', '"', '\r', '\n', '\t' };
            if (messageId.Any(c => prohibitedChars.Contains(c)))
            {
                throw new ArgumentException($"Message ID contains prohibited characters: {messageId}");
            }

            // Check for control characters
            if (messageId.Any(c => char.IsControl(c)))
            {
                throw new ArgumentException($"Message ID contains control characters: {messageId}");
            }

            log.Debug($"Message ID format validation passed: {messageId}");
        }

        /// <summary>
        /// Gets statistics about message ID processing
        /// </summary>
        public MessageIdStatistics GetStatistics()
        {
            return new MessageIdStatistics
            {
                TotalMessagesProcessed = _totalMessagesProcessed,
                DuplicatesDetected = _duplicatesDetected,
                ExpiredMessagesRemoved = _expiredMessagesRemoved,
                CurrentInMemoryCount = _messageIds.Count,
                MaxInMemoryEntries = _maxInMemoryEntries,
                MessageIdTtl = _messageIdTtl,
                CleanupInterval = _cleanupInterval,
                PersistentStorageEnabled = _persistentStorageEnabled
            };
        }

        /// <summary>
        /// Gets all message IDs (for debugging/monitoring)
        /// </summary>
        /// <param name="includeExpired">Whether to include expired entries</param>
        public IEnumerable<MessageIdEntry> GetAllMessageIds(bool includeExpired = false)
        {
            var now = DateTime.UtcNow;
            return _messageIds.Values
                .Where(entry => includeExpired || (now - entry.LastSeen) <= _messageIdTtl)
                .OrderByDescending(entry => entry.LastSeen)
                .ToList();
        }

        /// <summary>
        /// Manually triggers cleanup of expired message IDs
        /// </summary>
        public int CleanupExpiredMessages()
        {
            log.Info("Manual cleanup of expired message IDs triggered");
            return PerformCleanup();
        }

        /// <summary>
        /// Clears all message IDs (for testing/maintenance)
        /// </summary>
        public void ClearAllMessageIds()
        {
            log.Warn("Clearing all message IDs");
            _messageIds.Clear();
            
            if (_persistentStorageEnabled)
            {
                ClearPersistentStorage();
            }
            
            // Reset statistics
            Interlocked.Exchange(ref _totalMessagesProcessed, 0);
            Interlocked.Exchange(ref _duplicatesDetected, 0);
            Interlocked.Exchange(ref _expiredMessagesRemoved, 0);
        }

        #region Private Methods

        /// <summary>
        /// Initializes storage directories and files
        /// </summary>
        private void InitializeStorage()
        {
            if (_persistentStorageEnabled)
            {
                try
                {
                    if (!Directory.Exists(_persistentStoragePath))
                    {
                        Directory.CreateDirectory(_persistentStoragePath);
                        log.Info($"Created message ID storage directory: {_persistentStoragePath}");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to initialize persistent storage: {ex.Message}", ex);
                    // Continue without persistent storage
                }
            }
        }

        /// <summary>
        /// Loads message IDs from persistent storage
        /// </summary>
        private void LoadFromPersistentStorage()
        {
            if (!_persistentStorageEnabled)
                return;

            try
            {
                var files = Directory.GetFiles(_persistentStoragePath, "messageids_*.json")
                    .OrderByDescending(f => f);

                var loadedCount = 0;
                var now = DateTime.UtcNow;

                foreach (var file in files.Take(5)) // Load only recent files
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var entries = JsonSerializer.Deserialize<List<MessageIdEntry>>(json);

                        foreach (var entry in entries)
                        {
                            // Only load non-expired entries
                            if ((now - entry.LastSeen) <= _messageIdTtl)
                            {
                                _messageIds.TryAdd(entry.MessageId, entry);
                                loadedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Failed to load message IDs from {file}: {ex.Message}");
                    }
                }

                log.Info($"Loaded {loadedCount} message IDs from persistent storage");
            }
            catch (Exception ex)
            {
                log.Error($"Error loading from persistent storage: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Persists a message ID entry to storage
        /// </summary>
        private void PersistMessageId(MessageIdEntry entry)
        {
            if (!_persistentStorageEnabled)
                return;

            try
            {
                lock (_persistenceLock)
                {
                    var fileName = $"messageids_{DateTime.UtcNow:yyyyMMdd_HH}.json";
                    var filePath = Path.Combine(_persistentStoragePath, fileName);
                    
                    var entries = new List<MessageIdEntry>();
                    
                    // Load existing entries if file exists
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            var existingJson = File.ReadAllText(filePath);
                            entries = JsonSerializer.Deserialize<List<MessageIdEntry>>(existingJson) ?? new List<MessageIdEntry>();
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"Failed to read existing entries from {filePath}: {ex.Message}");
                        }
                    }
                    
                    // Add new entry
                    entries.Add(entry);
                    
                    // Write back to file
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to persist message ID {entry.MessageId}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs periodic cleanup of expired message IDs
        /// </summary>
        private int PerformCleanup()
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredEntries = new List<string>();

                // Find expired entries
                foreach (var kvp in _messageIds)
                {
                    if ((now - kvp.Value.LastSeen) > _messageIdTtl)
                    {
                        expiredEntries.Add(kvp.Key);
                    }
                }

                // Remove expired entries
                var removedCount = 0;
                foreach (var messageId in expiredEntries)
                {
                    if (_messageIds.TryRemove(messageId, out _))
                    {
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    Interlocked.Add(ref _expiredMessagesRemoved, removedCount);
                    log.Info($"Cleaned up {removedCount} expired message IDs. Current count: {_messageIds.Count}");
                }

                // Cleanup old persistent storage files
                if (_persistentStorageEnabled)
                {
                    CleanupOldPersistentFiles();
                }

                return removedCount;
            }
            catch (Exception ex)
            {
                log.Error("Error during message ID cleanup", ex);
                return 0;
            }
        }

        /// <summary>
        /// Performs in-memory cleanup when approaching limits
        /// </summary>
        private void PerformInMemoryCleanup()
        {
            try
            {
                if (_messageIds.Count <= _maxInMemoryEntries)
                    return;

                log.Info($"Performing in-memory cleanup. Current count: {_messageIds.Count}");

                // Remove oldest entries that haven't been hit recently
                var now = DateTime.UtcNow;
                var halfTtl = TimeSpan.FromTicks((long)(_messageIdTtl.Ticks * 0.5));
                var toRemove = _messageIds.Values
                    .Where(entry => (now - entry.LastSeen) > halfTtl) // Remove entries older than half TTL
                    .OrderBy(entry => entry.LastSeen)
                    .Take(_messageIds.Count - _maxInMemoryEntries)
                    .Select(entry => entry.MessageId)
                    .ToList();

                var removedCount = 0;
                foreach (var messageId in toRemove)
                {
                    if (_messageIds.TryRemove(messageId, out _))
                    {
                        removedCount++;
                    }
                }

                log.Info($"In-memory cleanup removed {removedCount} entries. New count: {_messageIds.Count}");
            }
            catch (Exception ex)
            {
                log.Error("Error during in-memory cleanup", ex);
            }
        }

        /// <summary>
        /// Cleans up old persistent storage files
        /// </summary>
        private void CleanupOldPersistentFiles()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.Subtract(_messageIdTtl);
                var files = Directory.GetFiles(_persistentStoragePath, "messageids_*.json");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffDate)
                    {
                        File.Delete(file);
                        log.Debug($"Deleted old message ID file: {fileInfo.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Warn($"Error cleaning up old persistent files: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all persistent storage
        /// </summary>
        private void ClearPersistentStorage()
        {
            try
            {
                var files = Directory.GetFiles(_persistentStoragePath, "messageids_*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                log.Info("Cleared all persistent storage files");
            }
            catch (Exception ex)
            {
                log.Error($"Error clearing persistent storage: {ex.Message}", ex);
            }
        }

        #endregion

        #region IDisposable Implementation

        private bool _disposed = false;

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
                    _cleanupTimer?.Dispose();
                    
                    // Perform final cleanup
                    PerformCleanup();
                    
                    log.Info($"MessageIdManager disposed. Final statistics: " +
                            $"Processed={_totalMessagesProcessed}, Duplicates={_duplicatesDetected}, " +
                            $"Expired={_expiredMessagesRemoved}, InMemory={_messageIds.Count}");
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Interface for message ID management
    /// </summary>
    public interface IMessageIdManager
    {
        bool IsDuplicate(string messageId, string source = null);
        void ValidateMessageIdFormat(string messageId);
        MessageIdStatistics GetStatistics();
        IEnumerable<MessageIdEntry> GetAllMessageIds(bool includeExpired = false);
        int CleanupExpiredMessages();
        void ClearAllMessageIds();
    }

    /// <summary>
    /// Represents a message ID entry with tracking information
    /// </summary>
    public class MessageIdEntry
    {
        public string MessageId { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string Source { get; set; }
        public int HitCount { get; set; }
    }

    /// <summary>
    /// Statistics about message ID processing
    /// </summary>
    public class MessageIdStatistics
    {
        public long TotalMessagesProcessed { get; set; }
        public long DuplicatesDetected { get; set; }
        public long ExpiredMessagesRemoved { get; set; }
        public int CurrentInMemoryCount { get; set; }
        public int MaxInMemoryEntries { get; set; }
        public TimeSpan MessageIdTtl { get; set; }
        public TimeSpan CleanupInterval { get; set; }
        public bool PersistentStorageEnabled { get; set; }
        
        public double DuplicateRate => TotalMessagesProcessed > 0 ? 
            (double)DuplicatesDetected / TotalMessagesProcessed * 100 : 0;
    }
} 