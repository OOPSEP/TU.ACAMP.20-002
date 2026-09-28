using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Identity;

namespace TU.ACAMP.WebApp.Controllers.V2;

[ApiController]
[Route("api/v2/events")]
[Produces("application/json")]
public class ActivityEventApiController : ControllerBase
{
    private readonly ApiKeyService _apiKeyService;
    private readonly PlanRateLimiterService _rateLimiter;
    private readonly EventIngestionService _ingestionService;
    private readonly EventAttachmentService _attachmentService;
    private readonly ActivityEventService _eventService;
    private readonly ILogger<ActivityEventApiController> _logger;

    public ActivityEventApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        EventIngestionService ingestionService,
        EventAttachmentService attachmentService,
        ActivityEventService eventService,
        ILogger<ActivityEventApiController> logger)
    {
        #region hide [2026-08-24]
        _apiKeyService = apiKeyService;
        _rateLimiter = rateLimiter;
        _ingestionService = ingestionService;
        _attachmentService = attachmentService;
        _eventService = eventService;
        _logger = logger;
        #endregion
    }

    /// <summary>
    /// [2026-09-13][Claude] ด่านเดียวสำหรับทุก endpoint: ตรวจ API Key แล้วหักโควตา rate limit ทันที
    /// ของเดิมมีเฉพาะ /ingest (batch) ที่เรียก CheckRateLimit ส่วน single-ingest, attachment และ get
    /// ไม่เรียกเลย ทำให้ยิงผ่าน endpoint เหล่านั้นเลี่ยง rate limit ได้ทั้งหมด
    /// คืน (Key, null) เมื่อผ่าน และ (null, Error) เมื่อไม่ผ่าน ให้ผู้เรียก return ค่า Error ออกไปตรง ๆ
    /// </summary>
    private async Task<(TbApiKey? Key, IActionResult? Error)> AuthorizeRequestAsync(string? apiKey)
    {
        var keyEntity = await _apiKeyService.ValidateApiKeyAsync(apiKey);
        if (keyEntity == null)
        {
            return (null, Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier }));
        }

        var rateCheck = _rateLimiter.CheckRateLimit(keyEntity.KeyPrefix, keyEntity.CustomRateLimitPerMin ?? 600);
        if (!rateCheck.IsAllowed)
        {
            Response.Headers.Append("Retry-After", rateCheck.RetryAfterSeconds.ToString());
            return (null, StatusCode(429, new { code = rateCheck.ErrorCode, message = rateCheck.ErrorMessage, traceId = HttpContext.TraceIdentifier }));
        }

        return (keyEntity, null);
    }

    /// <summary>
    /// แปลงสตริงเวลาที่รับจาก client เป็น DateTimeOffset
    /// คืน null เมื่ออ่านไม่ออก เพื่อให้ผู้เรียกตอบ 400 แทนการเดาค่าแทนผู้ใช้
    /// </summary>
    private static DateTimeOffset? ParseOccurredAt(string raw)
    {
        // [2026-09-13][Claude] ของเดิมแทน space ตัวสุดท้ายด้วย "+" ทุกครั้งที่พบ space
        //     ทำให้ "2026-08-01 10:00:00" (ISO ที่ใช้ space แทน T ซึ่งพบบ่อยมาก)
        //     กลายเป็น "2026-08-01+10:00:00" ที่ผิดรูป
        //     ตอนนี้จะเติม "+" คืนเฉพาะเมื่อส่วนท้ายเป็นรูปแบบ offset จริง (HH:mm / HHmm)
        //     และส่วนหน้ามีเวลาอยู่แล้ว จึงแยก " 07:00" (offset ที่ถูก form parser แปลง)
        //     ออกจาก " 10:00" (นาทีของเวลา) ได้ถูกต้อง
        var text = raw.Trim();
        if (text.Length == 0) return null;

        var lastSpaceIdx = text.LastIndexOf(' ');
        if (lastSpaceIdx > 0 && lastSpaceIdx < text.Length - 1 && !text.Contains('+'))
        {
            var head = text.Substring(0, lastSpaceIdx);
            var tail = text.Substring(lastSpaceIdx + 1);

            if (head.Contains(':') && System.Text.RegularExpressions.Regex.IsMatch(tail, @"^\d{2}:?\d{2}$"))
            {
                text = head + "+" + tail;
            }
        }

        // มี offset ระบุมาชัดเจน (ลงท้ายด้วย Z หรือ +HH:mm / -HHmm) -> ใช้ค่านั้นตรง ๆ
        bool hasExplicitOffset = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");

        if (hasExplicitOffset)
        {
            return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDto)
                ? parsedDto
                : null;
        }

        // ไม่ระบุ offset -> ตีความเป็นเวลาตามโซนของระบบผ่าน AppTimeZone (ห้ามพึ่ง local time ของเครื่อง)
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDt))
        {
            return new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Unspecified), AppTimeZone.Zone.GetUtcOffset(parsedDt));
        }

        return null;
    }

    /// <summary>
    /// แมป error code จาก EventIngestionService เป็น HTTP status code ที่สื่อความหมายตรงกับปัญหา
    /// </summary>
    private static int MapIngestErrorStatus(string? code) => code switch
    {
        "topic_not_found" => StatusCodes.Status404NotFound,
        "duplicate_external_ref" => StatusCodes.Status409Conflict,
        "idempotency_conflict" => StatusCodes.Status409Conflict,
        "duplicate_entry" => StatusCodes.Status409Conflict,
        "internal_error" => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status400BadRequest
    };

    public class IngestRequest
    {
        public List<EventIngestionService.IngestEventItemInput> Events { get; set; } = new();
    }

    [HttpPost("/api/v2/topics/{topicKey}/events")]
    public async Task<IActionResult> IngestTopicEvent(
        string topicKey,
        [FromForm] decimal? quantity,
        [FromForm] string? occurredAt,
        [FromForm] string? externalRefId,
        [FromForm] string? remark,
        IFormFile? file,
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        #region hide [2026-08-24]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        // [2026-09-13][Claude] แยก "ไม่ได้ส่ง quantity มา" ออกจาก "ส่งมาเป็น 0"
        //     ของเดิมใช้ quantity ?? 0m ทำให้คำขอที่ลืมใส่ quantity ถูกบันทึกเป็น 0 เงียบ ๆ
        //     (ฝั่ง ingestion ปฏิเสธเฉพาะค่าติดลบ) และค่าที่ parse ไม่ได้ก็ถูกกลืนกลายเป็น 0 เช่นกัน
        bool quantityProvided = quantity.HasValue;
        decimal finalQuantity = quantity ?? 0m;
        string? finalExternalRefId = externalRefId;
        string? finalRemark = remark;
        string? rawOccurredAtStr = occurredAt;

        if (Request.HasFormContentType)
        {
            if (!quantityProvided && Request.Form.TryGetValue("quantity", out var qVal))
            {
                if (!decimal.TryParse(qVal, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsedQ))
                {
                    return BadRequest(new { code = "invalid_quantity", message = $"Quantity value '{qVal}' is not a valid decimal number.", traceId = HttpContext.TraceIdentifier });
                }

                finalQuantity = parsedQ;
                quantityProvided = true;
            }

            if (string.IsNullOrWhiteSpace(rawOccurredAtStr) && Request.Form.TryGetValue("occurredAt", out var dtoVal))
            {
                rawOccurredAtStr = dtoVal.ToString();
            }

            if (string.IsNullOrWhiteSpace(finalExternalRefId) && Request.Form.TryGetValue("externalRefId", out var refVal))
            {
                finalExternalRefId = refVal;
            }

            if (string.IsNullOrWhiteSpace(finalRemark) && Request.Form.TryGetValue("remark", out var remVal))
            {
                finalRemark = remVal;
            }
        }

        // [2026-09-13][Claude] ตรวจ quantity ให้ครบก่อนส่งต่อ
        if (!quantityProvided)
        {
            return BadRequest(new { code = "missing_quantity", message = "Field 'quantity' is required.", traceId = HttpContext.TraceIdentifier });
        }

        bool occurredAtProvided = !string.IsNullOrWhiteSpace(rawOccurredAtStr);
        DateTimeOffset? finalOccurredAt = occurredAtProvided ? ParseOccurredAt(rawOccurredAtStr!) : null;

        // [2026-09-13][Claude] ส่งค่ามาแล้วแต่อ่านไม่ออก ต้องบอกให้ชัดว่ารูปแบบผิด
        //     ของเดิมปล่อยเป็น null แล้วไปโดนปฏิเสธปลายทางด้วยข้อความว่า "required"
        //     ซึ่งทำให้เข้าใจผิดว่าไม่ได้ส่งค่ามา
        if (occurredAtProvided && finalOccurredAt == null)
        {
            return BadRequest(new { code = "invalid_occurred_at", message = $"Value '{rawOccurredAtStr}' is not a valid timestamp. Use ISO-8601, e.g. 2026-08-01T00:00:00+07:00.", traceId = HttpContext.TraceIdentifier });
        }

        var item = new EventIngestionService.IngestEventItemInput
        {
            TopicKey = topicKey,
            Quantity = finalQuantity,
            OccurredAt = finalOccurredAt,
            ExternalRefId = finalExternalRefId,
            Remark = finalRemark ?? string.Empty
        };

        var batchRes = await _ingestionService.IngestBatchAsync(keyEntity.SubscriptionId, idempotencyKey, new List<EventIngestionService.IngestEventItemInput> { item }, "api-single", keyEntity.ScopeLevel, keyEntity.ScopeTargetId, HttpContext.RequestAborted);

        if (batchRes.Rejected > 0)
        {
            // [2026-09-13][Claude] แมป error code เป็น HTTP status ให้สื่อความหมาย
            //     ของเดิมคืน 400 ทุกกรณี ทั้งที่ topic_not_found ควรเป็น 404 และการชนซ้ำควรเป็น 409
            //     (endpoint แนบไฟล์ใน controller เดียวกันแมป status ละเอียดอยู่แล้ว จึงไม่สม่ำเสมอ)
            var firstError = batchRes.Results.FirstOrDefault();
            var errorCode = firstError?.Code ?? "rejected";
            return StatusCode(MapIngestErrorStatus(errorCode), new { code = errorCode, message = firstError?.Message ?? "Ingestion failed.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] นับเฉพาะ Event ที่ถูกสร้างใหม่จริง (ไม่นับ replay ซึ่งไม่ได้สร้างรายการใหม่)
        await _apiKeyService.AddIngestedEventsCountAsync(keyEntity.Id, batchRes.Results.Count(r => r.Status == "created" && !r.Replayed));

        var createdResult = batchRes.Results.FirstOrDefault();
        object? fileResult = null;
        object? fileError = null;

        // [2026-09-13][Claude] รายงานผลการแนบไฟล์ออกไปให้ client เสมอ
        //     ของเดิมถ้า AttachFileToEventAsync ล้มเหลว (ไฟล์ใหญ่เกิน / นามสกุลไม่รองรับ /
        //     magic bytes ไม่ตรง) โค้ดไม่ทำอะไรเลย แล้วยังตอบ 201 พร้อม file = null
        //     ผู้ส่งจึงเข้าใจว่าแนบหลักฐานสำเร็จทั้งที่ไฟล์หายไป ซึ่งยอมรับไม่ได้ในระบบ audit GHG
        string fileStatus = "none";

        if (file != null && file.Length > 0)
        {
            int eventId = 0;
            bool targetResolved = createdResult != null
                && !string.IsNullOrEmpty(createdResult.EventKey)
                && PublicKeyCodec.TryDecode(PublicKeyCodec.EventPrefix, createdResult.EventKey, out eventId);

            if (!targetResolved)
            {
                fileStatus = "failed";
                fileError = new { code = "attachment_target_unresolved", message = "Event was created but its identifier could not be resolved, so the file was not attached." };
                _logger.LogWarning("Attachment skipped: cannot resolve created event key for topic {TopicKey}.", topicKey);
            }
            else
            {
                using var stream = file.OpenReadStream();
                var attachRes = await _attachmentService.AttachFileToEventAsync(keyEntity.SubscriptionId, eventId, stream, file.FileName, file.ContentType, null, HttpContext.RequestAborted);

                if (attachRes.Success)
                {
                    fileResult = attachRes.FileEntity;
                    fileStatus = attachRes.IsDuplicate ? "duplicate" : "attached";
                }
                else
                {
                    fileStatus = "failed";
                    fileError = new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage };
                    _logger.LogWarning("Attachment failed for event {EventId} (topic {TopicKey}): {Code} - {Message}", eventId, topicKey, attachRes.ErrorCode, attachRes.ErrorMessage);
                }
            }
        }

        bool isReplayed = createdResult?.Replayed ?? false;
        string responseStatus = isReplayed ? "replayed" : "created";
        int statusCode = isReplayed ? 200 : 201;

        return StatusCode(statusCode, new
        {
            status = responseStatus,
            replayed = isReplayed,
            eventKey = createdResult?.EventKey,
            topicKey,
            quantity = item.Quantity,
            occurredAt = item.OccurredAt,
            externalRefId = item.ExternalRefId,
            remark = item.Remark,
            file = fileResult,
            fileStatus,
            fileError,
            message = isReplayed ? $"External reference '{item.ExternalRefId}' was previously ingested. Idempotent response returned." : null
        });
        #endregion
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> IngestEvents([FromBody] IngestRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
    {
        #region hide [2026-08-24]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (request?.Events == null || request.Events.Count == 0)
        {
            return BadRequest(new { code = "empty_batch", message = "Events payload cannot be empty.", traceId = HttpContext.TraceIdentifier });
        }

        if (request.Events.Count > 500)
        {
            return StatusCode(413, new { code = "batch_too_large", message = "Batch size exceeds maximum allowed limit of 500 items.", traceId = HttpContext.TraceIdentifier });
        }

        var res = await _ingestionService.IngestBatchAsync(keyEntity.SubscriptionId, idempotencyKey, request.Events, "api-batch", keyEntity.ScopeLevel, keyEntity.ScopeTargetId, HttpContext.RequestAborted);

        if (res.Rejected == request.Events.Count)
        {
            return BadRequest(new { code = "batch_failed", message = "All items in batch were rejected.", batchId = res.BatchId, results = res.Results, traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] นับเฉพาะ Event ที่ถูกสร้างใหม่จริง (ไม่นับ replay)
        await _apiKeyService.AddIngestedEventsCountAsync(keyEntity.Id, res.Results.Count(r => r.Status == "created" && !r.Replayed));

        if (res.Replayed)
        {
            return Ok(new { batchId = res.BatchId, accepted = res.Accepted, rejected = res.Rejected, replayed = true, results = res.Results });
        }

        return StatusCode(201, new { batchId = res.BatchId, accepted = res.Accepted, rejected = res.Rejected, results = res.Results });
        #endregion
    }

    [HttpPost("{eventKey}/attachments")]
    public async Task<IActionResult> AttachFile(string eventKey, IFormFile file, [FromForm] string? tag, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-08-24]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.EventPrefix, eventKey, out var eventId))
        {
            return NotFound(new { code = "event_not_found", message = "Event identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] บังคับ Scope ของ API Key ก่อนแนบไฟล์
        //     EventAttachmentService ตรวจให้แค่ SubscriptionId ทำให้ Key ที่จำกัด Scope
        //     แนบไฟล์ข้าม Target อื่นใน Subscription เดียวกันได้
        var scopedEvent = await _eventService.GetByIdForApiKeyScopeAsync(eventId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);
        if (scopedEvent == null)
        {
            return NotFound(new { code = "event_not_found", message = "Event identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { code = "invalid_payload", message = "No file stream provided in form-data.", traceId = HttpContext.TraceIdentifier });
        }

        using var stream = file.OpenReadStream();
        var attachRes = await _attachmentService.AttachFileToEventAsync(keyEntity.SubscriptionId, eventId, stream, file.FileName, file.ContentType, tag, HttpContext.RequestAborted);

        if (!attachRes.Success)
        {
            if (attachRes.ErrorCode == "event_not_found") return NotFound(new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage, traceId = HttpContext.TraceIdentifier });
            if (attachRes.ErrorCode == "file_too_large") return StatusCode(413, new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage, traceId = HttpContext.TraceIdentifier });
            if (attachRes.ErrorCode == "unsupported_file_type") return StatusCode(415, new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage, traceId = HttpContext.TraceIdentifier });
            if (attachRes.ErrorCode == "file_content_mismatch") return StatusCode(422, new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage, traceId = HttpContext.TraceIdentifier });

            return BadRequest(new { code = attachRes.ErrorCode, message = attachRes.ErrorMessage, traceId = HttpContext.TraceIdentifier });
        }

        if (attachRes.IsDuplicate)
        {
            return Ok(new { eventKey, duplicate = true, file = attachRes.FileEntity });
        }

        return StatusCode(201, new { eventKey, file = attachRes.FileEntity });
        #endregion
    }

    [HttpGet("{eventKey}")]
    public async Task<IActionResult> GetEvent(string eventKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-08-24]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.EventPrefix, eventKey, out var eventId))
        {
            return NotFound(new { code = "event_not_found", message = "Event identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] อ่านผ่านเมธอดที่บังคับ Scope ของ API Key ด้วย
        //     ของเดิมตรวจแค่ SubscriptionId ทำให้ Key ที่จำกัด Scope อ่าน Event ข้าม Target อื่นได้
        var ev = await _eventService.GetByIdForApiKeyScopeAsync(eventId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);
        if (ev == null)
        {
            return NotFound(new { code = "event_not_found", message = "Event identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        var topicKey = PublicKeyCodec.Encode(PublicKeyCodec.TopicPrefix, ev.TopicId);
        return Ok(new
        {
            eventKey,
            topicKey,
            quantity = ev.Quantity,
            occurredAt = ev.TimeStamp,
            externalRefId = ev.ExternalRefId,
            remark = ev.Remark
        });
        #endregion
    }
}
