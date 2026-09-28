using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Identity;

namespace TU.ACAMP.WebApp.Controllers.V2;

/// <summary>
/// [2026-09-18][Claude] ฐานร่วมของ Organization REST API (Corporate / Company / Facility)
/// รวมด่านตรวจ X-API-Key + Rate limit, รูปแบบ error body และกติกา Scope ไว้ที่เดียว
/// เพื่อไม่ให้แต่ละ controller เขียนเงื่อนไขความปลอดภัยซ้ำแล้วหลุดไม่เท่ากัน
/// </summary>
public abstract class OrganizationApiControllerBase : ControllerBase
{
    #region class field
    protected const int MaxPageSize = 200;
    protected const int MaxTitleLength = 200;
    protected const int MaxCodeLength = 50;
    protected const int MaxRemarkLength = 250;

    private readonly ApiKeyService _apiKeyService;
    private readonly PlanRateLimiterService _rateLimiter;
    #endregion

    #region class constructor
    protected OrganizationApiControllerBase(ApiKeyService apiKeyService, PlanRateLimiterService rateLimiter)
    {
        #region hide [2026-09-18]
        _apiKeyService = apiKeyService;
        _rateLimiter = rateLimiter;
        #endregion
    }
    #endregion

    #region protected helpers
    /// <summary>Error body รูปแบบเดียวกับ Topics/Events API (code, message, traceId)</summary>
    protected IActionResult ApiError(int statusCode, string code, string message)
        => StatusCode(statusCode, new { code, message, traceId = HttpContext.TraceIdentifier });

    /// <summary>
    /// ด่านเดียวของทุก endpoint: ตรวจ API Key แล้วหักโควตา rate limit ทันที (คืน (Key, null) เมื่อผ่าน)
    /// </summary>
    protected async Task<(TbApiKey? Key, IActionResult? Error)> AuthorizeRequestAsync(string? apiKey)
    {
        #region hide [2026-09-18]
        var keyEntity = await _apiKeyService.ValidateApiKeyAsync(apiKey);
        if (keyEntity == null)
        {
            return (null, ApiError(401, "invalid_api_key", "Invalid or expired API Key."));
        }

        var rateCheck = _rateLimiter.CheckRateLimit(keyEntity.KeyPrefix, keyEntity.CustomRateLimitPerMin ?? 600);
        if (!rateCheck.IsAllowed)
        {
            Response.Headers.Append("Retry-After", rateCheck.RetryAfterSeconds.ToString());
            return (null, ApiError(429, rateCheck.ErrorCode, rateCheck.ErrorMessage));
        }

        return (keyEntity, null);
        #endregion
    }

    /// <summary>
    /// ตรวจสิทธิ์รายการเดียว: Key ที่ถูกจำกัด Scope เข้าถึงได้เฉพาะ "ตัวเอง" และ "ลูกหลานของตัวเอง" เท่านั้น
    /// (Key ระดับ Company จึงแตะ Corporate แม่ไม่ได้ ป้องกันการไต่ขึ้นไปแก้ข้อมูลระดับบน)
    /// </summary>
    protected IActionResult? EnsureScope(TbApiKey key, int corporateId, int? companyId = null, int? facilityId = null)
    {
        #region hide [2026-09-18]
        if (!key.ScopeLevel.HasValue || !key.ScopeTargetId.HasValue) return null;

        var target = key.ScopeTargetId.Value;
        var allowed = key.ScopeLevel.Value switch
        {
            eTopicLevel.Corporate => corporateId == target,
            eTopicLevel.Company => companyId.HasValue && companyId.Value == target,
            eTopicLevel.Facility => facilityId.HasValue && facilityId.Value == target,
            _ => false
        };

        return allowed
            ? null
            : ApiError(403, "scope_forbidden", "This API Key is restricted to its own scope and cannot address this record.");
        #endregion
    }

    /// <summary>
    /// ตรวจสิทธิ์ระดับ list: Scope ของ Key ต้องกว้างกว่าหรือเท่ากับระดับที่ขอ
    /// (Key ระดับ Facility เรียก /corporates หรือ /companies ไม่ได้)
    /// </summary>
    protected IActionResult? EnsureListScope(TbApiKey key, eTopicLevel listLevel)
    {
        #region hide [2026-09-18]
        if (!key.ScopeLevel.HasValue || !key.ScopeTargetId.HasValue) return null;

        return (int)key.ScopeLevel.Value <= (int)listLevel
            ? null
            : ApiError(403, "scope_forbidden", "This API Key is restricted to a narrower scope than the requested collection.");
        #endregion
    }

    /// <summary>บีบค่า paging ให้อยู่ในกรอบ กัน pageSize มหาศาลลากทั้งตารางออกมา</summary>
    protected static (int Page, int PageSize) NormalizePaging(int page, int pageSize)
    {
        #region hide [2026-09-18]
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > MaxPageSize) pageSize = MaxPageSize;
        return (page, pageSize);
        #endregion
    }

    /// <summary>ห่อผลลัพธ์ list ให้มีรูปแบบ paging เดียวกับ Topics API</summary>
    protected static object PagedPayload(int page, int pageSize, int totalCount, IEnumerable<object> items) => new
    {
        page,
        pageSize,
        totalCount,
        totalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0,
        items = items.ToList()
    };

    /// <summary>ตรวจความยาวฟิลด์มาตรฐาน (title / code / remark) ตามข้อจำกัดของ Entity</summary>
    protected IActionResult? ValidateCommonFields(string? title, string? code, string? remark, bool titleRequired)
    {
        #region hide [2026-09-18]
        if (titleRequired && string.IsNullOrWhiteSpace(title))
        {
            return ApiError(400, "missing_title", "Field 'title' is required.");
        }

        if (title != null && title.Trim().Length > MaxTitleLength)
        {
            return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");
        }

        if (code != null && code.Trim().Length > MaxCodeLength)
        {
            return ApiError(400, "invalid_code", $"Field 'code' exceeds {MaxCodeLength} characters.");
        }

        if (remark != null && remark.Trim().Length > MaxRemarkLength)
        {
            return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxRemarkLength} characters.");
        }

        return null;
        #endregion
    }
    #endregion
}
