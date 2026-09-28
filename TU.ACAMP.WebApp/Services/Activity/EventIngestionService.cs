using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Common;

namespace TU.ACAMP.WebApp.Services.Activity;

public class EventIngestionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<TU.ACAMP.WebApp.Hubs.EventNotificationHub> _hubContext;
    private readonly GhgEventBusService _eventBus;
    private readonly ILogger<EventIngestionService> _logger;

    public EventIngestionService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        Microsoft.AspNetCore.SignalR.IHubContext<TU.ACAMP.WebApp.Hubs.EventNotificationHub> hubContext,
        GhgEventBusService eventBus,
        ILogger<EventIngestionService> logger)
    {
        #region hide [2026-08-24]
        _dbFactory = dbFactory;
        _hubContext = hubContext;
        _eventBus = eventBus;
        _logger = logger;
        #endregion
    }

    public class IngestEventItemInput
    {
        public string TopicKey { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public DateTimeOffset? OccurredAt { get; set; }
        public string? ExternalRefId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
    }

    public class IngestItemResult
    {
        public int Index { get; set; }
        public string Status { get; set; } = "created";
        public string? EventKey { get; set; }
        public string? ExternalRefId { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public bool Replayed { get; set; }
    }

    public class IngestBatchResult
    {
        public string BatchId { get; set; } = string.Empty;
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public bool Replayed { get; set; }
        public List<IngestItemResult> Results { get; set; } = new();
    }

    public async Task<IngestBatchResult> IngestBatchAsync(
        int subscriptionId,
        string? idempotencyKey,
        List<IngestEventItemInput> items,
        string sourceChannel = "api-batch",
        TU.ACAMP.Domain.Enums.eTopicLevel? scopeLevel = null,
        int? scopeTargetId = null,
        CancellationToken ct = default)
    {
        #region hide [2026-08-24]
        var batchId = Guid.NewGuid().ToString("N");
        var result = new IngestBatchResult { BatchId = batchId };

        if (items == null || items.Count == 0)
        {
            return result;
        }

        using var db = await _dbFactory.CreateDbContextAsync(ct);

        var topicQuery = db.Topics
            .AsNoTracking()
            .Where(t => t.SubscriptionId == subscriptionId && !t.IsDeleted);

        if (scopeLevel.HasValue && scopeTargetId.HasValue)
        {
            topicQuery = topicQuery.Where(t => t.Level == scopeLevel.Value && t.TargetId == scopeTargetId.Value);
        }

        var allowedTopicIds = await topicQuery
            .Select(t => t.Id)
            .ToHashSetAsync(ct);

        int index = 0;
        foreach (var item in items)
        {
            var itemResult = new IngestItemResult
            {
                Index = index++,
                ExternalRefId = item.ExternalRefId
            };

            if (string.IsNullOrWhiteSpace(item.TopicKey) ||
                !PublicKeyCodec.TryDecode(PublicKeyCodec.TopicPrefix, item.TopicKey, out var topicId) ||
                !allowedTopicIds.Contains(topicId))
            {
                itemResult.Status = "rejected";
                itemResult.Code = "topic_not_found";
                itemResult.Message = "Topic identifier not found or not accessible.";
                result.Results.Add(itemResult);
                result.Rejected++;
                continue;
            }

            if (item.Quantity < 0)
            {
                itemResult.Status = "rejected";
                itemResult.Code = "invalid_quantity";
                itemResult.Message = "Quantity cannot be negative.";
                result.Results.Add(itemResult);
                result.Rejected++;
                continue;
            }

            if (!item.OccurredAt.HasValue || item.OccurredAt.Value == default)
            {
                itemResult.Status = "rejected";
                itemResult.Code = "invalid_timestamp";
                itemResult.Message = "OccurredAt timestamp with explicit timezone offset is required.";
                result.Results.Add(itemResult);
                result.Rejected++;
                continue;
            }

            var occurredAtOffset = item.OccurredAt.Value;
            var reportingPeriod = AppTimeZone.ToReportingPeriod(occurredAtOffset);

            var itemJson = JsonSerializer.Serialize(item);
            var payloadHash = ComputeSha256(itemJson);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                // [2026-09-13][Claude] ค้นด้วย (SubscriptionId + IdempotencyKey + PayloadHash) ให้ตรงกับ
                //     unique index ux_staging_idem ที่ประกาศไว้ใน ApplicationDbContext
                //     ของเดิมค้นด้วย IdempotencyKey อย่างเดียว พอ item แรกใน batch ถูก Promote สำเร็จ
                //     item ถัดไปจะไปเจอ staging ของ item แรกแล้ว PayloadHash ไม่ตรง
                //     จึงถูกปฏิเสธด้วย idempotency_conflict ทั้งหมด = batch ที่ส่ง Idempotency-Key
                //     บันทึกได้แค่ item เดียวเท่านั้น
                var replayTarget = await db.EventStagings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId &&
                                              s.IdempotencyKey == idempotencyKey &&
                                              s.PayloadHash == payloadHash &&
                                              s.Status == "Promoted", ct);

                if (replayTarget?.PromotedEventId != null)
                {
                    // payload เดิมเป๊ะ = การส่งซ้ำ คืน EventKey เดิมโดยไม่สร้างรายการใหม่
                    itemResult.Status = "created";
                    itemResult.Replayed = true;
                    itemResult.EventKey = PublicKeyCodec.Encode(PublicKeyCodec.EventPrefix, replayTarget.PromotedEventId.Value);
                    result.Results.Add(itemResult);
                    result.Accepted++;
                    result.Replayed = true;
                    continue;
                }

                // [2026-09-13][Claude] ตรวจ conflict เฉพาะคำขอที่มี item เดียวเท่านั้น
                //     เพราะใน batch หลายรายการ การที่ PayloadHash ต่างกันคือเรื่องปกติ (คนละ item)
                //     ไม่ใช่การนำ Idempotency-Key เดิมมาใช้ซ้ำกับเนื้อหาอื่น
                if (items.Count == 1)
                {
                    var keyUsedBefore = await db.EventStagings
                        .AsNoTracking()
                        .AnyAsync(s => s.SubscriptionId == subscriptionId &&
                                       s.IdempotencyKey == idempotencyKey &&
                                       s.Status == "Promoted", ct);

                    if (keyUsedBefore)
                    {
                        itemResult.Status = "rejected";
                        itemResult.Code = "idempotency_conflict";
                        itemResult.Message = "Idempotency-Key reused with different payload content.";
                        result.Results.Add(itemResult);
                        result.Rejected++;
                        continue;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(item.ExternalRefId))
            {
                var existingExtRef = await db.EventStagings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SubscriptionId == subscriptionId &&
                                              s.ExternalRefId == item.ExternalRefId &&
                                              s.Status == "Promoted", ct);

                if (existingExtRef != null)
                {
                    if (existingExtRef.PayloadHash == payloadHash && existingExtRef.PromotedEventId.HasValue)
                    {
                        itemResult.Status = "created";
                        itemResult.Replayed = true;
                        itemResult.EventKey = PublicKeyCodec.Encode(PublicKeyCodec.EventPrefix, existingExtRef.PromotedEventId.Value);
                        result.Results.Add(itemResult);
                        result.Accepted++;
                        result.Replayed = true;
                        continue;
                    }
                    else
                    {
                        itemResult.Status = "rejected";
                        itemResult.Code = "duplicate_external_ref";
                        itemResult.Message = $"External reference '{item.ExternalRefId}' has already been ingested.";
                        result.Results.Add(itemResult);
                        result.Rejected++;
                        continue;
                    }
                }
            }

            using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var staging = new TbEventStaging
                {
                    SubscriptionId = subscriptionId,
                    BatchId = batchId,
                    IdempotencyKey = idempotencyKey,
                    ExternalRefId = item.ExternalRefId,
                    PayloadHash = payloadHash,
                    TopicKeyRaw = item.TopicKey,
                    ResolvedTopicId = topicId,
                    Quantity = item.Quantity,
                    OccurredAtUtc = occurredAtOffset.UtcDateTime,
                    OccurredAtOffsetMinutes = (int)occurredAtOffset.Offset.TotalMinutes,
                    Title = item.Title?.Trim() ?? string.Empty,
                    Remark = item.Remark?.Trim() ?? string.Empty,
                    PayloadJson = JsonNode.Parse(itemJson) ?? JsonNode.Parse("{}")!,
                    Status = "Pending",
                    SourceChannel = sourceChannel
                };

                db.EventStagings.Add(staging);
                await db.SaveChangesAsync(ct);

                var ev = new TbActivityEvent
                {
                    SubscriptionId = subscriptionId,
                    TopicId = topicId,
                    TimeStamp = reportingPeriod,
                    Quantity = item.Quantity,
                    Remark = item.Remark?.Trim() ?? string.Empty,
                    ExternalRefId = item.ExternalRefId,
                    SourceChannel = sourceChannel,
                    StagingId = staging.Id
                };

                db.Events.Add(ev);
                await db.SaveChangesAsync(ct);

                staging.Status = "Promoted";
                staging.PromotedEventId = ev.Id;
                await db.SaveChangesAsync(ct);

                await tx.CommitAsync(ct);

                itemResult.Status = "created";
                itemResult.EventKey = PublicKeyCodec.Encode(PublicKeyCodec.EventPrefix, ev.Id);
                result.Results.Add(itemResult);
                result.Accepted++;

                // 1. Publish to In-Memory Event Bus (for immediate Blazor Server UI reactivity)
                try
                {
                    await _eventBus.PublishEventIngestedAsync(new GhgEventIngestedNotification(
                        subscriptionId,
                        topicId,
                        item.TopicKey,
                        itemResult.EventKey,
                        item.Quantity,
                        occurredAtOffset
                    ));
                }
                catch (Exception busEx)
                {
                    _logger.LogWarning(busEx, "Failed to publish in-memory GHG event notification for batch {BatchId}", batchId);
                }

                // 2. Broadcast to SignalR Hub (for external client listeners)
                try
                {
                    await _hubContext.Clients.Group($"sub_{subscriptionId}").SendAsync("ReceiveEventIngested", new
                    {
                        subscriptionId,
                        topicId,
                        topicKey = item.TopicKey,
                        eventKey = itemResult.EventKey,
                        quantity = item.Quantity,
                        occurredAt = occurredAtOffset
                    }, ct);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Failed to broadcast SignalR event notification");
                }
            }
            catch (DbUpdateException dbEx)
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(dbEx, "Database unique constraint conflict during staging event promotion in batch {BatchId}", batchId);

                itemResult.Status = "rejected";
                if (dbEx.InnerException?.Message.Contains("ux_staging_extref") == true || dbEx.Message.Contains("ux_staging_extref") == true)
                {
                    itemResult.Code = "duplicate_external_ref";
                    itemResult.Message = $"External reference '{item.ExternalRefId}' has already been ingested.";
                }
                else
                {
                    itemResult.Code = "duplicate_entry";
                    itemResult.Message = "Duplicate database entry detected.";
                }
                result.Results.Add(itemResult);
                result.Rejected++;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Failed to promote staging event in batch {BatchId}", batchId);

                itemResult.Status = "rejected";
                itemResult.Code = "internal_error";
                itemResult.Message = "Failed to process event transaction.";
                result.Results.Add(itemResult);
                result.Rejected++;
            }
        }

        return result;
        #endregion
    }

    private static string ComputeSha256(string raw)
    {
        #region hide [2026-08-24]
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
        #endregion
    }
}
