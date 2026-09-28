using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Emissions;
using TU.ACAMP.WebApp.Services.Identity;
using TU.ACAMP.WebApp.Services.Organization;

namespace TU.ACAMP.WebApp.Controllers.V2;

/// <summary>
/// Public REST API for Topics (CRUD). Authenticated by X-API-Key and bound to the key's Subscription/Scope.
/// Topics are always addressed by their public TopicKey (tpc_xxx); database integer keys are never exposed.
/// </summary>
[ApiController]
[Route("api/v2/topics")]
[Produces("application/json")]
public class ActivityTopicApiController : ControllerBase
{
    #region class field
    private readonly ApiKeyService _apiKeyService;
    private readonly PlanRateLimiterService _rateLimiter;
    private readonly ActivityTopicService _topicService;
    private readonly EmissionSourceService _sourceService;
    private readonly EmissionMappingService _mappingService;
    private readonly ActivityGroupService _activityGroupService;
    private readonly CorporateService _corporateService;
    private readonly CompanyService _companyService;
    private readonly FacilityService _facilityService;
    private readonly ILogger<ActivityTopicApiController> _logger;

    private const int MaxPageSize = 200;
    #endregion

    #region class constructor
    public ActivityTopicApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        ActivityTopicService topicService,
        EmissionSourceService sourceService,
        EmissionMappingService mappingService,
        ActivityGroupService activityGroupService,
        CorporateService corporateService,
        CompanyService companyService,
        FacilityService facilityService,
        ILogger<ActivityTopicApiController> logger)
    {
        #region hide [2026-09-13]
        _apiKeyService = apiKeyService;
        _rateLimiter = rateLimiter;
        _topicService = topicService;
        _sourceService = sourceService;
        _mappingService = mappingService;
        _activityGroupService = activityGroupService;
        _corporateService = corporateService;
        _companyService = companyService;
        _facilityService = facilityService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    /// <summary>Payload for creating a Topic with its master emission source.</summary>
    public class CreateTopicRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }

        /// <summary>Corporate = 1, Company = 2, Facility = 3. Optional when the API Key is already scoped.</summary>
        public int? Level { get; set; }
        public int? TargetId { get; set; }

        /// <summary>
        /// [2026-09-23][Claude] Target by public key (crp_ / cmp_ / fac_) — ใช้แทน level + targetId ได้ (Org API คืนแต่ Key)
        /// </summary>
        public string? TargetKey { get; set; }

        /// <summary>Exact Emission Source title (case-insensitive) available to this Subscription.</summary>
        public string? EmissionSource { get; set; }

        /// <summary>[2026-09-23][Claude] Emission Source by public key (esr_xxx) — ชัดเจนกว่าชื่อ (ชื่อ Default อาจซ้ำกัน); ส่งอย่างใดอย่างหนึ่งกับ emissionSource</summary>
        public string? EmissionSourceKey { get; set; }

        public decimal? UnitConversionRatio { get; set; }
        public float? EmissionSharePercent { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>Payload for updating the editable fields of a Topic.</summary>
    public class UpdateTopicRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? IsActive { get; set; }
    }
    #endregion

    #region private helpers
    /// <summary>
    /// [2026-09-13][Claude] ด่านเดียวสำหรับทุก endpoint ของ Topics API: ตรวจ API Key แล้วหักโควตา rate limit ทันที
    /// ใช้รูปแบบเดียวกับ ActivityEventApiController เพื่อไม่ให้มี endpoint ไหนหลุดการตรวจ (คืน (Key, null) เมื่อผ่าน)
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
    /// [2026-09-13][Claude] หา Level/TargetId ปลายทางของคำขอ
    /// ถ้า API Key ถูกจำกัด Scope ไว้แล้ว ให้ยึด Scope ของ Key เสมอ และปฏิเสธคำขอที่พยายามระบุ Target อื่น
    /// (กัน Key ระดับโรงงานเดียวสร้าง Topic ข้ามโรงงานใน Subscription เดียวกัน)
    /// </summary>
    private (eTopicLevel? Level, int? TargetId, IActionResult? Error) ResolveTarget(TbApiKey key, int? requestedLevel, int? requestedTargetId)
    {
        if (key.ScopeLevel.HasValue && key.ScopeTargetId.HasValue)
        {
            bool conflict = (requestedLevel.HasValue && requestedLevel.Value != (int)key.ScopeLevel.Value)
                         || (requestedTargetId.HasValue && requestedTargetId.Value != key.ScopeTargetId.Value);

            if (conflict)
            {
                return (null, null, StatusCode(403, new { code = "scope_forbidden", message = "This API Key is restricted to its own scope and cannot address another target.", traceId = HttpContext.TraceIdentifier }));
            }

            return (key.ScopeLevel.Value, key.ScopeTargetId.Value, null);
        }

        if (!requestedLevel.HasValue || !requestedTargetId.HasValue || requestedTargetId.Value <= 0)
        {
            return (null, null, BadRequest(new { code = "missing_target", message = "Fields 'level' (1=Corporate, 2=Company, 3=Facility) and 'targetId' are required for an unscoped API Key.", traceId = HttpContext.TraceIdentifier }));
        }

        if (!Enum.IsDefined(typeof(eTopicLevel), requestedLevel.Value))
        {
            return (null, null, BadRequest(new { code = "invalid_level", message = "Field 'level' must be 1 (Corporate), 2 (Company) or 3 (Facility).", traceId = HttpContext.TraceIdentifier }));
        }

        return ((eTopicLevel)requestedLevel.Value, requestedTargetId.Value, null);
    }

    /// <summary>
    /// [2026-09-23][Claude] แปลง targetKey (crp_ / cmp_ / fac_) เป็น Level + TargetId พร้อมบังคับว่าเป็นของ Subscription ของ Key
    /// (กติกา Scope ของ Key ตรวจต่อใน ResolveTarget)
    /// </summary>
    private async Task<(eTopicLevel? Level, int? TargetId, IActionResult? Error)> ResolveTargetKeyAsync(TbApiKey key, string targetKey)
    {
        #region hide [2026-09-23]
        var notFound = NotFound(new { code = "target_not_found", message = "Field 'targetKey' does not identify a corporate, company or facility of this subscription.", traceId = HttpContext.TraceIdentifier });
        var raw = targetKey.Trim();

        if (PublicKeyCodec.TryDecode(PublicKeyCodec.FacilityPrefix, raw, out var facilityId))
        {
            var facility = await _facilityService.GetByIdAsync(facilityId);
            return facility?.Company?.Corporate?.SubscriptionId == key.SubscriptionId
                ? (eTopicLevel.Facility, facility.Id, null)
                : (null, null, notFound);
        }
        if (PublicKeyCodec.TryDecode(PublicKeyCodec.CompanyPrefix, raw, out var companyId))
        {
            var company = await _companyService.GetByIdAsync(companyId);
            return company?.Corporate?.SubscriptionId == key.SubscriptionId
                ? (eTopicLevel.Company, company.Id, null)
                : (null, null, notFound);
        }
        if (PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, raw, out var corporateId))
        {
            var corporate = await _corporateService.GetByIdAsync(corporateId);
            return corporate?.SubscriptionId == key.SubscriptionId
                ? (eTopicLevel.Corporate, corporate.Id, null)
                : (null, null, notFound);
        }
        return (null, null, notFound);
        #endregion
    }

    /// <summary>[2026-09-23][Claude] Key สาธารณะของ Target ตาม Level (crp_ / cmp_ / fac_)</summary>
    private static string EncodeTargetKey(eTopicLevel level, int targetId) => level switch
    {
        eTopicLevel.Corporate => PublicKeyCodec.Encode(PublicKeyCodec.CorporatePrefix, targetId),
        eTopicLevel.Company => PublicKeyCodec.Encode(PublicKeyCodec.CompanyPrefix, targetId),
        _ => PublicKeyCodec.Encode(PublicKeyCodec.FacilityPrefix, targetId)
    };

    /// <summary>Projects a Topic child row into the public API contract.</summary>
    private static object MapChild(TbActivityTopicChild child) => new
    {
        emissionSourceKey = PublicKeyCodec.Encode(PublicKeyCodec.EmissionSourcePrefix, child.SourceId),
        activityGroup = child.ActivityGroup?.Title,
        activityGroupCode = child.ActivityGroup?.Code,
        scope = child.ActivityGroup?.TgoScopeNo.ToString(),
        emissionSource = child.Source?.Title,
        unit = child.Source?.Unit?.Title,
        ef = child.Source?.EF,
        unitConversionRatio = child.UnitConversionRatio ?? 1m,
        emissionSharePercent = child.EmissionSharePercent,
        isActive = child.IsActive
    };

    /// <summary>Projects a Topic into the public API contract (TopicKey only, never the integer Id).</summary>
    private static object MapTopic(TbActivityTopic topic, bool includeEventCount) => new
    {
        topicKey = PublicKeyCodec.Encode(PublicKeyCodec.TopicPrefix, topic.Id),
        title = topic.Title,
        remark = topic.Remark,
        level = topic.Level.ToString(),
        levelNo = (int)topic.Level,
        targetId = topic.TargetId,
        targetKey = EncodeTargetKey(topic.Level, topic.TargetId),
        emissionGroup = topic.EmissionGroup?.Title,
        isActive = topic.IsActive,
        createdAt = topic.CreatedAt,
        updatedAt = topic.UpdatedAt,
        eventCount = includeEventCount ? topic.Events.Count : (int?)null,
        childs = topic.Childs.OrderBy(c => c.Id).Select(MapChild).ToList()
    };
    #endregion

    #region endpoints
    /// <summary>
    /// Lists Topics visible to the API Key, newest scope filter first, with paging.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTopics(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromQuery] int? level = null,
        [FromQuery] int? targetId = null,
        [FromQuery] string? q = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        #region hide [2026-09-13]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (level.HasValue && !Enum.IsDefined(typeof(eTopicLevel), level.Value))
        {
            return BadRequest(new { code = "invalid_level", message = "Query 'level' must be 1 (Corporate), 2 (Company) or 3 (Facility).", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] บีบค่า paging ให้อยู่ในกรอบ กัน pageSize มหาศาลลากทั้งตารางออกมา
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;

        var (items, total) = await _topicService.GetPagedForApiKeyScopeAsync(
            keyEntity.SubscriptionId,
            keyEntity.ScopeLevel,
            keyEntity.ScopeTargetId,
            level.HasValue ? (eTopicLevel)level.Value : null,
            targetId,
            q,
            includeInactive,
            page,
            pageSize);

        return Ok(new
        {
            page,
            pageSize,
            totalCount = total,
            totalPages = pageSize > 0 ? (int)Math.Ceiling(total / (double)pageSize) : 0,
            items = items.Select(t => MapTopic(t, includeEventCount: false)).ToList()
        });
        #endregion
    }

    /// <summary>
    /// Returns a single Topic with its emission source childs.
    /// </summary>
    [HttpGet("{topicKey}")]
    public async Task<IActionResult> GetTopic(string topicKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-13]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.TopicPrefix, topicKey, out var topicId))
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        var topic = await _topicService.GetForApiKeyScopeAsync(topicId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);
        if (topic == null)
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        return Ok(MapTopic(topic, includeEventCount: true));
        #endregion
    }

    /// <summary>
    /// Creates a Topic from one master emission source. Scope 3 WTT / Biogenic childs are branched
    /// automatically from TbEmissionMapping only; free-form child sources are never accepted.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateTopic([FromBody] CreateTopicRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-13]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (request == null)
        {
            return BadRequest(new { code = "invalid_payload", message = "Request body is required.", traceId = HttpContext.TraceIdentifier });
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return BadRequest(new { code = "missing_title", message = "Field 'title' is required.", traceId = HttpContext.TraceIdentifier });
        }

        if (title.Length > 200)
        {
            return BadRequest(new { code = "invalid_title", message = "Field 'title' exceeds 200 characters.", traceId = HttpContext.TraceIdentifier });
        }

        if (string.IsNullOrWhiteSpace(request.EmissionSource) && string.IsNullOrWhiteSpace(request.EmissionSourceKey))
        {
            return BadRequest(new { code = "missing_emission_source", message = "Field 'emissionSourceKey' or 'emissionSource' is required.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-23][Claude] Remark ยาวไม่เกิน [StringLength(500)] ของ TbActivityTopic
        if ((request.Remark?.Trim().Length ?? 0) > 500)
        {
            return BadRequest(new { code = "invalid_remark", message = "Field 'remark' exceeds 500 characters.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-23][Claude] targetKey มาก่อน level + targetId (ถ้าส่งมาทั้งคู่ใช้ targetKey)
        int? requestedLevel = request.Level;
        int? requestedTargetId = request.TargetId;
        if (!string.IsNullOrWhiteSpace(request.TargetKey))
        {
            var (keyLevel, keyTargetId, keyError) = await ResolveTargetKeyAsync(keyEntity, request.TargetKey);
            if (keyError != null) return keyError;
            requestedLevel = (int)keyLevel!.Value;
            requestedTargetId = keyTargetId;
        }

        var (level, targetId, targetError) = ResolveTarget(keyEntity, requestedLevel, requestedTargetId);
        if (targetError != null) return targetError;

        var topicLevel = level!.Value;
        var topicTargetId = targetId!.Value;

        // [2026-09-13][Claude] Topic Integrity: ถ้า (Title, Level, TargetId) มีอยู่แล้วต้องตอบ Already Exists แล้วหยุด
        //     ห้ามสร้างซ้ำและห้ามเอา Source ใหม่ไปต่อท้าย Topic เดิม (กฎเดียวกับฝั่ง Import Excel)
        if (await _topicService.TitleExistsForTargetAsync(keyEntity.SubscriptionId, topicLevel, topicTargetId, title))
        {
            return Conflict(new { code = "topic_already_exists", message = "A topic with the same title already exists for this target.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] จับคู่ Emission Source แบบตรงตัวเท่านั้น (ไม่ทำ fuzzy match ในชั้น API)
        //     ถ้าชื่อซ้ำกันหลายรายการให้ผู้เรียกไปเลือกเอง ดีกว่าเดาแล้วผูก EF ผิดตัว
        var sources = await _sourceService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
        List<TbEmissionSource> matched;
        string keyword;
        if (!string.IsNullOrWhiteSpace(request.EmissionSourceKey))
        {
            // [2026-09-23][Claude] อ้าง Source ด้วย Key (ต้องเป็น Source ที่ Subscription ใช้ได้)
            keyword = request.EmissionSourceKey.Trim();
            matched = PublicKeyCodec.TryDecode(PublicKeyCodec.EmissionSourcePrefix, keyword, out var sourceId)
                ? sources.Where(s => s.Id == sourceId).ToList()
                : new List<TbEmissionSource>();
        }
        else
        {
            keyword = request.EmissionSource!.Trim();
            matched = sources.Where(s => string.Equals(s.Title?.Trim(), keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (matched.Count == 0)
        {
            return BadRequest(new { code = "emission_source_not_found", message = $"Emission source '{keyword}' was not found for this subscription.", traceId = HttpContext.TraceIdentifier });
        }

        if (matched.Count > 1)
        {
            return Conflict(new { code = "emission_source_ambiguous", message = $"Emission source '{keyword}' matches {matched.Count} records. Please rename or use a unique source title.", traceId = HttpContext.TraceIdentifier });
        }

        var masterSource = matched[0];
        var activityGroups = await _activityGroupService.GetAllAsync();

        var childs = new List<TbActivityTopicChild>
        {
            new TbActivityTopicChild
            {
                ActivityGroupId = TbEmissionMapping.GetActivityGroupIdForSource(masterSource, activityGroups),
                SourceId = masterSource.Id,
                ActivityUnitId = masterSource.UnitId,
                UnitConversionRatio = request.UnitConversionRatio ?? 1m,
                EmissionSharePercent = request.EmissionSharePercent ?? 100f,
                IsActive = true
            }
        };

        // [2026-09-13][Claude] แตกกิ่ง Child อัตโนมัติตาม TbEmissionMapping เท่านั้น และเฉพาะ Master ที่เป็น Scope 1/2
        //     ใช้เงื่อนไขชุดเดียวกับหน้า AddEditActivityTopic เพื่อให้ผลลัพธ์ผ่าน API กับผ่าน UI ตรงกัน
        var masterScope = masterSource.Group?.TgoScopeNo;
        if (masterScope == eTgoScopeNo.Scope1 || masterScope == eTgoScopeNo.Scope2)
        {
            var mapping = await _mappingService.FindMappingBySourceIdAsync(masterSource.Id, keyEntity.SubscriptionId);
            if (mapping != null)
            {
                if (mapping.Scope3ChildId > 0)
                {
                    var s3Source = sources.FirstOrDefault(s => s.Id == mapping.Scope3ChildId);
                    childs.Add(new TbActivityTopicChild
                    {
                        ActivityGroupId = s3Source != null
                            ? TbEmissionMapping.GetActivityGroupIdForSource(s3Source, activityGroups)
                            : activityGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.Scope3)?.Id ?? 0,
                        SourceId = mapping.Scope3ChildId,
                        ActivityUnitId = s3Source?.UnitId,
                        UnitConversionRatio = mapping.UnitConversionRatio ?? 1m,
                        EmissionSharePercent = 100f,
                        IsActive = true
                    });
                }

                if (mapping.BiogenicChildId > 0)
                {
                    var bioSource = sources.FirstOrDefault(s => s.Id == mapping.BiogenicChildId);
                    childs.Add(new TbActivityTopicChild
                    {
                        ActivityGroupId = bioSource != null
                            ? TbEmissionMapping.GetActivityGroupIdForSource(bioSource, activityGroups)
                            : activityGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.SeparateReport)?.Id ?? 0,
                        SourceId = mapping.BiogenicChildId,
                        ActivityUnitId = bioSource?.UnitId,
                        UnitConversionRatio = 1m,
                        EmissionSharePercent = TbEmissionMapping.BiogenicChildEmissionSharePercent, // [2026-09-24][Claude] เดิมคูณ BiogenicSharePercent ซ้ำ
                        IsActive = true
                    });
                }
            }
        }

        var topic = new TbActivityTopic
        {
            SubscriptionId = keyEntity.SubscriptionId,
            Level = topicLevel,
            TargetId = topicTargetId,
            EmissionGroupId = masterSource.GroupId,
            Title = title,
            Remark = request.Remark?.Trim() ?? string.Empty,
            IsActive = request.IsActive ?? true
        };

        var created = await _topicService.SaveTopicWithChildsAsync(topic, childs);
        var result = await _topicService.GetForApiKeyScopeAsync(created.Id, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);

        _logger.LogInformation("Topic created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);

        return StatusCode(201, result != null
            ? MapTopic(result, includeEventCount: true)
            : new { topicKey = PublicKeyCodec.Encode(PublicKeyCodec.TopicPrefix, created.Id) });
        #endregion
    }

    /// <summary>
    /// Updates the editable fields of a Topic (title, remark, active flag).
    /// Level, target and emission mapping are intentionally immutable through this API.
    /// </summary>
    [HttpPatch("{topicKey}")]
    public async Task<IActionResult> UpdateTopic(string topicKey, [FromBody] UpdateTopicRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-13]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (request == null)
        {
            return BadRequest(new { code = "invalid_payload", message = "Request body is required.", traceId = HttpContext.TraceIdentifier });
        }

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.TopicPrefix, topicKey, out var topicId))
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        var topic = await _topicService.GetForApiKeyScopeAsync(topicId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);
        if (topic == null)
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        if (request.Title == null && request.Remark == null && !request.IsActive.HasValue)
        {
            return BadRequest(new { code = "empty_update", message = "At least one of 'title', 'remark' or 'isActive' must be supplied.", traceId = HttpContext.TraceIdentifier });
        }

        if (request.Title != null)
        {
            var newTitle = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                return BadRequest(new { code = "invalid_title", message = "Field 'title' cannot be empty.", traceId = HttpContext.TraceIdentifier });
            }

            if (newTitle.Length > 200)
            {
                return BadRequest(new { code = "invalid_title", message = "Field 'title' exceeds 200 characters.", traceId = HttpContext.TraceIdentifier });
            }

            // [2026-09-13][Claude] เปลี่ยนชื่อแล้วต้องไม่ชนกับ Topic อื่นใน Target เดียวกัน (กฎ Topic Integrity เดียวกับตอนสร้าง)
            if (!string.Equals(newTitle, topic.Title, StringComparison.OrdinalIgnoreCase)
                && await _topicService.TitleExistsForTargetAsync(keyEntity.SubscriptionId, topic.Level, topic.TargetId, newTitle, topic.Id))
            {
                return Conflict(new { code = "topic_already_exists", message = "A topic with the same title already exists for this target.", traceId = HttpContext.TraceIdentifier });
            }

            topic.Title = newTitle;
        }

        if (request.Remark != null)
        {
            topic.Remark = request.Remark.Trim();
        }

        if (request.IsActive.HasValue)
        {
            topic.IsActive = request.IsActive.Value;
        }

        await _topicService.UpdateAsync(topic);
        var updated = await _topicService.GetForApiKeyScopeAsync(topicId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);

        return Ok(updated != null ? MapTopic(updated, includeEventCount: true) : MapTopic(topic, includeEventCount: true));
        #endregion
    }

    /// <summary>
    /// Soft-deletes a Topic together with its childs and events (same behaviour as the Topics screen).
    /// </summary>
    [HttpDelete("{topicKey}")]
    public async Task<IActionResult> DeleteTopic(string topicKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-13]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return Unauthorized(new { code = "invalid_api_key", message = "Invalid or expired API Key.", traceId = HttpContext.TraceIdentifier });

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.TopicPrefix, topicKey, out var topicId))
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        // [2026-09-13][Claude] อ่านผ่านเมธอดที่บังคับ Subscription + Scope ก่อนเสมอ
        //     ถ้าเรียก DeleteAsync ตรง ๆ ด้วย Id ที่ถอดจาก Key จะลบข้าม Subscription/Target ได้
        var topic = await _topicService.GetForApiKeyScopeAsync(topicId, keyEntity.SubscriptionId, keyEntity.ScopeLevel, keyEntity.ScopeTargetId);
        if (topic == null)
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        var eventCount = topic.Events.Count;
        var deleted = await _topicService.DeleteAsync(topicId);

        if (!deleted)
        {
            return NotFound(new { code = "topic_not_found", message = "Topic identifier not found or not accessible.", traceId = HttpContext.TraceIdentifier });
        }

        _logger.LogInformation("Topic ID {Id} deleted via API by key {KeyPrefix} ({EventCount} events removed).", topicId, keyEntity.KeyPrefix, eventCount);

        return Ok(new
        {
            status = "deleted",
            topicKey,
            deletedEvents = eventCount
        });
        #endregion
    }
    #endregion
}
