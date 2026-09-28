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
/// Public REST API for Facilities / Sites (CRUD). Authenticated by X-API-Key and bound to the key's Subscription/Scope.
/// Facilities are addressed by their public FacilityKey (fac_xxx) and reference their parent by CompanyKey (cmp_xxx).
/// </summary>
[ApiController]
[Route("api/v2/facilities")]
[Produces("application/json")]
public class FacilityApiController : OrganizationApiControllerBase
{
    #region class field
    private readonly FacilityService _facilityService;
    private readonly CompanyService _companyService;
    private readonly ILogger<FacilityApiController> _logger;
    #endregion

    #region class constructor
    public FacilityApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        FacilityService facilityService,
        CompanyService companyService,
        ILogger<FacilityApiController> logger) : base(apiKeyService, rateLimiter)
    {
        #region hide [2026-09-18]
        _facilityService = facilityService;
        _companyService = companyService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    /// <summary>Payload for creating a Facility under a Company of the same subscription.</summary>
    public class CreateFacilityRequest
    {
        /// <summary>Required for an unscoped or corporate-scoped API Key; ignored when the key is scoped to a company.</summary>
        public string? CompanyKey { get; set; }

        /// <summary>Optional. Auto-generated as FAC-001, FAC-002, ... when omitted.</summary>
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? AllowShare { get; set; }
        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// Payload for updating a Facility.
    /// [2026-09-18][Claude] รับเฉพาะฟิลด์ที่ FacilityService.UpdateAsync เขียนลงฐานจริง (Code, Title, Remark, IsActive)
    /// การย้าย Facility ข้าม Company และ AllowShare ไม่เปิดผ่าน API นี้ เพราะกระทบ Scope ของ API Key
    /// </summary>
    public class UpdateFacilityRequest
    {
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? IsActive { get; set; }
    }
    #endregion

    #region private helpers
    /// <summary>Projects a Facility into the public API contract (keys only, never integer Ids).</summary>
    private static object MapFacility(TbFacility facility) => new
    {
        facilityKey = PublicKeyCodec.Encode(PublicKeyCodec.FacilityPrefix, facility.Id),
        companyKey = PublicKeyCodec.Encode(PublicKeyCodec.CompanyPrefix, facility.CompanyId),
        companyTitle = facility.Company?.Title,
        corporateKey = facility.Company != null
            ? PublicKeyCodec.Encode(PublicKeyCodec.CorporatePrefix, facility.Company.CorporateId)
            : null,
        code = facility.Code,
        title = facility.Title,
        remark = facility.Remark,
        allowShare = facility.AllowShare,
        isActive = facility.IsActive,
        orderId = facility.OrderId,
        createdAt = facility.CreatedAt,
        updatedAt = facility.UpdatedAt
    };

    /// <summary>
    /// โหลด Facility พร้อมบังคับ Subscription ของ Key และกติกา Scope
    /// ทุก endpoint ที่อ้าง facilityKey ต้องผ่านเมธอดนี้ ห้ามเรียก service ด้วย Id ดิบ
    /// </summary>
    private async Task<(TbFacility? Facility, IActionResult? Error)> LoadScopedFacilityAsync(TbApiKey keyEntity, string facilityKey)
    {
        #region hide [2026-09-18]
        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.FacilityPrefix, facilityKey, out var facilityId))
        {
            return (null, ApiError(404, "facility_not_found", "Facility identifier not found or not accessible."));
        }

        var facility = await _facilityService.GetByIdAsync(facilityId);
        if (facility?.Company?.Corporate == null || facility.Company.Corporate.SubscriptionId != keyEntity.SubscriptionId)
        {
            return (null, ApiError(404, "facility_not_found", "Facility identifier not found or not accessible."));
        }

        var scopeError = EnsureScope(keyEntity, facility.Company.CorporateId, facility.CompanyId, facility.Id);
        if (scopeError != null) return (null, scopeError);

        return (facility, null);
        #endregion
    }

    /// <summary>
    /// หา Company ปลายทางของคำขอสร้าง: Key ที่ถูกจำกัด Scope ระดับ Company ให้ยึด Scope ของ Key เสมอ
    /// </summary>
    private async Task<(TbCompany? Company, IActionResult? Error)> ResolveTargetCompanyAsync(TbApiKey keyEntity, string? companyKey)
    {
        #region hide [2026-09-18]
        int companyId;

        if (keyEntity.ScopeLevel == eTopicLevel.Company && keyEntity.ScopeTargetId.HasValue)
        {
            companyId = keyEntity.ScopeTargetId.Value;

            if (!string.IsNullOrWhiteSpace(companyKey)
                && (!PublicKeyCodec.TryDecode(PublicKeyCodec.CompanyPrefix, companyKey, out var requestedId) || requestedId != companyId))
            {
                return (null, ApiError(403, "scope_forbidden", "This API Key is restricted to its own company and cannot address another one."));
            }
        }
        else if (keyEntity.ScopeLevel == eTopicLevel.Facility && keyEntity.ScopeTargetId.HasValue)
        {
            // Key ระดับ Facility สร้าง Facility ใหม่ไม่ได้ เพราะของใหม่ย่อมอยู่นอก Scope ตัวเอง
            return (null, ApiError(403, "scope_forbidden", "A facility-scoped API Key cannot create a new facility."));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(companyKey)
                || !PublicKeyCodec.TryDecode(PublicKeyCodec.CompanyPrefix, companyKey, out companyId))
            {
                return (null, ApiError(400, "missing_company", "Field 'companyKey' is required."));
            }
        }

        var company = await _companyService.GetByIdAsync(companyId);
        if (company?.Corporate == null || company.Corporate.SubscriptionId != keyEntity.SubscriptionId)
        {
            return (null, ApiError(404, "company_not_found", "Company identifier not found or not accessible."));
        }

        // Key ระดับ Corporate สร้างได้เฉพาะภายใต้ Corporate ของตัวเอง
        var scopeError = EnsureScope(keyEntity, company.CorporateId, company.Id);
        if (scopeError != null) return (null, scopeError);

        return (company, null);
        #endregion
    }
    #endregion

    #region endpoints
    /// <summary>Lists Facilities visible to the API Key, optionally filtered by parent Company or Corporate.</summary>
    [HttpGet]
    public async Task<IActionResult> GetFacilities(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromQuery] string? companyKey = null,
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

        (page, pageSize) = NormalizePaging(page, pageSize);

        int? filterCompanyId = null;
        int? filterCorporateId = null;

        if (!string.IsNullOrWhiteSpace(companyKey))
        {
            if (!PublicKeyCodec.TryDecode(PublicKeyCodec.CompanyPrefix, companyKey, out var decodedCompanyId))
            {
                return ApiError(404, "company_not_found", "Company identifier not found or not accessible.");
            }
            filterCompanyId = decodedCompanyId;
        }

        if (!string.IsNullOrWhiteSpace(corporateKey))
        {
            if (!PublicKeyCodec.TryDecode(PublicKeyCodec.CorporatePrefix, corporateKey, out var decodedCorporateId))
            {
                return ApiError(404, "corporate_not_found", "Corporate identifier not found or not accessible.");
            }
            filterCorporateId = decodedCorporateId;
        }

        // [2026-09-18][Claude] Scope ของ Key ทับ filter ที่ผู้เรียกส่งมาเสมอ และปฏิเสธเมื่อขัดกัน
        if (keyEntity.ScopeLevel.HasValue && keyEntity.ScopeTargetId.HasValue)
        {
            var target = keyEntity.ScopeTargetId.Value;
            switch (keyEntity.ScopeLevel.Value)
            {
                case eTopicLevel.Corporate:
                    if (filterCorporateId.HasValue && filterCorporateId.Value != target)
                    {
                        return ApiError(403, "scope_forbidden", "This API Key is restricted to its own corporate and cannot address another one.");
                    }
                    filterCorporateId = target;
                    break;

                case eTopicLevel.Company:
                    if (filterCompanyId.HasValue && filterCompanyId.Value != target)
                    {
                        return ApiError(403, "scope_forbidden", "This API Key is restricted to its own company and cannot address another one.");
                    }
                    filterCompanyId = target;
                    filterCorporateId = null;
                    break;
            }
        }

        var facilities = await _facilityService.GetByContextAsync(
            keyEntity.SubscriptionId,
            filterCompanyId.HasValue ? null : filterCorporateId,
            filterCompanyId,
            includeTopics: false,
            includeInactive: includeInactive);

        if (keyEntity.ScopeLevel == eTopicLevel.Facility && keyEntity.ScopeTargetId.HasValue)
        {
            facilities = facilities.Where(f => f.Id == keyEntity.ScopeTargetId.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim();
            facilities = facilities.Where(f =>
                (f.Title ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (f.Code ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (f.Remark ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var total = facilities.Count;
        var items = facilities.Skip((page - 1) * pageSize).Take(pageSize).Select(MapFacility);

        return Ok(PagedPayload(page, pageSize, total, items));
        #endregion
    }

    /// <summary>Returns a single Facility.</summary>
    [HttpGet("{facilityKey}")]
    public async Task<IActionResult> GetFacility(string facilityKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (facility, error) = await LoadScopedFacilityAsync(keyEntity, facilityKey);
        if (error != null) return error;

        return Ok(MapFacility(facility!));
        #endregion
    }

    /// <summary>Creates a Facility under a Company. Title must be unique within that company.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateFacility([FromBody] CreateFacilityRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: true);
        if (fieldError != null) return fieldError;

        var (company, targetError) = await ResolveTargetCompanyAsync(keyEntity, request.CompanyKey);
        if (targetError != null) return targetError;

        var title = request.Title!.Trim();
        if (await _facilityService.CheckTitleExistsAsync(title, company!.Id))
        {
            return ApiError(409, "facility_already_exists", $"Facility title '{title}' already exists in this company.");
        }

        // [2026-09-18][Claude] FacilityService.CreateAsync ไม่ auto-gen Code ให้เหมือน Corporate/Company จึงต้องขอรหัสถัดไปเอง
        var code = request.Code?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            code = await _facilityService.GenerateNextCodeAsync(company.Id);
        }

        var facility = new TbFacility
        {
            CompanyId = company.Id,
            Code = code,
            Title = title,
            Remark = request.Remark?.Trim() ?? string.Empty,
            AllowShare = request.AllowShare ?? false,
            IsActive = request.IsActive ?? true
        };

        try
        {
            var created = await _facilityService.CreateAsync(facility);
            _logger.LogInformation("Facility created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);

            var result = await _facilityService.GetByIdAsync(created.Id);
            return StatusCode(201, result != null ? MapFacility(result) : MapFacility(created));
        }
        catch (KeyNotFoundException)
        {
            return ApiError(404, "company_not_found", "Company identifier not found or not accessible.");
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "facility_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>Updates the editable fields of a Facility (code, title, remark, active flag).</summary>
    [HttpPatch("{facilityKey}")]
    [HttpPut("{facilityKey}")]
    public async Task<IActionResult> UpdateFacility(string facilityKey, [FromBody] UpdateFacilityRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var (facility, error) = await LoadScopedFacilityAsync(keyEntity, facilityKey);
        if (error != null) return error;

        if (request.Code == null && request.Title == null && request.Remark == null && !request.IsActive.HasValue)
        {
            return ApiError(400, "empty_update", "At least one of 'code', 'title', 'remark' or 'isActive' must be supplied.");
        }

        var fieldError = ValidateCommonFields(request.Title, request.Code, request.Remark, titleRequired: false);
        if (fieldError != null) return fieldError;

        var entity = facility!;

        if (request.Title != null)
        {
            var newTitle = request.Title.Trim();
            if (string.IsNullOrWhiteSpace(newTitle)) return ApiError(400, "invalid_title", "Field 'title' cannot be empty.");

            if (!string.Equals(newTitle, entity.Title, StringComparison.OrdinalIgnoreCase)
                && await _facilityService.CheckTitleExistsAsync(newTitle, entity.CompanyId, entity.Id))
            {
                return ApiError(409, "facility_already_exists", $"Facility title '{newTitle}' already exists in this company.");
            }

            entity.Title = newTitle;
        }

        if (request.Code != null) entity.Code = request.Code.Trim();
        if (request.Remark != null) entity.Remark = request.Remark.Trim();
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;

        try
        {
            await _facilityService.UpdateAsync(entity);
            var updated = await _facilityService.GetByIdAsync(entity.Id);
            return Ok(MapFacility(updated ?? entity));
        }
        catch (KeyNotFoundException)
        {
            return ApiError(404, "facility_not_found", "Facility identifier not found or not accessible.");
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "facility_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>
    /// Soft-deletes a Facility together with its topics. Rejected while it still holds recorded activity events.
    /// </summary>
    [HttpDelete("{facilityKey}")]
    public async Task<IActionResult> DeleteFacility(string facilityKey, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-18]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var (facility, error) = await LoadScopedFacilityAsync(keyEntity, facilityKey);
        if (error != null) return error;

        try
        {
            var deleted = await _facilityService.DeleteAsync(facility!.Id);
            if (!deleted) return ApiError(404, "facility_not_found", "Facility identifier not found or not accessible.");

            _logger.LogInformation("Facility ID {Id} deleted via API by key {KeyPrefix}.", facility.Id, keyEntity.KeyPrefix);
            return Ok(new { status = "deleted", facilityKey });
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "facility_has_events", ex.Message);
        }
        #endregion
    }
    #endregion
}
