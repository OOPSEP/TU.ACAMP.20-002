// [2026-09-23][Claude] HTTP client สำหรับเรียก V20 Admin API (api/v2/admin/*) ยืนยันตัวด้วยบัญชี Administrator ของ V20 (HTTP Basic)
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MigrationTool.V20;

public sealed class V20AdminApiClient : IDisposable
{
    #region class field
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _http;
    #endregion

    #region class constructor
    public V20AdminApiClient(string baseUrl, string adminEmail, string adminPassword, TimeSpan? timeout = null)
    {
        #region hide [2026-09-23]
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };
        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{adminEmail}:{adminPassword}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        #endregion
    }
    #endregion

    #region contracts
    public sealed class CreateSubscriptionRequest
    {
        public string? Title { get; set; }
        public int? Plan { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string? TaxId { get; set; }
        public string? OwnerName { get; set; }
        public string? Remarks { get; set; }
        public string? ExternalRef { get; set; }
        public string? ApiKeyName { get; set; }
        public int? ApiKeyExpireDays { get; set; }
    }

    public sealed class SubscriptionDto
    {
        public int SubscriptionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string? Remarks { get; set; }
    }

    public sealed class ApiKeyDto
    {
        public string KeyName { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public string RawKey { get; set; } = string.Empty;
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class SubscriptionWithKeyDto
    {
        public SubscriptionDto? Subscription { get; set; }
        public ApiKeyDto? ApiKey { get; set; }
    }

    public sealed class ApiErrorDto
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public SubscriptionDto? Subscription { get; set; }
    }

    // [2026-09-23][Claude] สัญญาของ GET api/v2/admin/subscriptions/{id}/migration-report
    public sealed class TopicReportDto
    {
        public string TopicKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int EventCount { get; set; }
        public decimal TotalQuantity { get; set; }
        public string? FirstPeriod { get; set; }
        public string? LastPeriod { get; set; }
    }

    public sealed class FacilityReportDto
    {
        public string FacilityKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TopicCount { get; set; }
        public int EventCount { get; set; }
        public List<TopicReportDto>? Topics { get; set; }
    }

    public sealed class CompanyReportDto
    {
        public string CompanyKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public float EquityShare { get; set; }
        public bool IsActive { get; set; }
        public int TopicCount { get; set; }
        public int EventCount { get; set; }
        public List<TopicReportDto>? Topics { get; set; }
        public List<FacilityReportDto> Facilities { get; set; } = new();
    }

    public sealed class CorporateReportDto
    {
        public string CorporateKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int TopicCount { get; set; }
        public int EventCount { get; set; }
        public List<TopicReportDto>? Topics { get; set; }
        public List<CompanyReportDto> Companies { get; set; } = new();
    }

    public sealed class ReportTotalsDto
    {
        public int Corporates { get; set; }
        public int Companies { get; set; }
        public int Facilities { get; set; }
        public int Topics { get; set; }
        public int Events { get; set; }
        public decimal TotalQuantity { get; set; }
        public int CustomUnits { get; set; }
        public int CustomGroups { get; set; }
        public int CustomSources { get; set; }
    }

    public sealed class GroupReportDto
    {
        public string GroupKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int Scope { get; set; }
        public string Remark { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>[2026-09-23][Claude] Emission Source ใน migration-report</summary>
    public sealed class SourceReportDto
    {
        public string SourceKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public int Scope { get; set; }
        public string UnitKey { get; set; } = string.Empty;
        public decimal CO2 { get; set; }
        public decimal FossilCH4 { get; set; }
        public decimal CH4 { get; set; }
        public decimal N2O { get; set; }
        public decimal SF6 { get; set; }
        public decimal NF3 { get; set; }
        public decimal HFCs { get; set; }
        public decimal PFCs { get; set; }
        public decimal GwpCO2 { get; set; }
        public decimal GwpFossilCH4 { get; set; }
        public decimal GwpCH4 { get; set; }
        public decimal GwpN2O { get; set; }
        public decimal GwpSF6 { get; set; }
        public decimal GwpNF3 { get; set; }
        public decimal GwpHFCs { get; set; }
        public decimal GwpPFCs { get; set; }
        public string GwpVersion { get; set; } = string.Empty;
        public decimal Ef { get; set; }
        public string Ref { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class UnitReportDto
    {
        public string UnitKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class MigrationReportDto
    {
        public int SubscriptionId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public string? Remarks { get; set; }
        public ReportTotalsDto Totals { get; set; } = new();
        public List<UnitReportDto> Units { get; set; } = new();
        public List<GroupReportDto> Groups { get; set; } = new();
        public List<SourceReportDto> Sources { get; set; } = new();
        public List<CorporateReportDto> Corporates { get; set; } = new();
    }

    /// <summary>ผลการเรียก API: Success + Data หรือ StatusCode + Error</summary>
    public sealed record ApiResult<T>(bool Success, HttpStatusCode StatusCode, T? Data, ApiErrorDto? Error);
    #endregion

    #region endpoints
    /// <summary>GET api/v2/admin/subscriptions?externalRef=... (404 = ยังไม่เคยสร้าง)</summary>
    public async Task<ApiResult<SubscriptionDto>> FindSubscriptionByExternalRefAsync(string externalRef, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var response = await _http.GetAsync($"api/v2/admin/subscriptions?externalRef={Uri.EscapeDataString(externalRef)}", ct);
        return await ReadResultAsync<SubscriptionDto>(response, ct);
        #endregion
    }

    /// <summary>POST api/v2/admin/subscriptions (201 = สร้างใหม่ + rawKey, 409 = มีอยู่แล้ว)</summary>
    public async Task<ApiResult<SubscriptionWithKeyDto>> CreateSubscriptionAsync(CreateSubscriptionRequest request, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var response = await _http.PostAsJsonAsync("api/v2/admin/subscriptions", request, JsonOptions, ct);
        return await ReadResultAsync<SubscriptionWithKeyDto>(response, ct);
        #endregion
    }

    /// <summary>POST api/v2/admin/subscriptions/{id}/api-keys (ออก Key ใหม่ให้ Subscription ที่มีอยู่แล้ว)</summary>
    public async Task<ApiResult<SubscriptionWithKeyDto>> CreateApiKeyAsync(int subscriptionId, string keyName, int? expireDays, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var response = await _http.PostAsJsonAsync($"api/v2/admin/subscriptions/{subscriptionId}/api-keys", new { keyName, expireDays }, JsonOptions, ct);
        return await ReadResultAsync<SubscriptionWithKeyDto>(response, ct);
        #endregion
    }
    #endregion

    #region api key probe
    /// <summary>
    /// [2026-09-23][Claude] ทดสอบว่า API Key ของ Subscription ยังใช้ได้ โดยเรียก GET api/v2/corporates?pageSize=1 ด้วย X-API-Key
    /// คืน HTTP status (200 = ใช้ได้, 401 = หมดอายุ/ถูกยกเลิก/ไม่ถูกต้อง)
    /// </summary>
    public static async Task<HttpStatusCode> ProbeApiKeyAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        using var response = await http.GetAsync("api/v2/corporates?pageSize=1", ct);
        return response.StatusCode;
        #endregion
    }

    /// <summary>[2026-09-23][Claude] GET api/v2/admin/subscriptions/{id}/migration-report (อ่านอย่างเดียว)</summary>
    public async Task<ApiResult<MigrationReportDto>> GetMigrationReportAsync(int subscriptionId, bool includeTopics, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var response = await _http.GetAsync($"api/v2/admin/subscriptions/{subscriptionId}/migration-report?includeTopics={(includeTopics ? "true" : "false")}", ct);
        return await ReadResultAsync<MigrationReportDto>(response, ct);
        #endregion
    }
    #endregion

    #region private helpers
    private static async Task<ApiResult<T>> ReadResultAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        #region hide [2026-09-23]
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            var data = string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize<T>(body, JsonOptions);
            return new ApiResult<T>(true, response.StatusCode, data, null);
        }

        ApiErrorDto? error;
        try
        {
            error = string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<ApiErrorDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            error = null;
        }
        error ??= new ApiErrorDto { Code = ((int)response.StatusCode).ToString(), Message = body };
        return new ApiResult<T>(false, response.StatusCode, default, error);
        #endregion
    }
    #endregion

    public void Dispose() => _http.Dispose();
}
