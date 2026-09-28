using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Services;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Emissions;
using TU.ACAMP.WebApp.Services.Identity;

namespace TU.ACAMP.WebApp.Controllers.V2;

/// <summary>
/// [2026-09-23][Claude] Public REST API สำหรับ Emission Library ของ Subscription (ยืนยันตัวด้วย X-API-Key)
/// เฟสนี้: Emission Units (GET รายการ Default + Custom, POST สร้าง Custom, PATCH แก้ Custom)
/// [2026-09-23][Claude] เพิ่ม Emission Groups (GET / POST / PATCH api/v2/emission-groups) อ้างอิงด้วย GroupKey (egp_xxx)
/// [2026-09-23][Claude] เพิ่ม Emission Sources (GET / POST / PATCH api/v2/emission-sources) อ้างอิงด้วย SourceKey (esr_xxx), Group/Unit อ้างด้วย Key
/// อ้างอิงด้วย UnitKey (unt_xxx) เท่านั้น ไม่เปิด integer Id; รายการ Default (ระบบ) แก้ไขผ่าน API ไม่ได้
/// </summary>
[ApiController]
[Route("api/v2")]
[Produces("application/json")]
public class EmissionLibraryApiController : OrganizationApiControllerBase
{
    #region class field
    private const int MaxUnitRemarkLength = 500;
    private const int MaxGroupRemarkLength = 250; // [2026-09-23][Claude] ตาม [StringLength(250)] ของ TbEmissionGroup.Remark
    private const int MaxSourceRemarkLength = 500; // [2026-09-23][Claude] ตาม [StringLength(500)] ของ TbEmissionSource.Remark
    private readonly EmissionUnitService _unitService;
    private readonly EmissionGroupService _groupService;
    private readonly EmissionSourceService _sourceService;
    private readonly ILogger<EmissionLibraryApiController> _logger;
    #endregion

    #region class constructor
    public EmissionLibraryApiController(
        ApiKeyService apiKeyService,
        PlanRateLimiterService rateLimiter,
        EmissionUnitService unitService,
        EmissionGroupService groupService,
        EmissionSourceService sourceService,
        ILogger<EmissionLibraryApiController> logger) : base(apiKeyService, rateLimiter)
    {
        #region hide [2026-09-23]
        _unitService = unitService;
        _groupService = groupService;
        _sourceService = sourceService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    public class CreateUnitRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }
    }

    public class UpdateUnitRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>[2026-09-23][Claude] scope = 1-4 (Scope1, Scope2, Scope3, SeparateReport)</summary>
    public class CreateGroupRequest
    {
        public string? Title { get; set; }
        public int? Scope { get; set; }
        public string? Remark { get; set; }
    }

    public class UpdateGroupRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>
    /// [2026-09-23][Claude] สัญญาเดียวกันทั้ง POST และ PATCH — PATCH: ฟิลด์ null = ไม่แก้
    /// POST: title / groupKey / unitKey บังคับ, ปริมาณก๊าซไม่ส่ง = 0, ค่า GWP ไม่ส่ง = ค่ามาตรฐานของ gwpVersion (ค่าเริ่มต้น AR5)
    /// </summary>
    public class SourceRequest
    {
        public string? Title { get; set; }
        public string? GroupKey { get; set; }
        public string? UnitKey { get; set; }
        public string? Remark { get; set; }
        public string? Ref { get; set; }
        public decimal? CO2 { get; set; }
        public decimal? FossilCH4 { get; set; }
        public decimal? CH4 { get; set; }
        public decimal? N2O { get; set; }
        public decimal? SF6 { get; set; }
        public decimal? NF3 { get; set; }
        public decimal? HFCs { get; set; }
        public decimal? PFCs { get; set; }
        public decimal? GwpCO2 { get; set; }
        public decimal? GwpFossilCH4 { get; set; }
        public decimal? GwpCH4 { get; set; }
        public decimal? GwpN2O { get; set; }
        public decimal? GwpSF6 { get; set; }
        public decimal? GwpNF3 { get; set; }
        public decimal? GwpHFCs { get; set; }
        public decimal? GwpPFCs { get; set; }
        /// <summary>AR4 / AR5 / AR6</summary>
        public string? GwpVersion { get; set; }
    }
    #endregion

    #region private helpers
    private static object MapUnit(TbEmissionUnit unit) => new
    {
        unitKey = PublicKeyCodec.Encode(PublicKeyCodec.UnitPrefix, unit.Id),
        title = unit.Title,
        remark = unit.Remark,
        sourceType = unit.SourceType.ToString(),
        isActive = unit.IsActive
    };

    private static string NormalizeTitle(string? title) => EmissionUnitExcelService.TrimTitle(title);

    /// <summary>[2026-09-23][Claude] scope ส่งเป็นเลข 1-4 (ตรงกับ migration-report)</summary>
    private static object MapGroup(TbEmissionGroup group) => new
    {
        groupKey = PublicKeyCodec.Encode(PublicKeyCodec.EmissionGroupPrefix, group.Id),
        code = group.Code,
        title = group.Title,
        titleTh = group.TitleTh, // [2026-09-23][Claude] ให้ MigrationTool จับคู่กลุ่ม Default ของ V16 (ชื่อไทย) ได้
        scope = (int)group.TgoScopeNo,
        remark = group.Remark,
        sourceType = group.SourceType.ToString(),
        isActive = group.IsActive
    };

    private static string NormalizeGroupTitle(string? title) => EmissionGroupExcelService.TrimTitle(title);

    /// <summary>[2026-09-23][Claude] scope มาจาก Group ของ Source; ef = ผลรวม ก๊าซ x GWP (TbEmissionSource.EF)</summary>
    private static object MapSource(TbEmissionSource source) => new
    {
        sourceKey = PublicKeyCodec.Encode(PublicKeyCodec.EmissionSourcePrefix, source.Id),
        title = source.Title,
        groupKey = PublicKeyCodec.Encode(PublicKeyCodec.EmissionGroupPrefix, source.GroupId),
        groupTitle = source.Group?.Title ?? string.Empty,
        scope = (int)source.TgoScopeNo,
        unitKey = PublicKeyCodec.Encode(PublicKeyCodec.UnitPrefix, source.UnitId),
        unitTitle = source.Unit?.Title ?? string.Empty,
        co2 = source.CO2,
        fossilCH4 = source.FossilCH4,
        ch4 = source.CH4,
        n2o = source.N2O,
        sf6 = source.SF6,
        nf3 = source.NF3,
        hfcs = source.HFCs,
        pfcs = source.PFCs,
        gwpCO2 = source.gwpCO2,
        gwpFossilCH4 = source.gwpFossilCH4,
        gwpCH4 = source.gwpCH4,
        gwpN2O = source.gwpN2O,
        gwpSF6 = source.gwpSF6,
        gwpNF3 = source.gwpNF3,
        gwpHFCs = source.gwpHFCs,
        gwpPFCs = source.gwpPFCs,
        gwpVersion = source.GwpVersion.ToString(),
        ef = source.EF,
        @ref = source.Ref,
        remark = source.Remark,
        sourceType = source.SourceType.ToString(),
        isActive = source.IsActive
    };

    /// <summary>
    /// [2026-09-23][Claude] ค่า GWP มาตรฐานตามรุ่น IPCC (ชุดเดียวกับ EmissionFactorWidget) — HFCs/PFCs = 1 (ใส่ค่า CO2e มาแล้ว) ตาม Excel Import
    /// ลำดับ: CO2, FossilCH4, CH4, N2O, SF6, NF3, HFCs, PFCs
    /// </summary>
    private static decimal[] GetGwpPreset(eGwpVersion version) => version switch
    {
        eGwpVersion.AR4 => new[] { 1m, 25m, 25m, 298m, 22800m, 17200m, 1m, 1m },
        eGwpVersion.AR6 => new[] { 1m, 29.8m, 27.2m, 273m, 25200m, 17400m, 1m, 1m },
        _ => new[] { 1m, 30m, 28m, 265m, 23500m, 16100m, 1m, 1m }
    };

    /// <summary>
    /// [2026-09-23][Claude] ตรวจและนำค่าจาก request ลง entity (ใช้ทั้ง POST และ PATCH) — คืน IActionResult เมื่อไม่ผ่าน
    /// isCreate = true: ค่า null ของก๊าซ = 0, ของ GWP = preset ของ gwpVersion; PATCH: null = คงค่าเดิม
    /// </summary>
    private IActionResult? ApplySourceValues(SourceRequest request, TbEmissionSource source, bool isCreate)
    {
        #region hide [2026-09-23]
        if (request.GwpVersion != null)
        {
            if (!Enum.TryParse<eGwpVersion>(request.GwpVersion.Trim(), ignoreCase: true, out var version) || !Enum.IsDefined(typeof(eGwpVersion), version))
            {
                return ApiError(400, "invalid_gwp_version", "Field 'gwpVersion' must be AR4, AR5 or AR6.");
            }
            source.GwpVersion = version;
        }
        else if (isCreate)
        {
            source.GwpVersion = eGwpVersion.AR5;
        }

        var gases = new (string Name, decimal? Value, Action<decimal> Set)[]
        {
            ("co2", request.CO2, v => source.CO2 = v),
            ("fossilCH4", request.FossilCH4, v => source.FossilCH4 = v),
            ("ch4", request.CH4, v => source.CH4 = v),
            ("n2o", request.N2O, v => source.N2O = v),
            ("sf6", request.SF6, v => source.SF6 = v),
            ("nf3", request.NF3, v => source.NF3 = v),
            ("hfcs", request.HFCs, v => source.HFCs = v),
            ("pfcs", request.PFCs, v => source.PFCs = v)
        };
        foreach (var (name, value, set) in gases)
        {
            if (value.HasValue && value.Value < 0) return ApiError(400, "invalid_gas_value", $"Field '{name}' must not be negative.");
            if (value.HasValue) set(value.Value);
            else if (isCreate) set(0m);
        }

        var preset = GetGwpPreset(source.GwpVersion);
        var gwps = new (string Name, decimal? Value, Action<decimal> Set)[]
        {
            ("gwpCO2", request.GwpCO2, v => source.gwpCO2 = v),
            ("gwpFossilCH4", request.GwpFossilCH4, v => source.gwpFossilCH4 = v),
            ("gwpCH4", request.GwpCH4, v => source.gwpCH4 = v),
            ("gwpN2O", request.GwpN2O, v => source.gwpN2O = v),
            ("gwpSF6", request.GwpSF6, v => source.gwpSF6 = v),
            ("gwpNF3", request.GwpNF3, v => source.gwpNF3 = v),
            ("gwpHFCs", request.GwpHFCs, v => source.gwpHFCs = v),
            ("gwpPFCs", request.GwpPFCs, v => source.gwpPFCs = v)
        };
        for (int i = 0; i < gwps.Length; i++)
        {
            var (name, value, set) = gwps[i];
            if (value.HasValue && value.Value < 0) return ApiError(400, "invalid_gwp_value", $"Field '{name}' must not be negative.");
            if (value.HasValue) set(value.Value);
            else if (isCreate) set(preset[i]);
        }
        // [GHG-07] gwpCO2 ต้องมากกว่า 0 (service ตรวจซ้ำ)
        if (source.gwpCO2 <= 0) return ApiError(400, "invalid_gwp_value", "Field 'gwpCO2' must be greater than 0.");

        if (request.Remark != null)
        {
            var remark = request.Remark.Trim();
            if (remark.Length > MaxSourceRemarkLength) return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxSourceRemarkLength} characters.");
            source.Remark = remark;
        }
        if (request.Ref != null)
        {
            source.Ref = request.Ref.Trim();
        }
        return null;
        #endregion
    }

    /// <summary>[2026-09-23][Claude] หา Source ที่ Title ซ้ำ (ไม่สนตัวพิมพ์) ในรายการที่ Subscription มองเห็น — กติกาเดียวกับ Excel Import</summary>
    private static TbEmissionSource? FindDuplicateSource(IEnumerable<TbEmissionSource> visible, string title, int? excludeId = null)
        => visible.FirstOrDefault(s => s.Id != excludeId
            && string.Equals(EmissionSourceExcelService.TrimTitle(s.Title), title, StringComparison.OrdinalIgnoreCase));

    /// <summary>[2026-09-23][Claude] หา Group / Unit จาก Key เฉพาะรายการที่ Subscription ใช้ได้ (Default + Custom ของตัวเอง, active)</summary>
    private async Task<(TbEmissionGroup? Group, IActionResult? Error)> ResolveGroupAsync(string groupKey, int subscriptionId)
    {
        #region hide [2026-09-23]
        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.EmissionGroupPrefix, groupKey, out var groupId))
        {
            return (null, ApiError(400, "invalid_group_key", "Field 'groupKey' is not a valid emission group key."));
        }
        var groups = await _groupService.GetBySubscriptionIdAsync(subscriptionId, includeDefault: true);
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        return group == null
            ? (null, ApiError(400, "invalid_group_key", "Emission group not found or not accessible for this subscription."))
            : (group, null);
        #endregion
    }

    private async Task<(TbEmissionUnit? Unit, IActionResult? Error)> ResolveUnitAsync(string unitKey, int subscriptionId)
    {
        #region hide [2026-09-23]
        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.UnitPrefix, unitKey, out var unitId))
        {
            return (null, ApiError(400, "invalid_unit_key", "Field 'unitKey' is not a valid emission unit key."));
        }
        var units = await _unitService.GetBySubscriptionIdAsync(subscriptionId, includeDefault: true);
        var unit = units.FirstOrDefault(u => u.Id == unitId);
        return unit == null
            ? (null, ApiError(400, "invalid_unit_key", "Emission unit not found or not accessible for this subscription."))
            : (unit, null);
        #endregion
    }

    /// <summary>[2026-09-23][Claude] หา Group ที่ Title + Scope ซ้ำ (ไม่สนตัวพิมพ์) ในรายการที่ Subscription มองเห็น</summary>
    private static TbEmissionGroup? FindDuplicateGroup(IEnumerable<TbEmissionGroup> visible, string title, eTgoScopeNo scope, int? excludeId = null)
        => visible.FirstOrDefault(g => g.Id != excludeId && g.TgoScopeNo == scope
            && string.Equals(NormalizeGroupTitle(g.Title), title, StringComparison.OrdinalIgnoreCase));

    /// <summary>Key ที่ถูกจำกัด Scope แก้ไข Library ระดับ Subscription ไม่ได้</summary>
    private IActionResult? EnsureUnscopedKey(TbApiKey keyEntity)
        => keyEntity.ScopeLevel.HasValue && keyEntity.ScopeTargetId.HasValue
            ? ApiError(403, "scope_forbidden", "A scoped API Key cannot modify the subscription emission library.")
            : null;
    #endregion

    #region endpoints - emission units
    /// <summary>GET api/v2/emission-units?includeDefault=true — หน่วยที่ Subscription ใช้ได้ (Default + Custom)</summary>
    [HttpGet("emission-units")]
    public async Task<IActionResult> GetUnits([FromHeader(Name = "X-API-Key")] string? apiKey, [FromQuery] bool includeDefault = true)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var units = await _unitService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault);
        var items = units.Select(MapUnit).ToList();
        return Ok(new { totalCount = items.Count, items });
        #endregion
    }

    /// <summary>POST api/v2/emission-units — สร้างหน่วย Custom (409 unit_already_exists พร้อมข้อมูลตัวเดิม เมื่อชื่อซ้ำ Default หรือ Custom)</summary>
    [HttpPost("emission-units")]
    public async Task<IActionResult> CreateUnit([FromBody] CreateUnitRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");
        var title = NormalizeTitle(request.Title);
        if (title.Length == 0) return ApiError(400, "missing_title", "Field 'title' is required.");
        if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");
        var remark = request.Remark?.Trim() ?? string.Empty;
        if (remark.Length > MaxUnitRemarkLength) return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxUnitRemarkLength} characters.");

        var visible = await _unitService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
        var duplicate = visible.FirstOrDefault(u => string.Equals(NormalizeTitle(u.Title), title, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            return StatusCode(409, new
            {
                code = "unit_already_exists",
                message = $"Emission unit '{title}' already exists ({duplicate.SourceType}).",
                unit = MapUnit(duplicate),
                traceId = HttpContext.TraceIdentifier
            });
        }

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var created = await _unitService.CreateAsync(new TbEmissionUnit
            {
                Title = title,
                Remark = remark,
                SubscriptionId = keyEntity.SubscriptionId
            });
            _logger.LogInformation("Emission unit created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);
            return StatusCode(201, MapUnit(created));
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "unit_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>PATCH api/v2/emission-units/{unitKey} — แก้หน่วย Custom ของ Subscription (ฟิลด์ null = ไม่แก้)</summary>
    [HttpPatch("emission-units/{unitKey}")]
    public async Task<IActionResult> UpdateUnit(string unitKey, [FromBody] UpdateUnitRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null || (request.Title == null && request.Remark == null))
        {
            return ApiError(400, "invalid_payload", "At least one of 'title' or 'remark' is required.");
        }

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.UnitPrefix, unitKey, out var unitId))
        {
            return ApiError(404, "unit_not_found", "Emission unit identifier not found or not accessible.");
        }

        var unit = await _unitService.GetByIdAsync(unitId);
        if (unit == null || unit.SourceType != eSourceType.Custom || unit.SubscriptionId != keyEntity.SubscriptionId)
        {
            return ApiError(404, "unit_not_found", "Emission unit identifier not found or not accessible (system default units are read-only).");
        }

        if (request.Title != null)
        {
            var title = NormalizeTitle(request.Title);
            if (title.Length == 0) return ApiError(400, "invalid_title", "Field 'title' cannot be empty.");
            if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");
            unit.Title = title;
        }
        if (request.Remark != null)
        {
            var remark = request.Remark.Trim();
            if (remark.Length > MaxUnitRemarkLength) return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxUnitRemarkLength} characters.");
            unit.Remark = remark;
        }

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var updated = await _unitService.UpdateAsync(unit);
            return Ok(MapUnit(updated));
        }
        catch (InvalidOperationException ex)
        {
            // ชื่อซ้ำกับหน่วยอื่น (Default หรือ Custom)
            return ApiError(409, "unit_rejected", ex.Message);
        }
        #endregion
    }
    #endregion

    #region endpoints - emission groups
    /// <summary>[2026-09-23][Claude] GET api/v2/emission-groups?includeDefault=true — กลุ่มที่ Subscription ใช้ได้ (Default + Custom)</summary>
    [HttpGet("emission-groups")]
    public async Task<IActionResult> GetGroups([FromHeader(Name = "X-API-Key")] string? apiKey, [FromQuery] bool includeDefault = true)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var groups = await _groupService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault);
        var items = groups.Select(MapGroup).ToList();
        return Ok(new { totalCount = items.Count, items });
        #endregion
    }

    /// <summary>
    /// [2026-09-23][Claude] POST api/v2/emission-groups — สร้างกลุ่ม Custom
    /// Code สร้างอัตโนมัติแบบเดียวกับหน้าจอ (namespace Custom เช่น 03C01), IsoCategoryNo = Category1 ตามค่าเริ่มต้นของหน้าจอ/Import
    /// 409 group_already_exists พร้อมข้อมูลตัวเดิม เมื่อ Title + Scope ซ้ำกับ Default หรือ Custom
    /// </summary>
    [HttpPost("emission-groups")]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");
        var title = NormalizeGroupTitle(request.Title);
        if (title.Length == 0) return ApiError(400, "missing_title", "Field 'title' is required.");
        if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");
        if (!request.Scope.HasValue || !Enum.IsDefined(typeof(eTgoScopeNo), request.Scope.Value))
        {
            return ApiError(400, "invalid_scope", "Field 'scope' is required and must be 1-4 (1 = Scope1, 2 = Scope2, 3 = Scope3, 4 = SeparateReport).");
        }
        var scope = (eTgoScopeNo)request.Scope.Value;
        var remark = request.Remark?.Trim() ?? string.Empty;
        if (remark.Length > MaxGroupRemarkLength) return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxGroupRemarkLength} characters.");

        var visible = await _groupService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
        var duplicate = FindDuplicateGroup(visible, title, scope);
        if (duplicate != null)
        {
            return StatusCode(409, new
            {
                code = "group_already_exists",
                message = $"Emission group '{title}' already exists in scope {(int)scope} ({duplicate.SourceType}).",
                group = MapGroup(duplicate),
                traceId = HttpContext.TraceIdentifier
            });
        }

        var customCodes = visible.Where(g => g.SourceType == eSourceType.Custom).Select(g => g.Code);
        var slot = EmissionGroupExcelService.TakeNextFreeSlot(EmissionGroupExcelService.GetUsedCodeNumbers(customCodes, scope, custom: true));
        if (slot <= 0) return ApiError(409, "group_code_exhausted", $"No available custom group code left for scope {(int)scope} (limit 99).");

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var created = await _groupService.CreateAsync(new TbEmissionGroup
            {
                Code = EmissionGroupExcelService.BuildCode(scope, slot, custom: true),
                Title = title,
                TgoScopeNo = scope,
                IsoCategoryNo = eIsoCategoryNo.Category1,
                Remark = remark,
                SubscriptionId = keyEntity.SubscriptionId,
                SourceType = eSourceType.Custom
            });
            _logger.LogInformation("Emission group created via API: '{Title}' (ID: {Id}, Code: {Code}) by key {KeyPrefix}", created.Title, created.Id, created.Code, keyEntity.KeyPrefix);
            return StatusCode(201, MapGroup(created));
        }
        catch (InvalidOperationException ex)
        {
            // Code ชนกับกลุ่มที่ inactive (ไม่อยู่ในรายการที่มองเห็น) หรือเงื่อนไขอื่นของ service
            return ApiError(409, "group_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>[2026-09-23][Claude] PATCH api/v2/emission-groups/{groupKey} — แก้กลุ่ม Custom ของ Subscription (ฟิลด์ null = ไม่แก้; Scope/Code แก้ไม่ได้)</summary>
    [HttpPatch("emission-groups/{groupKey}")]
    public async Task<IActionResult> UpdateGroup(string groupKey, [FromBody] UpdateGroupRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null || (request.Title == null && request.Remark == null))
        {
            return ApiError(400, "invalid_payload", "At least one of 'title' or 'remark' is required.");
        }

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.EmissionGroupPrefix, groupKey, out var groupId))
        {
            return ApiError(404, "group_not_found", "Emission group identifier not found or not accessible.");
        }

        var group = await _groupService.GetByIdAsync(groupId);
        if (group == null || group.SourceType != eSourceType.Custom || group.SubscriptionId != keyEntity.SubscriptionId)
        {
            return ApiError(404, "group_not_found", "Emission group identifier not found or not accessible (system default groups are read-only).");
        }

        if (request.Title != null)
        {
            var title = NormalizeGroupTitle(request.Title);
            if (title.Length == 0) return ApiError(400, "invalid_title", "Field 'title' cannot be empty.");
            if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");

            var visible = await _groupService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
            var duplicate = FindDuplicateGroup(visible, title, group.TgoScopeNo, excludeId: group.Id);
            if (duplicate != null)
            {
                return StatusCode(409, new
                {
                    code = "group_already_exists",
                    message = $"Emission group '{title}' already exists in scope {(int)group.TgoScopeNo} ({duplicate.SourceType}).",
                    group = MapGroup(duplicate),
                    traceId = HttpContext.TraceIdentifier
                });
            }
            group.Title = title;
        }
        if (request.Remark != null)
        {
            var remark = request.Remark.Trim();
            if (remark.Length > MaxGroupRemarkLength) return ApiError(400, "invalid_remark", $"Field 'remark' exceeds {MaxGroupRemarkLength} characters.");
            group.Remark = remark;
        }

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var updated = await _groupService.UpdateAsync(group);
            return Ok(MapGroup(updated));
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "group_rejected", ex.Message);
        }
        #endregion
    }
    #endregion

    #region endpoints - emission sources
    /// <summary>[2026-09-23][Claude] GET api/v2/emission-sources?includeDefault=true — Source ที่ Subscription ใช้ได้ (Default + Custom)</summary>
    [HttpGet("emission-sources")]
    public async Task<IActionResult> GetSources([FromHeader(Name = "X-API-Key")] string? apiKey, [FromQuery] bool includeDefault = true)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");

        var sources = await _sourceService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault);
        var items = sources.Select(MapSource).ToList();
        return Ok(new { totalCount = items.Count, items });
        #endregion
    }

    /// <summary>
    /// [2026-09-23][Claude] POST api/v2/emission-sources — สร้าง Source Custom
    /// Title ซ้ำกับ Default หรือ Custom (ไม่สนตัวพิมพ์) → 409 source_already_exists พร้อมข้อมูลตัวเดิม
    /// </summary>
    [HttpPost("emission-sources")]
    public async Task<IActionResult> CreateSource([FromBody] SourceRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");
        var title = EmissionSourceExcelService.TrimTitle(request.Title);
        if (title.Length == 0) return ApiError(400, "missing_title", "Field 'title' is required.");
        if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");
        if (string.IsNullOrWhiteSpace(request.GroupKey)) return ApiError(400, "missing_group_key", "Field 'groupKey' is required.");
        if (string.IsNullOrWhiteSpace(request.UnitKey)) return ApiError(400, "missing_unit_key", "Field 'unitKey' is required.");

        var (group, groupError) = await ResolveGroupAsync(request.GroupKey, keyEntity.SubscriptionId);
        if (groupError != null) return groupError;
        var (unit, unitError) = await ResolveUnitAsync(request.UnitKey, keyEntity.SubscriptionId);
        if (unitError != null) return unitError;

        var source = new TbEmissionSource
        {
            Title = title,
            GroupId = group!.Id,
            UnitId = unit!.Id,
            SubscriptionId = keyEntity.SubscriptionId,
            SourceType = eSourceType.Custom
        };
        var valueError = ApplySourceValues(request, source, isCreate: true);
        if (valueError != null) return valueError;

        var visible = await _sourceService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
        var duplicate = FindDuplicateSource(visible, title);
        if (duplicate != null)
        {
            return StatusCode(409, new
            {
                code = "source_already_exists",
                message = $"Emission source '{title}' already exists ({duplicate.SourceType}).",
                source = MapSource(duplicate),
                traceId = HttpContext.TraceIdentifier
            });
        }

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var created = await _sourceService.CreateAsync(source);
            _logger.LogInformation("Emission source created via API: '{Title}' (ID: {Id}) by key {KeyPrefix}", created.Title, created.Id, keyEntity.KeyPrefix);
            // โหลดใหม่พร้อม Group/Unit เพื่อให้ scope / groupTitle / unitTitle ถูกต้อง
            var reloaded = await _sourceService.GetByIdAsync(created.Id) ?? created;
            return StatusCode(201, MapSource(reloaded));
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "source_rejected", ex.Message);
        }
        #endregion
    }

    /// <summary>[2026-09-23][Claude] PATCH api/v2/emission-sources/{sourceKey} — แก้ Source Custom ของ Subscription (ฟิลด์ null = ไม่แก้)</summary>
    [HttpPatch("emission-sources/{sourceKey}")]
    public async Task<IActionResult> UpdateSource(string sourceKey, [FromBody] SourceRequest request, [FromHeader(Name = "X-API-Key")] string? apiKey)
    {
        #region hide [2026-09-23]
        var (keyEntity, authError) = await AuthorizeRequestAsync(apiKey);
        if (authError != null) return authError;
        if (keyEntity == null) return ApiError(401, "invalid_api_key", "Invalid or expired API Key.");
        var scopeError = EnsureUnscopedKey(keyEntity);
        if (scopeError != null) return scopeError;

        if (request == null || !typeof(SourceRequest).GetProperties().Any(p => p.GetValue(request) != null))
        {
            return ApiError(400, "invalid_payload", "At least one field is required.");
        }

        if (!PublicKeyCodec.TryDecode(PublicKeyCodec.EmissionSourcePrefix, sourceKey, out var sourceId))
        {
            return ApiError(404, "source_not_found", "Emission source identifier not found or not accessible.");
        }

        var source = await _sourceService.GetByIdAsync(sourceId);
        if (source == null || source.SourceType != eSourceType.Custom || source.SubscriptionId != keyEntity.SubscriptionId)
        {
            return ApiError(404, "source_not_found", "Emission source identifier not found or not accessible (system default sources are read-only).");
        }

        if (request.Title != null)
        {
            var title = EmissionSourceExcelService.TrimTitle(request.Title);
            if (title.Length == 0) return ApiError(400, "invalid_title", "Field 'title' cannot be empty.");
            if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");

            var visible = await _sourceService.GetBySubscriptionIdAsync(keyEntity.SubscriptionId, includeDefault: true);
            var duplicate = FindDuplicateSource(visible, title, excludeId: source.Id);
            if (duplicate != null)
            {
                return StatusCode(409, new
                {
                    code = "source_already_exists",
                    message = $"Emission source '{title}' already exists ({duplicate.SourceType}).",
                    source = MapSource(duplicate),
                    traceId = HttpContext.TraceIdentifier
                });
            }
            source.Title = title;
        }
        if (request.GroupKey != null)
        {
            var (group, groupError) = await ResolveGroupAsync(request.GroupKey, keyEntity.SubscriptionId);
            if (groupError != null) return groupError;
            source.GroupId = group!.Id;
        }
        if (request.UnitKey != null)
        {
            var (unit, unitError) = await ResolveUnitAsync(request.UnitKey, keyEntity.SubscriptionId);
            if (unitError != null) return unitError;
            source.UnitId = unit!.Id;
        }

        var valueError = ApplySourceValues(request, source, isCreate: false);
        if (valueError != null) return valueError;

        using var actorScope = ApiActorContext.Begin($"api:{keyEntity.KeyPrefix}");
        try
        {
            var updated = await _sourceService.UpdateAsync(source);
            var reloaded = await _sourceService.GetByIdAsync(updated.Id) ?? updated;
            return Ok(MapSource(reloaded));
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(409, "source_rejected", ex.Message);
        }
        #endregion
    }
    #endregion
}
