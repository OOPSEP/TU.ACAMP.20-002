using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Identity;
using TU.ACAMP.WebApp.Services.Organization;

namespace TU.ACAMP.WebApp.Controllers.V2;

/// <summary>
/// Public REST API for Corporates (CRUD). Authenticated by X-API-Key and bound to the key's Subscription/Scope.
/// Corporates are always addressed by their public CorporateKey (crp_xxx); database integer keys are never exposed.
/// </summary>
[ApiController]
[Route("api/v2/corporates")]
[Produces("application/json")]
public class CorporateApiController : OrganizationApiControllerBase
{
    #region class field
    private readonly CorporateService _corporateService;
    private readonly ILogger<CorporateApiController> _logger;
    #endregion

    #region class constructor
    public CorporateApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        CorporateService corporateService,
        ILogger<CorporateApiController> logger) : base(apiKeyService, rateLimiter)
    {
        #region hide [2026-09-18]
        _corporateService = corporateService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    /// <summary>Payload for creating a Corporate under the API Key's subscription.</summary>
    public class CreateCorporateRequest
    {
        /// <summary>Optional. Auto-generated as CRP-001, CRP-002, ... when omitted.</summary>
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? AllowShare { get; set; }
        public decimal? MaterialityThreshold { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// Payload for updating a Corporate.
    /// [2026-09-18][Claude] รับเฉพาะฟิลด์ที่ CorporateService.UpdateAsync เขียนลงฐานจริง (Code, Title, Remark, IsActive)
    /// AllowShare / MaterialityThreshold ตั้งได้ตอนสร้างเท่านั้น ถ้ารับตรงนี้จะกลายเป็น silent no-op
    /// </summary>
    public class UpdateCorporateRequest
    {
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? IsActive { get; set; }
    }
    #endregion

    #region private helpers
    /// <summary>Projects a Corporate into the public API contract (CorporateKey only, never the integer Id).</summary>
    private static object MapCorporate(TbCorporate corporate) => new
    {
        corporateKey = PublicKeyCodec.Encode(PublicKeyCodec.CorporatePrefix, corporate.Id),
        code = corporate.Code,
        title = corporate.Title,
        remark = corporate.Remark,
        allowShare = corporate.AllowShare,
        materialityThreshold = corporate.MaterialityThreshold,
        companyCount = corporate.Companies.Count,
        isActive = corporate.IsActive,
        orderId = corporate.OrderId,
        createdAt = corporate.CreatedAt,
        updatedAt = corporate.UpdatedAt
    };

    /// <summary>
    /// โหลด Corporate พร้อมบังคับ Subscription ของ Key และกติกา Scope
    /// ทุก endpoint ที่อ้าง corporateKey ต้องผ่านเมธอดนี้ ห้ามเรียก service ด้วย Id ดิบ
    /// </summary>
    private async Task<(TbCorporate? Corporate, IActionResult? Error)> LoadScopedCorporateAsync(TbApiKey keyEntity, string corporateKey)
    {
        #region hide [2026-09-18]
        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, corporateKey, out var corporateId))
        {
            return (null, ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible."));
        }

        var corporate = await _corporateService.GetByIdAsync(corporateId, includeCompanies: true);
        if (corporate == null || corporate.SubscriptionId != keyEntity.SubscriptionId)
        {
            return (null, ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible."));
        }

        var scopeError = EnsureScope(keyEntity, corporate.Id);
        if (scopeError != null) return (null, scopeError);

        return (corporate, null);
        #endregion
    }
    #endregion

    #region endpoints
    /// <summary>Lists Corporates visible to the API Key, with search and paging.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCorporates(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromQuery] string? q = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var listScopeError = EnsureListScope(keyEntity, eTopicLevel.Corporate);
        if (listScopeError != null) return listScopeError;

        (page, pageSize) = NormalizePaging(page, pageSize);

        var corporates = await _corporateService.GetBySubscriptionIdAsync(
            keyEntity.SubscriptionId,
            includeCompanies: true,
            includeInactive: includeInactive);

        if (keyEntity.ScopeLevel == eTopicLevel.Corporate && keyEntity.ScopeTargetId.HasValue)
        {
            corporates = corporates.Where(c => c.Id == keyEntity.ScopeTargetId.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            corporates = corporates.Where(c =>
                (c.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (c.Code ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (c.Remark ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // [2026-09-18][Claude] Corporate ต่อ Subscription มีจำนวนจำกัดตามโควตาแพ็กเกจ จึงตัดหน้าในหน่วยความจำได้
        var total = corporates.Count;
        var items = corporates.Skip((page - 1) * pageSize).Take(pageSize).Select(MapCorporate);

        return Ok(PagedPayload(page, pageSize, total, items));
        #endregion
    }

    /// <summary>Returns a single Corporate.</summary>
    [HttpGet("{corporateKey}")]
    public async Task<IActionResult> GetCorporate(string corporateKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (corporate, error) = await LoadScopedCorporateAsync(keyEntity, corporateKey);
        if (error != null) return error;

        return Ok(MapCorporate(corporate!));
        #endregion
    }

    /// <summary>Creates a Corporate under the API Key's subscription (quota enforced by the subscription plan).</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCorporate([FromBody] CreateCorporateRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        // [2026-09-18][Claude] Key ที่ถูกจำกัด Scope สร้าง Corporate ใหม่ไม่ได้ เพราะของใหม่ย่อมอยู่นอก Scope ของตัวเองเสมอ
        if (keyEntity.ScopeLevel.HasValue && keyEntity.ScopeTargetId.HasValue)
        {
            return ApiError(403, "scope_forbidden", "A scoped API Key cannot create a new corporate.");
        }

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: true);
        if (fieldError != null) return fieldError;

        if (request.MaterialityThreshold.HasValue && (request.MaterialityThreshold.Value < 0 || request.MaterialityThreshold.Value > 100))
        {
            return ApiError(400, "invalid_materiality_threshold", "Field 'materialityThreshold' must be between 0 and 100.");
        }

        var code = request.Code?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(code) && await _corporateService.CheckCodeExistsAsync(code, keyEntity.SubscriptionId))
        {
            return ApiError(409, "corporate_already_exists", $"Corporate code '{code}' already exists in this subscription.");
        }

        var corporate = new TbCorporate
        {
            SubscriptionId = keyEntity.SubscriptionId,
            Code = code,
            Title = request.Title!.Trim(),
            Remark = request.Remark?.Trim() ?? string.Empty,
            AllowShare = request.AllowShare ?? false,
            MaterialityThreshold = request.MaterialityThreshold ?? 5m,
            IsActive = request.IsActive ?? true
        };

        try
        {
            var created = await _corporateService.CreateAsync(corporate);
            _logger.LogInformation("Corporate created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);

            var result = await _corporateService.GetByIdAsync(created.Id, includeCompanies: true);
            return StatusCode(201, result != null ? MapCorporate(result) : MapCorporate(created));
        }
        catch (InvalidOperationException ex)
        {
            // ครอบทั้งกรณีโควตาเต็มและรหัสซ้ำที่ service โยนออกมา
            return ApiError(409, "corporate_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>Updates the editable fields of a Corporate (code, title, remark, active flag).</summary>
    [HttpPatch("{corporateKey}")]
    [HttpPut("{corporateKey}")]
    public async Task<IActionResult> UpdateCorporate(string corporateKey, [FromBody] UpdateCorporateRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var (corporate, error) = await LoadScopedCorporateAsync(keyEntity, corporateKey);
        if (error != null) return error;

        if (request.Code == null && request.Title == null && request.Remark == null && !request.IsActive.HasValue)
        {
            return ApiError(400, "empty_update", "At least one of 'code', 'title', 'remark' or 'isActive' must be supplied.");
        }

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: false);
        if (fieldError != null) return fieldError;

        var entity = corporate!;

        if (request.Title != null)
        {
            var newTitle = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(newTitle)) return ApiError(400, "invalid_title", "Field 'title' cannot be empty.");
            entity.Title = newTitle;
        }

        if (request.Code != null)
        {
            var newCode = request.Code.Trim();
            if (string.IsNullOrWhiteSpace(newCode)) return ApiError(400, "invalid_code", "Field 'code' cannot be empty.");

            if (!string.Equals(newCode, entity.Code, StringComparison.OrdinalIgnoreCase)
                && await _corporateService.CheckCodeExistsAsync(newCode, entity.SubscriptionId, entity.Id))
            {
                return ApiError(409, "corporate_already_exists", $"Corporate code '{newCode}' already exists in this subscription.");
            }

            entity.Code = newCode;
        }

        if (request.Remark != null) entity.Remark = request.Remark.Trim();
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;

        try
        {
            await _corporateService.UpdateAsync(entity);
            var updated = await _corporateService.GetByIdAsync(entity.Id, includeCompanies: true);
            return Ok(MapCorporate(updated ?? entity));
        }
        catch (KeyNotFoundException)
        {
            return ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible.");
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "corporate_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>
    /// Soft-deletes a Corporate. Rejected while it still owns companies (same rule as the Corporates screen).
    /// </summary>
    [HttpDelete("{corporateKey}")]
    public async Task<IActionResult> DeleteCorporate(string corporateKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (corporate, error) = await LoadScopedCorporateAsync(keyEntity, corporateKey);
        if (error != null) return error;

        try
        {
            var deleted = await _corporateService.DeleteAsync(corporate!.Id);
            if (!deleted) return ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible.");

            _logger.LogInformation("Corporate ID {Id} deleted via API by key {KeyPrefix}.", corporate.Id, keyEntity.KeyPrefix);
            return Ok(new { status = "deleted", corporateKey });
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "corporate_has_children", ex.Message);
        }
        #endregion
    }
    #endregion
}
