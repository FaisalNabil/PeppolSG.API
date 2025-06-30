using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using PeppolSG.API.Service;
using log4net;

namespace PeppolSG.API.Controllers
{
    /// <summary>
    /// Controller for monitoring and managing message ID processing
    /// Provides endpoints for operational monitoring and troubleshooting
    /// </summary>
    [RoutePrefix("api/messageids")]
    public class MessageIdStatusController : ApiController
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MessageIdStatusController));

        /// <summary>
        /// Gets current message ID processing statistics
        /// </summary>
        [HttpGet]
        [Route("statistics")]
        public IHttpActionResult GetStatistics()
        {
            try
            {
                var stats = MessageIdServices.GetStatistics();
                
                log.Debug($"Message ID statistics requested. Current count: {stats.CurrentInMemoryCount}");
                
                return Ok(new
                {
                    TotalMessagesProcessed = stats.TotalMessagesProcessed,
                    DuplicatesDetected = stats.DuplicatesDetected,
                    ExpiredMessagesRemoved = stats.ExpiredMessagesRemoved,
                    CurrentInMemoryCount = stats.CurrentInMemoryCount,
                    MaxInMemoryEntries = stats.MaxInMemoryEntries,
                    DuplicateRate = stats.DuplicateRate,
                    MessageIdTtl = stats.MessageIdTtl.ToString(),
                    CleanupInterval = stats.CleanupInterval.ToString(),
                    PersistentStorageEnabled = stats.PersistentStorageEnabled,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                log.Error("Error retrieving message ID statistics", ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Gets all message IDs (for debugging - use with caution in production)
        /// </summary>
        [HttpGet]
        [Route("all")]
        public IHttpActionResult GetAllMessageIds([FromUri] bool includeExpired = false, [FromUri] int limit = 100)
        {
            try
            {
                if (limit > 1000)
                {
                    return BadRequest("Limit cannot exceed 1000 entries");
                }

                var manager = MessageIdServices.GetManager();
                var messageIds = manager.GetAllMessageIds(includeExpired)
                    .Take(limit)
                    .Select(entry => new
                    {
                        MessageId = entry.MessageId,
                        FirstSeen = entry.FirstSeen,
                        LastSeen = entry.LastSeen,
                        Source = entry.Source,
                        HitCount = entry.HitCount,
                        Age = DateTime.UtcNow - entry.LastSeen
                    })
                    .ToList();

                log.Debug($"Retrieved {messageIds.Count} message ID entries");

                return Ok(new
                {
                    Count = messageIds.Count,
                    Limit = limit,
                    IncludeExpired = includeExpired,
                    MessageIds = messageIds,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                log.Error("Error retrieving message IDs", ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Checks if a specific message ID is a duplicate (without registering it)
        /// </summary>
        [HttpGet]
        [Route("check/{messageId}")]
        public IHttpActionResult CheckMessageId(string messageId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                {
                    return BadRequest("Message ID is required");
                }

                var manager = MessageIdServices.GetManager();
                
                // Validate format first
                try
                {
                    manager.ValidateMessageIdFormat(messageId);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest($"Invalid message ID format: {ex.Message}");
                }

                // Check if it exists without registering it
                var allMessageIds = manager.GetAllMessageIds(false);
                var existingEntry = allMessageIds.FirstOrDefault(e => e.MessageId.Equals(messageId, StringComparison.OrdinalIgnoreCase));

                log.Debug($"Message ID check requested for: {messageId}");

                return Ok(new
                {
                    MessageId = messageId,
                    Exists = existingEntry != null,
                    Entry = existingEntry != null ? new
                    {
                        FirstSeen = existingEntry.FirstSeen,
                        LastSeen = existingEntry.LastSeen,
                        Source = existingEntry.Source,
                        HitCount = existingEntry.HitCount,
                        Age = DateTime.UtcNow - existingEntry.LastSeen
                    } : null,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                log.Error($"Error checking message ID: {messageId}", ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Triggers immediate cleanup of expired message IDs
        /// </summary>
        [HttpPost]
        [Route("cleanup")]
        public IHttpActionResult TriggerCleanup()
        {
            try
            {
                log.Info("Manual message ID cleanup triggered via API");
                
                var manager = MessageIdServices.GetManager();
                var removedCount = manager.CleanupExpiredMessages();
                
                log.Info($"Manual cleanup completed: removed {removedCount} expired message IDs");

                return Ok(new
                {
                    Message = "Cleanup completed successfully",
                    RemovedCount = removedCount,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                log.Error("Error during manual cleanup", ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Triggers immediate maintenance (cleanup + statistics logging)
        /// </summary>
        [HttpPost]
        [Route("maintenance")]
        public IHttpActionResult TriggerMaintenance()
        {
            try
            {
                log.Info("Manual message ID maintenance triggered via API");
                
                MessageIdServices.TriggerMaintenance();
                
                // Get updated statistics after maintenance
                var stats = MessageIdServices.GetStatistics();
                
                return Ok(new
                {
                    Message = "Maintenance triggered successfully",
                    Statistics = new
                    {
                        TotalMessagesProcessed = stats.TotalMessagesProcessed,
                        DuplicatesDetected = stats.DuplicatesDetected,
                        ExpiredMessagesRemoved = stats.ExpiredMessagesRemoved,
                        CurrentInMemoryCount = stats.CurrentInMemoryCount,
                        DuplicateRate = stats.DuplicateRate
                    },
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                log.Error("Error during manual maintenance", ex);
                return InternalServerError(ex);
            }
        }

        /// <summary>
        /// Gets health status of message ID management system
        /// </summary>
        [HttpGet]
        [Route("health")]
        public IHttpActionResult GetHealth()
        {
            try
            {
                var stats = MessageIdServices.GetStatistics();
                
                var health = new
                {
                    Status = "Healthy",
                    Issues = new List<string>(),
                    Warnings = new List<string>(),
                    Statistics = new
                    {
                        CurrentInMemoryCount = stats.CurrentInMemoryCount,
                        MaxInMemoryEntries = stats.MaxInMemoryEntries,
                        MemoryUsagePercentage = (double)stats.CurrentInMemoryCount / stats.MaxInMemoryEntries * 100,
                        DuplicateRate = stats.DuplicateRate,
                        TotalMessagesProcessed = stats.TotalMessagesProcessed
                    },
                    Timestamp = DateTime.UtcNow
                };

                // Check for potential issues
                var issues = (List<string>)health.Issues;
                var warnings = (List<string>)health.Warnings;

                if (stats.CurrentInMemoryCount > stats.MaxInMemoryEntries * 0.9)
                {
                    issues.Add("Memory usage critical: " + health.Statistics.MemoryUsagePercentage.ToString("F1") + "%");
                    health = new { Status = "Critical", health.Issues, health.Warnings, health.Statistics, health.Timestamp };
                }
                else if (stats.CurrentInMemoryCount > stats.MaxInMemoryEntries * 0.8)
                {
                    warnings.Add("Memory usage high: " + health.Statistics.MemoryUsagePercentage.ToString("F1") + "%");
                    health = new { Status = "Warning", health.Issues, health.Warnings, health.Statistics, health.Timestamp };
                }

                if (stats.DuplicateRate > 15)
                {
                    warnings.Add($"High duplicate rate: {stats.DuplicateRate:F2}%");
                    if (health.Status == "Healthy")
                        health = new { Status = "Warning", health.Issues, health.Warnings, health.Statistics, health.Timestamp };
                }

                log.Debug($"Message ID health check: {health.Status}");

                return Ok(health);
            }
            catch (Exception ex)
            {
                log.Error("Error during health check", ex);
                return Ok(new
                {
                    Status = "Error",
                    Issues = new[] { "Health check failed: " + ex.Message },
                    Warnings = new string[0],
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <summary>
        /// Validates a message ID format without processing it
        /// </summary>
        [HttpPost]
        [Route("validate")]
        public IHttpActionResult ValidateMessageId([FromBody] MessageIdValidationRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.MessageId))
                {
                    return BadRequest("Message ID is required");
                }

                var manager = MessageIdServices.GetManager();
                
                try
                {
                    manager.ValidateMessageIdFormat(request.MessageId);
                    
                    log.Debug($"Message ID validation successful: {request.MessageId}");
                    
                    return Ok(new
                    {
                        MessageId = request.MessageId,
                        Valid = true,
                        Message = "Message ID format is valid",
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (ArgumentException ex)
                {
                    log.Debug($"Message ID validation failed: {request.MessageId} - {ex.Message}");
                    
                    return Ok(new
                    {
                        MessageId = request.MessageId,
                        Valid = false,
                        Message = ex.Message,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error validating message ID: {request?.MessageId}", ex);
                return InternalServerError(ex);
            }
        }
    }

    /// <summary>
    /// Request model for message ID validation
    /// </summary>
    public class MessageIdValidationRequest
    {
        public string MessageId { get; set; }
    }
} 