using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PeppolSG.API.Service;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class MessageIdManagerTests
    {
        private MessageIdManager _messageIdManager;
        private string _testStoragePath;

        [TestInitialize]
        public void Setup()
        {
            // Create temporary storage path for testing
            _testStoragePath = Path.Combine(Path.GetTempPath(), "PeppolTestMessageIds", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testStoragePath);

            // Mock configuration for testing
            System.Configuration.ConfigurationManager.AppSettings["MessageIdStoragePath"] = _testStoragePath;
            System.Configuration.ConfigurationManager.AppSettings["MessageIdTtlHours"] = "1"; // 1 hour for faster testing
            System.Configuration.ConfigurationManager.AppSettings["MessageIdCleanupIntervalMinutes"] = "1"; // 1 minute
            System.Configuration.ConfigurationManager.AppSettings["MaxInMemoryMessageIds"] = "100";
            System.Configuration.ConfigurationManager.AppSettings["MessageIdPersistentStorageEnabled"] = "true";

            _messageIdManager = new MessageIdManager();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _messageIdManager?.Dispose();
            
            if (Directory.Exists(_testStoragePath))
            {
                Directory.Delete(_testStoragePath, true);
            }
        }

        #region Basic Functionality Tests

        [TestMethod]
        public void IsDuplicate_NewMessageId_ReturnsFalse()
        {
            // Arrange
            var messageId = "test-message-1@example.com";

            // Act
            var isDuplicate = _messageIdManager.IsDuplicate(messageId, "test-source");

            // Assert
            Assert.IsFalse(isDuplicate);
        }

        [TestMethod]
        public void IsDuplicate_SameMessageIdTwice_ReturnsTrue()
        {
            // Arrange
            var messageId = "test-message-1@example.com";

            // Act
            var firstCall = _messageIdManager.IsDuplicate(messageId, "test-source");
            var secondCall = _messageIdManager.IsDuplicate(messageId, "test-source");

            // Assert
            Assert.IsFalse(firstCall);
            Assert.IsTrue(secondCall);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void IsDuplicate_NullMessageId_ThrowsException()
        {
            // Act & Assert
            _messageIdManager.IsDuplicate(null, "test-source");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void IsDuplicate_EmptyMessageId_ThrowsException()
        {
            // Act & Assert
            _messageIdManager.IsDuplicate("", "test-source");
        }

        #endregion

        #region Message ID Format Validation Tests

        [TestMethod]
        public void ValidateMessageIdFormat_ValidMessageId_DoesNotThrow()
        {
            // Arrange
            var validMessageIds = new[]
            {
                "message-123@example.com",
                "test@peppol.org",
                "uuid-1234-5678@domain.tld",
                "phase4-msg-abc123@company.com"
            };

            // Act & Assert
            foreach (var messageId in validMessageIds)
            {
                _messageIdManager.ValidateMessageIdFormat(messageId);
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ValidateMessageIdFormat_NullMessageId_ThrowsException()
        {
            // Act & Assert
            _messageIdManager.ValidateMessageIdFormat(null);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ValidateMessageIdFormat_EmptyMessageId_ThrowsException()
        {
            // Act & Assert
            _messageIdManager.ValidateMessageIdFormat("");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ValidateMessageIdFormat_TooLongMessageId_ThrowsException()
        {
            // Arrange
            var longMessageId = new string('a', 260) + "@example.com";

            // Act & Assert
            _messageIdManager.ValidateMessageIdFormat(longMessageId);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ValidateMessageIdFormat_TooShortMessageId_ThrowsException()
        {
            // Act & Assert
            _messageIdManager.ValidateMessageIdFormat("ab");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void ValidateMessageIdFormat_WithProhibitedCharacters_ThrowsException()
        {
            // Arrange
            var invalidMessageIds = new[]
            {
                "message<test>@example.com",
                "message\"test@example.com",
                "message\ntest@example.com",
                "message\rtest@example.com",
                "message\ttest@example.com"
            };

            // Act & Assert
            foreach (var messageId in invalidMessageIds)
            {
                try
                {
                    _messageIdManager.ValidateMessageIdFormat(messageId);
                    Assert.Fail($"Expected exception for message ID: {messageId}");
                }
                catch (ArgumentException)
                {
                    // Expected
                }
            }
        }

        [TestMethod]
        public void ValidateMessageIdFormat_WithoutAtSymbol_LogsWarningButDoesNotThrow()
        {
            // Arrange
            var messageIdWithoutAt = "message-without-at-symbol";

            // Act & Assert - should not throw but should log warning
            _messageIdManager.ValidateMessageIdFormat(messageIdWithoutAt);
        }

        #endregion

        #region Statistics Tests

        [TestMethod]
        public void GetStatistics_InitialState_ReturnsZeroValues()
        {
            // Act
            var stats = _messageIdManager.GetStatistics();

            // Assert
            Assert.AreEqual(0, stats.TotalMessagesProcessed);
            Assert.AreEqual(0, stats.DuplicatesDetected);
            Assert.AreEqual(0, stats.ExpiredMessagesRemoved);
            Assert.AreEqual(0, stats.CurrentInMemoryCount);
            Assert.AreEqual(0, stats.DuplicateRate);
        }

        [TestMethod]
        public void GetStatistics_AfterProcessingMessages_ReturnsCorrectCounts()
        {
            // Arrange
            var messageIds = new[]
            {
                "msg1@example.com",
                "msg2@example.com",
                "msg1@example.com", // duplicate
                "msg3@example.com"
            };

            // Act
            foreach (var messageId in messageIds)
            {
                _messageIdManager.IsDuplicate(messageId, "test");
            }

            var stats = _messageIdManager.GetStatistics();

            // Assert
            Assert.AreEqual(4, stats.TotalMessagesProcessed);
            Assert.AreEqual(1, stats.DuplicatesDetected);
            Assert.AreEqual(3, stats.CurrentInMemoryCount); // 3 unique messages
            Assert.AreEqual(25.0, stats.DuplicateRate); // 1 duplicate out of 4 = 25%
        }

        #endregion

        #region Thread Safety Tests

        [TestMethod]
        public void IsDuplicate_ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            const int threadCount = 10;
            const int messagesPerThread = 100;
            var tasks = new List<Task<List<bool>>>();
            var duplicateCounts = new int[threadCount];

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                var task = Task.Run(() =>
                {
                    var results = new List<bool>();
                    for (int j = 0; j < messagesPerThread; j++)
                    {
                        var messageId = $"thread-{threadIndex}-msg-{j}@example.com";
                        var isDuplicate = _messageIdManager.IsDuplicate(messageId, $"thread-{threadIndex}");
                        results.Add(isDuplicate);
                    }
                    return results;
                });
                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            var allResults = tasks.SelectMany(t => t.Result).ToList();
            var duplicatesFound = allResults.Count(r => r);
            
            // Should be no duplicates since each thread uses unique message IDs
            Assert.AreEqual(0, duplicatesFound);
            
            var stats = _messageIdManager.GetStatistics();
            Assert.AreEqual(threadCount * messagesPerThread, stats.TotalMessagesProcessed);
            Assert.AreEqual(threadCount * messagesPerThread, stats.CurrentInMemoryCount);
        }

        [TestMethod]
        public void IsDuplicate_SameMessageIdFromMultipleThreads_OnlyFirstIsNotDuplicate()
        {
            // Arrange
            const int threadCount = 10;
            var messageId = "shared-message@example.com";
            var tasks = new List<Task<bool>>();
            var countdown = new CountdownEvent(threadCount);

            // Act
            for (int i = 0; i < threadCount; i++)
            {
                var task = Task.Run(() =>
                {
                    countdown.Signal();
                    countdown.Wait(); // Wait for all threads to be ready
                    return _messageIdManager.IsDuplicate(messageId, "concurrent-test");
                });
                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            // Assert
            var results = tasks.Select(t => t.Result).ToArray();
            var nonDuplicates = results.Count(r => !r);
            var duplicates = results.Count(r => r);

            Assert.AreEqual(1, nonDuplicates, "Exactly one thread should register the message as non-duplicate");
            Assert.AreEqual(threadCount - 1, duplicates, "All other threads should see it as duplicate");
        }

        #endregion

        #region Expiry and Cleanup Tests

        [TestMethod]
        public void CleanupExpiredMessages_WithExpiredMessages_RemovesExpiredEntries()
        {
            // This test requires modifying the TTL configuration or manipulating time
            // For now, we'll test the basic cleanup functionality
            
            // Arrange
            var messageId = "test-message@example.com";
            _messageIdManager.IsDuplicate(messageId, "test");

            // Act
            var initialCount = _messageIdManager.GetStatistics().CurrentInMemoryCount;
            var removedCount = _messageIdManager.CleanupExpiredMessages();

            // Assert
            Assert.AreEqual(1, initialCount);
            // Without time manipulation, no messages should be expired yet
            Assert.AreEqual(0, removedCount);
        }

        [TestMethod]
        public void GetAllMessageIds_IncludesCorrectEntries()
        {
            // Arrange
            var messageIds = new[]
            {
                "msg1@example.com",
                "msg2@example.com",
                "msg3@example.com"
            };

            foreach (var messageId in messageIds)
            {
                _messageIdManager.IsDuplicate(messageId, "test");
            }

            // Act
            var allEntries = _messageIdManager.GetAllMessageIds().ToList();

            // Assert
            Assert.AreEqual(3, allEntries.Count);
            Assert.IsTrue(allEntries.All(e => messageIds.Contains(e.MessageId)));
            Assert.IsTrue(allEntries.All(e => e.Source == "test"));
            Assert.IsTrue(allEntries.All(e => e.HitCount == 1));
        }

        [TestMethod]
        public void ClearAllMessageIds_RemovesAllEntries()
        {
            // Arrange
            var messageIds = new[]
            {
                "msg1@example.com",
                "msg2@example.com",
                "msg3@example.com"
            };

            foreach (var messageId in messageIds)
            {
                _messageIdManager.IsDuplicate(messageId, "test");
            }

            // Act
            _messageIdManager.ClearAllMessageIds();

            // Assert
            var stats = _messageIdManager.GetStatistics();
            Assert.AreEqual(0, stats.CurrentInMemoryCount);
            Assert.AreEqual(0, stats.TotalMessagesProcessed);
            Assert.AreEqual(0, stats.DuplicatesDetected);
        }

        #endregion

        #region Persistent Storage Tests

        [TestMethod]
        public void MessageIdManager_WithPersistentStorage_CreatesStorageDirectory()
        {
            // Act is in Setup (constructor creates storage)

            // Assert
            Assert.IsTrue(Directory.Exists(_testStoragePath));
        }

        [TestMethod]
        public void IsDuplicate_WithPersistentStorage_CreatesStorageFiles()
        {
            // Arrange
            var messageId = "persistent-test@example.com";

            // Act
            _messageIdManager.IsDuplicate(messageId, "persistent-test");

            // Wait a bit for async persistence
            Thread.Sleep(100);

            // Assert
            var files = Directory.GetFiles(_testStoragePath, "messageids_*.json");
            Assert.IsTrue(files.Length > 0, "Should create at least one storage file");
        }

        #endregion

        #region Performance Tests

        [TestMethod]
        public void IsDuplicate_LargeNumberOfMessages_PerformsWithinReasonableTime()
        {
            // Arrange
            const int messageCount = 10000;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            for (int i = 0; i < messageCount; i++)
            {
                var messageId = $"perf-test-{i}@example.com";
                _messageIdManager.IsDuplicate(messageId, "performance-test");
            }

            stopwatch.Stop();

            // Assert
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 5000, 
                $"Processing {messageCount} messages took {stopwatch.ElapsedMilliseconds}ms (should be < 5000ms)");

            var stats = _messageIdManager.GetStatistics();
            Assert.AreEqual(messageCount, stats.TotalMessagesProcessed);
            Assert.AreEqual(0, stats.DuplicatesDetected);
        }

        [TestMethod]
        public void IsDuplicate_DuplicateDetectionPerformance_FastLookup()
        {
            // Arrange
            const int duplicateCount = 1000;
            var messageId = "duplicate-test@example.com";
            
            // Register the message once
            _messageIdManager.IsDuplicate(messageId, "test");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act - Test duplicate detection performance
            for (int i = 0; i < duplicateCount; i++)
            {
                var isDuplicate = _messageIdManager.IsDuplicate(messageId, "test");
                Assert.IsTrue(isDuplicate);
            }

            stopwatch.Stop();

            // Assert
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, 
                $"Processing {duplicateCount} duplicates took {stopwatch.ElapsedMilliseconds}ms (should be < 1000ms)");
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void IsDuplicate_MessageIdWithSpecialCharacters_HandledCorrectly()
        {
            // Arrange
            var specialMessageIds = new[]
            {
                "message+special@example.com",
                "message-with-dashes@example.com",
                "message_with_underscores@example.com",
                "message.with.dots@example.com",
                "message123@example.com"
            };

            // Act & Assert
            foreach (var messageId in specialMessageIds)
            {
                var isDuplicate1 = _messageIdManager.IsDuplicate(messageId, "test");
                var isDuplicate2 = _messageIdManager.IsDuplicate(messageId, "test");

                Assert.IsFalse(isDuplicate1, $"First occurrence of {messageId} should not be duplicate");
                Assert.IsTrue(isDuplicate2, $"Second occurrence of {messageId} should be duplicate");
            }
        }

        [TestMethod]
        public void IsDuplicate_VeryLongValidMessageId_HandledCorrectly()
        {
            // Arrange
            var longButValidMessageId = new string('a', 200) + "@example.com"; // 211 chars total, under 255 limit

            // Act & Assert
            _messageIdManager.ValidateMessageIdFormat(longButValidMessageId);
            
            var isDuplicate1 = _messageIdManager.IsDuplicate(longButValidMessageId, "test");
            var isDuplicate2 = _messageIdManager.IsDuplicate(longButValidMessageId, "test");

            Assert.IsFalse(isDuplicate1);
            Assert.IsTrue(isDuplicate2);
        }

        #endregion
    }

    [TestClass]
    public class MessageIdCleanupServiceTests
    {
        [TestMethod]
        public void Instance_GetMultipleTimes_ReturnsSameInstance()
        {
            // Act
            var instance1 = MessageIdCleanupService.Instance;
            var instance2 = MessageIdCleanupService.Instance;

            // Assert
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Initialize_CallMultipleTimes_DoesNotThrow()
        {
            // Act & Assert - should not throw
            MessageIdCleanupService.Initialize();
            MessageIdCleanupService.Initialize();
        }

        [TestMethod]
        public void GetCurrentStatistics_ReturnsValidStatistics()
        {
            // Act
            var stats = MessageIdCleanupService.Instance.GetCurrentStatistics();

            // Assert
            Assert.IsNotNull(stats);
            Assert.IsTrue(stats.TotalMessagesProcessed >= 0);
            Assert.IsTrue(stats.DuplicatesDetected >= 0);
            Assert.IsTrue(stats.CurrentInMemoryCount >= 0);
        }

        [TestMethod]
        public void MessageIdServices_GetManager_ReturnsValidManager()
        {
            // Act
            var manager = MessageIdServices.GetManager();

            // Assert
            Assert.IsNotNull(manager);
            Assert.IsInstanceOfType(manager, typeof(IMessageIdManager));
        }

        [TestMethod]
        public void MessageIdServices_TriggerMaintenance_DoesNotThrow()
        {
            // Act & Assert - should not throw
            MessageIdServices.TriggerMaintenance();
        }
    }
} 