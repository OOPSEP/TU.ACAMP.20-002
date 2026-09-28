using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Identity;
using TU.ACAMP.WebApp.Services.Migration;

namespace TU.ACAMP.WebApp.Controllers.V2;

/// <summary>
/// [2026-09-23][Claude] Admin REST API สำหรับงานระบบ (เช่น MigrationTool V16 → V20)
/// ยืนยันตัวด้วยบัญชีผู้ใช้ V20 ที่มี Role "Administrator" ผ่าน header Authorization: Basic base64(email:password)
/// (ใช้ Identity เดียวกับหน้า Login: ตรวจรหัสผ่าน, lockout, ยืนยันอีเมล) — ไม่ต้องตั้งค่าเพิ่มที่ WebApp
/// </summary>
[ApiController]
[Route("api/v2/admin/subscriptions")]
[Produces("application/json")]
public class AdminSubscriptionApiController : ControllerBase
{
    #region class field
    private const int MaxTitleLength = 200;
    private const string ExternalRefTagFormat = "[ExternalRef:{0}]";
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly SubscriptionService _subscriptionService;
    private readonly ApiKeyService _apiKeyService;
    private readonly MigrationReportService _migrationReportService;
    private readonly ILogger<AdminSubscriptionApiController> _logger;
    #endregion

    #region class constructor
    public AdminSubscriptionApiController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        SubscriptionService subscriptionService,
        ApiKeyService apiKeyService,
        MigrationReportService migrationReportService,
        ILogger<AdminSubscriptionApiController> logger)
    {
        #region hide [2026-09-23]
        _userManager = userManager;
        _signInManager = signInManager;
        _subscriptionService = subscriptionService;
        _apiKeyService = apiKeyService;
        _migrationReportService = migrationReportService;
        _logger = logger;
        #endregion
    }
    #endregion

    #region request contracts
    /// <summary>Payload สำหรับสร้าง Subscription ใหม่ พร้อมออก API Key ของ Subscription นั้น</summary>
    public class CreateSubscriptionRequest
    {
        public string? Title { get; set; }
        /// <summary>Basic = 0, Pro = 1, Premium = 2, Enterprise = 3 (default: Pro)</summary>
        public int? Plan { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        /// <summary>เลขทะเบียน 13 หลัก ถ้าไม่ใช่ 13 หลักจะถูกเก็บไว้ใน Remarks แทน</summary>
        public string? TaxId { get; set; }
        public string? OwnerName { get; set; }
        public string? Remarks { get; set; }
        /// <summary>รหัสอ้างอิงจากระบบต้นทาง (เช่น V16:{guid}) ใช้กันสร้างซ้ำ</summary>
        public string? ExternalRef { get; set; }
        public string? ApiKeyName { get; set; }
        /// <summary>อายุ API Key เป็นวัน (null = ไม่หมดอายุ)</summary>
        public int? ApiKeyExpireDays { get; set; }
    }

    /// <summary>Payload สำหรับออก API Key ใหม่ให้ Subscription ที่มีอยู่แล้ว</summary>
    public class CreateApiKeyRequest
    {
        public string? KeyName { get; set; }
        public int? ExpireDays { get; set; }
    }
    #endregion

    #region private helpers
    private IActionResult ApiError(int statusCode, string code, string message)
        => StatusCode(statusCode, new { code, message, traceId = HttpContext.TraceIdentifier });

    /// <summary>
    /// ตรวจ Authorization: Basic (email:password) ของผู้ใช้ V20 ที่มี Role Administrator (คืน (email, null) เมื่อผ่าน)
    /// ใช้ CheckPasswordSignInAsync เพื่อให้กติกา lockout / ยืนยันบัญชี เหมือนหน้า Login
    /// </summary>
    private async Task<(string? AdminEmail, IActionResult? Error)> AuthorizeAdminAsync()
    {
        #region hide [2026-09-23]
        Response.Headers.Append("WWW-Authenticate", "Basic realm=\"ACAMP Admin API\"");
        var unauthorized = ApiError(401, "invalid_credentials", "Invalid administrator credentials.");

        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return (null, unauthorized);
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return (null, unauthorized);
        }

        int sep = decoded.IndexOf(':');
        if (sep <= 0) return (null, unauthorized);
        var email = decoded[..sep].Trim();
        var password = decoded[(sep + 1)..];

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null) return (null, unauthorized);

        var signIn = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (signIn.IsLockedOut)
        {
            _logger.LogWarning("Admin API: account locked out for {Email}", email);
            return (null, ApiError(423, "account_locked", "Account is locked out. Try again later."));
        }
        if (!signIn.Succeeded)
        {
            _logger.LogWarning("Admin API: failed sign-in for {Email} (NotAllowed={NotAllowed})", email, signIn.IsNotAllowed);
            return (null, unauthorized);
        }

        if (!await _userManager.IsInRoleAsync(user, SystemRoles.Administrator))
        {
            _logger.LogWarning("Admin API: {Email} is not in role {Role}", email, SystemRoles.Administrator);
            return (null, ApiError(403, "forbidden", "Administrator role is required."));
        }

        Response.Headers.Remove("WWW-Authenticate");
        return (email, null);
        #endregion
    }

    private static object MapSubscription(TbSubscription sub) => new
    {
        subscriptionId = sub.Id,
        title = sub.Title,
        plan = sub.SubscriptionPlan.ToString(),
        planNo = (int)sub.SubscriptionPlan,
        startDate = sub.StartDate,
        expiryDate = sub.ExpiryDate,
        taxId = sub.TaxId,
        ownerName = sub.OwnerName,
        remarks = sub.Remarks,
        isActive = sub.IsActive,
        createdAt = sub.CreatedAt
    };

    private async Task<object> IssueApiKeyAsync(int subscriptionId, string? keyName, int? expireDays)
    {
        #region hide [2026-09-23]
        var name = string.IsNullOrWhiteSpace(keyName) ? "admin-issued" : keyName.Trim();
        DateTime? expiresAt = expireDays.HasValue && expireDays.Value > 0 ? DateTime.UtcNow.AddDays(expireDays.Value) : null;
        var (entity, rawKey) = await _apiKeyService.CreateApiKeyAsync(subscriptionId, name, expiresAt: expiresAt);
        return new
        {
            keyName = entity.KeyName,
            keyPrefix = entity.KeyPrefix,
            rawKey, // แสดงครั้งเดียว ระบบเก็บแค่ hash
            expiresAt = entity.ExpiresAt
        };
        #endregion
    }
    #endregion

    #region endpoints
    /// <summary>ค้นหา Subscription จาก ExternalRef (ใช้ตรวจว่าเคยสร้างแล้วหรือยัง)</summary>
    [HttpGet]
    public async Task<IActionResult> FindByExternalRef([FromQuery] string? externalRef)
    {
        #region hide [2026-09-23]
        var (_, authError) = await AuthorizeAdminAsync();
        if (authError != null) return authError;

        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return ApiError(400, "missing_external_ref", "Query 'externalRef' is required.");
        }

        var tag = string.Format(ExternalRefTagFormat, externalRef.Trim());
        var existing = await _subscriptionService.FindByRemarksTagAsync(tag);
        if (existing == null)
        {
            return ApiError(404, "subscription_not_found", $"No subscription found for externalRef '{externalRef.Trim()}'.");
        }

        return Ok(MapSubscription(existing));
        #endregion
    }

    /// <summary>สร้าง Subscription ใหม่ + ออก API Key ของ Subscription นั้น (rawKey แสดงครั้งเดียว)</summary>
    [HttpPost]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest request)
    {
        #region hide [2026-09-23]
        var (adminEmail, authError) = await AuthorizeAdminAsync();
        if (authError != null) return authError;
        using var actorScope = TU.ACAMP.WebApp.Services.ApiActorContext.Begin(adminEmail!); // CreatedBy = อีเมล Admin

        if (request == null) return ApiError(400, "invalid_payload", "Request body is required.");

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) return ApiError(400, "missing_title", "Field 'title' is required.");
        if (title.Length > MaxTitleLength) return ApiError(400, "invalid_title", $"Field 'title' exceeds {MaxTitleLength} characters.");

        var planNo = request.Plan ?? (int)eSubscriptionPlan.Pro;
        if (!Enum.IsDefined(typeof(eSubscriptionPlan), planNo))
        {
            return ApiError(400, "invalid_plan", "Field 'plan' must be 0 (Basic), 1 (Pro), 2 (Premium) or 3 (Enterprise).");
        }

        // [2026-09-23][Claude] Npgsql ต้องการ DateTimeKind.Utc (กฎ DB-agnostic) — ค่าจาก V16 ไม่มี Kind จึงระบุเป็น Utc (คงวัน/เวลาเดิม ไม่เลื่อนโซน)
        var startDate = request.StartDate.HasValue ? DateTime.SpecifyKind(request.StartDate.Value, DateTimeKind.Utc) : DateTime.UtcNow;
        var expiryDate = request.ExpiryDate.HasValue ? DateTime.SpecifyKind(request.ExpiryDate.Value, DateTimeKind.Utc) : startDate.AddYears(1);
        if (expiryDate <= startDate)
        {
            return ApiError(400, "invalid_expiry_date", "Field 'expiryDate' must be later than 'startDate'.");
        }

        // กันสร้างซ้ำด้วย ExternalRef (เก็บเป็น tag ใน Remarks เพราะ TbSubscription ยังไม่มีคอลัมน์อ้างอิงภายนอก)
        string? tag = null;
        if (!string.IsNullOrWhiteSpace(request.ExternalRef))
        {
            tag = string.Format(ExternalRefTagFormat, request.ExternalRef.Trim());
            var existing = await _subscriptionService.FindByRemarksTagAsync(tag);
            if (existing != null)
            {
                return StatusCode(409, new
                {
                    code = "subscription_already_exists",
                    message = $"Subscription for externalRef '{request.ExternalRef.Trim()}' already exists. Use POST {existing.Id}/api-keys to issue a new key.",
                    subscription = MapSubscription(existing),
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        var remarks = request.Remarks?.Trim() ?? string.Empty;
        var taxIdRaw = request.TaxId?.Trim() ?? string.Empty;
        var taxId = Regex.IsMatch(taxIdRaw, @"^\d{13}$") ? taxIdRaw : string.Empty;
        if (taxIdRaw.Length > 0 && taxId.Length == 0)
        {
            remarks = $"{remarks} [RegistrationNo:{taxIdRaw}]".Trim();
        }
        if (tag != null)
        {
            remarks = $"{remarks} {tag}".Trim();
        }

        var subscription = new TbSubscription
        {
            Title = title,
            SubscriptionPlan = (eSubscriptionPlan)planNo,
            StartDate = startDate,
            ExpiryDate = expiryDate,
            TaxId = taxId,
            OwnerName = string.IsNullOrWhiteSpace(request.OwnerName) ? title : request.OwnerName.Trim(),
            Remarks = remarks,
            IsActive = true
        };

        try
        {
            var created = await _subscriptionService.CreateAsync(subscription, sendEmail: false);
            var apiKey = await IssueApiKeyAsync(created.Id, request.ApiKeyName, request.ApiKeyExpireDays);

            _logger.LogInformation("Admin API: {AdminEmail} created Subscription {SubscriptionId} ({Title}) externalRef={ExternalRef}", adminEmail, created.Id, created.Title, request.ExternalRef);

            return StatusCode(201, new { subscription = MapSubscription(created), apiKey });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin API failed to create subscription {Title}", title);
            return ApiError(500, "internal_error", "Failed to create subscription.");
        }
        #endregion
    }

    /// <summary>ออก API Key ใหม่ให้ Subscription ที่มีอยู่แล้ว (rawKey แสดงครั้งเดียว)</summary>
    [HttpPost("{subscriptionId:int}/api-keys")]
    public async Task<IActionResult> CreateApiKey(int subscriptionId, [FromBody] CreateApiKeyRequest? request)
    {
        #region hide [2026-09-23]
        var (adminEmail, authError) = await AuthorizeAdminAsync();
        if (authError != null) return authError;
        using var actorScope = TU.ACAMP.WebApp.Services.ApiActorContext.Begin(adminEmail!); // CreatedBy = อีเมล Admin

        var subscription = await _subscriptionService.GetByIdAsync(subscriptionId);
        if (subscription == null)
        {
            return ApiError(404, "subscription_not_found", $"Subscription {subscriptionId} was not found.");
        }

        var apiKey = await IssueApiKeyAsync(subscription.Id, request?.KeyName, request?.ExpireDays);
        _logger.LogInformation("Admin API: {AdminEmail} issued API key for Subscription {SubscriptionId}", adminEmail, subscription.Id);
        return StatusCode(201, new { subscription = MapSubscription(subscription), apiKey });
        #endregion
    }

    /// <summary>
    /// [2026-09-23][Claude] รายงานผลการ migrate ของ Subscription (อ่านอย่างเดียว) สำหรับตรวจเทียบกับ V16
    /// GET api/v2/admin/subscriptions/{id}/migration-report?includeTopics=false
    /// คืนโครงสร้าง Corporate -> Company -> Facility (Code / Key / Title / จำนวน Topic, Event) และยอดรวมทั้ง Subscription
    /// </summary>
    [HttpGet("{subscriptionId:int}/migration-report")]
    public async Task<IActionResult> GetMigrationReport(int subscriptionId, [FromQuery] bool includeTopics = false)
    {
        #region hide [2026-09-23]
        var (_, authError) = await AuthorizeAdminAsync();
        if (authError != null) return authError;

        var report = await _migrationReportService.GetReportAsync(subscriptionId, includeTopics, HttpContext.RequestAborted);
        if (report == null)
        {
            return ApiError(404, "subscription_not_found", $"Subscription {subscriptionId} was not found.");
        }

        return Ok(report);
        #endregion
    }
    #endregion
}
