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
/// Public REST API for Companies (CRUD). Authenticated by X-API-Key and bound to the key's Subscription/Scope.
/// Companies are addressed by their public CompanyKey (cmp_xxx) and reference their parent by CorporateKey (crp_xxx).
/// </summary>
[ApiController]
[Route("api/v2/companies")]
[Produces("application/json")]
public class CompanyApiController : OrganizationApiControllerBase
{
    #region class field
    private readonly CompanyService _companyService;
    private readonly CorporateService _corporateService;
    private readonly ILogger<CompanyApiController> _logger;
    #endregion

    #region class constructor
    public CompanyApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        CompanyService companyService,
        CorporateService corporateService,
        ILogger<CompanyApiController> logger) : base(apiKeyService, rateLimiter)
    {
        #region hide [2026-09-18]
        _companyService = companyService;
        _corporateService = corporateService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    /// <summary>Payload for creating a Company under a Corporate of the same subscription.</summary>
    public class CreateCompanyRequest
    {
        /// <summary>Required for an unscoped API Key; ignored when the key is already scoped to a corporate.</summary>
        public string? CorporateKey { get; set; }

        /// <summary>Optional. Auto-generated as COM-001, COM-002, ... when omitted.</summary>
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }

        /// <summary>Equity share percent (0-100). Defaults to 100.</summary>
        public float? EquityShare { get; set; }
        public bool? AllowShare { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// Payload for updating a Company.
    /// [2026-09-18][Claude] รับเฉพาะฟิลด์ที่ CompanyService.UpdateAsync เขียนลงฐานจริง
    /// (Code, Title, EquityShare, Remark, IsActive) — AllowShare และการย้าย Corporate ไม่เปิดผ่าน API นี้
    /// </summary>
    public class UpdateCompanyRequest
    {
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public float? EquityShare { get; set; }
        public bool? IsActive { get; set; }
    }
    #endregion

    #region private helpers
    /// <summary>Projects a Company into the public API contract (keys only, never integer Ids).</summary>
    private static object MapCompany(TbCompany company) => new
    {
        companyKey = PublicKeyCodec.Encode(PublicKeyCodec.CompanyPrefix, company.Id),
        corporateKey = PublicKeyCodec.Encode(PublicKeyCodec.CorporatePrefix, company.CorporateId),
        corporateTitle = company.Corporate?.Title,
        code = company.Code,
        title = company.Title,
        remark = company.Remark,
        equityShare = company.EquityShare,
        allowShare = company.AllowShare,
        facilityCount = company.Facilities.Count,
        isActive = company.IsActive,
        orderId = company.OrderId,
        createdAt = company.CreatedAt,
        updatedAt = company.UpdatedAt
    };

    /// <summary>
    /// โหลด Company พร้อมบังคับ Subscription ของ Key และกติกา Scope
    /// ทุก endpoint ที่อ้าง companyKey ต้องผ่านเมธอดนี้ ห้ามเรียก service ด้วย Id ดิบ
    /// </summary>
    private async Task<(TbCompany? Company, IActionResult? Error)> LoadScopedCompanyAsync(TbApiKey keyEntity, string companyKey)
    {
        #region hide [2026-09-18]
        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.CompanyPrefix, companyKey, out var companyId))
        {
            return (null, ApiError(404, "company_not_found", "Company identifier not found or not accessible."));
        }

        var company = await _companyService.GetByIdAsync(companyId, includeFacilities: true);
        if (company == null || company.Corporate == null || company.Corporate.SubscriptionId != keyEntity.SubscriptionId)
        {
            return (null, ApiError(404, "company_not_found", "Company identifier not found or not accessible."));
        }

        var scopeError = EnsureScope(keyEntity, company.CorporateId, company.Id);
        if (scopeError != null) return (null, scopeError);

        return (company, null);
        #endregion
    }

    /// <summary>
    /// หา Corporate ปลายทางของคำขอสร้าง: Key ที่ถูกจำกัด Scope ระดับ Corporate ให้ยึด Scope ของ Key เสมอ
    /// </summary>
    private async Task<(TbCorporate? Corporate, IActionResult? Error)> ResolveTargetCorporateAsync(TbApiKey keyEntity, string? corporateKey)
    {
        #region hide [2026-09-18]
        int corporateId;

        if (keyEntity.ScopeLevel == eTopicLevel.Corporate && keyEntity.ScopeTargetId.HasValue)
        {
            corporateId = keyEntity.ScopeTargetId.Value;

            if (!string.IsNullOrWhiteSpace(corporateKey)
                && (!PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, corporateKey, out var requestedId) || requestedId != corporateId))
            {
                return (null, ApiError(403, "scope_forbidden", "This API Key is restricted to its own corporate and cannot address another one."));
            }
        }
        else if (keyEntity.ScopeLevel.HasValue && keyEntity.ScopeTargetId.HasValue)
        {
            // Key ระดับ Company/Facility สร้าง Company ใหม่ไม่ได้ เพราะของใหม่ย่อมอยู่นอก Scope ตัวเอง
            return (null, ApiError(403, "scope_forbidden", "A company- or facility-scoped API Key cannot create a new company."));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(corporateKey)
                || !PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, corporateKey, out corporateId))
            {
                return (null, ApiError(400, "missing_corporate", "Field 'corporateKey' is required for an unscoped API Key."));
            }
        }

        var corporate = await _corporateService.GetByIdAsync(corporateId);
        if (corporate == null || corporate.SubscriptionId != keyEntity.SubscriptionId)
        {
            return (null, ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible."));
        }

        return (corporate, null);
        #endregion
    }
    #endregion

    #region endpoints
    /// <summary>Lists Companies visible to the API Key, optionally filtered by parent Corporate.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCompanies(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromQuery] string? corporateKey = null,
        [FromQuery] string? q = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var listScopeError = EnsureListScope(keyEntity, eTopicLevel.Company);
        if (listScopeError != null) return listScopeError;

        (page, pageSize) = NormalizePaging(page, pageSize);

        int? filterCorporateId = null;
        if (!string.IsNullOrWhiteSpace(corporateKey))
        {
            if (!PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, corporateKey, out var decodedCorporateId))
            {
                return ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible.");
            }
            filterCorporateId = decodedCorporateId;
        }

        if (keyEntity.ScopeLevel == eTopicLevel.Corporate && keyEntity.ScopeTargetId.HasValue)
        {
            if (filterCorporateId.HasValue && filterCorporateId.Value != keyEntity.ScopeTargetId.Value)
            {
                return ApiError(403, "scope_forbidden", "This API Key is restricted to its own corporate and cannot address another one.");
            }
            filterCorporateId = keyEntity.ScopeTargetId.Value;
        }

        var companies = await _companyService.GetByCorporateIdAsync(
            filterCorporateId,
            keyEntity.SubscriptionId,
            includeFacilities: true,
            includeInactive: includeInactive);

        if (keyEntity.ScopeLevel == eTopicLevel.Company && keyEntity.ScopeTargetId.HasValue)
        {
            companies = companies.Where(c => c.Id == keyEntity.ScopeTargetId.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            companies = companies.Where(c =>
                (c.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (c.Code ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (c.Remark ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var total = companies.Count;
        var items = companies.Skip((page - 1) * pageSize).Take(pageSize).Select(MapCompany);

        return Ok(PagedPayload(page, pageSize, total, items));
        #endregion
    }

    /// <summary>Returns a single Company.</summary>
    [HttpGet("{companyKey}")]
    public async Task<IActionResult> GetCompany(string companyKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (company, error) = await LoadScopedCompanyAsync(keyEntity, companyKey);
        if (error != null) return error;

        return Ok(MapCompany(company!));
        #endregion
    }

    /// <summary>Creates a Company under a Corporate (quota enforced by the subscription plan).</summary>
    [HttpPost]
    public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: true);
        if (fieldError != null) return fieldError;

        if (request.EquityShare.HasValue && (request.EquityShare.Value < 0f || request.EquityShare.Value > 100f))
        {
            return ApiError(400, "invalid_equity_share", "Field 'equityShare' must be between 0 and 100.");
        }

        var (corporate, targetError) = await ResolveTargetCorporateAsync(keyEntity, request.CorporateKey);
        if (targetError != null) return targetError;

        var code = request.Code?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(code) && await _companyService.CheckCodeExistsAsync(code, corporate!.Id))
        {
            return ApiError(409, "company_already_exists", $"Company code '{code}' already exists in this corporate group.");
        }

        var company = new TbCompany
        {
            CorporateId = corporate!.Id,
            Code = code,
            Title = request.Title!.Trim(),
            Remark = request.Remark?.Trim() ?? string.Empty,
            EquityShare = request.EquityShare ?? 100f,
            AllowShare = request.AllowShare ?? false,
            IsActive = request.IsActive ?? true
        };

        try
        {
            var created = await _companyService.CreateAsync(company);
            _logger.LogInformation("Company created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);

            var result = await _companyService.GetByIdAsync(created.Id, includeFacilities: true);
            return StatusCode(201, result != null ? MapCompany(result) : MapCompany(created));
        }
        catch (KeyNotFoundException)
        {
            return ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible.");
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "company_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>Updates the editable fields of a Company (code, title, remark, equity share, active flag).</summary>
    [HttpPatch("{companyKey}")]
    [HttpPut("{companyKey}")]
    public async Task<IActionResult> UpdateCompany(string companyKey, [FromBody] UpdateCompanyRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var (company, error) = await LoadScopedCompanyAsync(keyEntity, companyKey);
        if (error != null) return error;

        if (request.Code == null && request.Title == null && request.Remark == null
            && !request.EquityShare.HasValue && !request.IsActive.HasValue)
        {
            return ApiError(400, "empty_update", "At least one of 'code', 'title', 'remark', 'equityShare' or 'isActive' must be supplied.");
        }

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: false);
        if (fieldError != null) return fieldError;

        var entity = company!;

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
                && await _companyService.CheckCodeExistsAsync(newCode, entity.CorporateId, entity.Id))
            {
                return ApiError(409, "company_already_exists", $"Company code '{newCode}' already exists in this corporate group.");
            }

            entity.Code = newCode;
        }

        if (request.EquityShare.HasValue)
        {
            if (request.EquityShare.Value < 0f || request.EquityShare.Value > 100f)
            {
                return ApiError(400, "invalid_equity_share", "Field 'equityShare' must be between 0 and 100.");
            }
            entity.EquityShare = request.EquityShare.Value;
        }

        if (request.Remark != null) entity.Remark = request.Remark.Trim();
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;

        try
        {
            await _companyService.UpdateAsync(entity);
            var updated = await _companyService.GetByIdAsync(entity.Id, includeFacilities: true);
            return Ok(MapCompany(updated ?? entity));
        }
        catch (KeyNotFoundException)
        {
            return ApiError(404, "company_not_found", "Company identifier not found or not accessible.");
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "company_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>
    /// Soft-deletes a Company. Rejected while it still owns facilities (same rule as the Companies screen).
    /// </summary>
    [HttpDelete("{companyKey}")]
    public async Task<IActionResult> DeleteCompany(string companyKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (company, error) = await LoadScopedCompanyAsync(keyEntity, companyKey);
        if (error != null) return error;

        try
        {
            var deleted = await _companyService.DeleteAsync(company!.Id);
            if (!deleted) return ApiError(404, "company_not_found", "Company identifier not found or not accessible.");

            _logger.LogInformation("Company ID {Id} deleted via API by key {KeyPrefix}.", company.Id, keyEntity.KeyPrefix);
            return Ok(new { status = "deleted", companyKey });
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "company_has_children", ex.Message);
        }
        #endregion
    }
    #endregion
}
