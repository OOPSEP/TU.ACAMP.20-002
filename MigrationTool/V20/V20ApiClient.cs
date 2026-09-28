// [2026-09-23][Claude] HTTP client สำหรับเรียก V20 Public API (api/v2/*) ด้วย API Key ของ Subscription (X-API-Key)
// retry อัตโนมัติเมื่อเจอ 429 (ตาม Retry-After) และ 5xx / network error (backoff 2, 4, 8 วินาที)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MigrationTool.V20;

public sealed class V20ApiClient : IDisposable
{
    #region class field
    private const int MaxAttempts = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private readonly HttpClient _http;
    #endregion

    #region class constructor
    public V20ApiClient(string baseUrl, string apiKey, TimeSpan? timeout = null)
    {
        #region hide [2026-09-23]
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(60)
        };
        _http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        #endregion
    }
    #endregion

    #region contracts
    public sealed class ApiErrorDto
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        /// <summary>[2026-09-23][Claude] body ดิบของ response ที่ไม่สำเร็จ (ใช้อ่านผลรายรายการของ ingest ที่ถูกปฏิเสธทั้ง batch)</summary>
        [JsonIgnore]
        public string? Body { get; set; }
    }

    public sealed record ApiResult<T>(bool Success, HttpStatusCode StatusCode, T? Data, ApiErrorDto? Error);

    public sealed class PagedResult<T>
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<T> Items { get; set; } = new();
    }

    public sealed class CorporateDto
    {
        public string CorporateKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Remark { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class CreateCorporateRequest
    {
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? AllowShare { get; set; }
        public decimal? MaterialityThreshold { get; set; }
        public bool? IsActive { get; set; }
    }

    public sealed class CompanyDto
    {
        public string CompanyKey { get; set; } = string.Empty;
        public string CorporateKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public float EquityShare { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class CreateCompanyRequest
    {
        public string? CorporateKey { get; set; }
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public float? EquityShare { get; set; }
        public bool? AllowShare { get; set; }
        public bool? IsActive { get; set; }
    }

    public sealed class FacilityDto
    {
        public string FacilityKey { get; set; } = string.Empty;
        public string CompanyKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class CreateFacilityRequest
    {
        public string? CompanyKey { get; set; }
        public string? Code { get; set; }
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public bool? AllowShare { get; set; }
        public bool? IsActive { get; set; }
    }

    // [2026-09-23][Claude] Emission Units (api/v2/emission-units)
    public sealed class UnitDto
    {
        public string UnitKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class UnitListDto
    {
        public int TotalCount { get; set; }
        public List<UnitDto> Items { get; set; } = new();
    }

    public sealed class CreateUnitRequest { public string? Title { get; set; } public string? Remark { get; set; } }
    public sealed class UpdateUnitRequest { public string? Title { get; set; } public string? Remark { get; set; } }

    // [2026-09-23][Claude] Emission Groups (api/v2/emission-groups)
    public sealed class GroupDto
    {
        public string GroupKey { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? TitleTh { get; set; }
        public int Scope { get; set; }
        public string Remark { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public sealed class GroupListDto
    {
        public int TotalCount { get; set; }
        public List<GroupDto> Items { get; set; } = new();
    }

    // [2026-09-23][Claude] Emission Sources (api/v2/emission-sources)
    public sealed class SourceDto
    {
        public string SourceKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public string GroupTitle { get; set; } = string.Empty;
        public int Scope { get; set; }
        public string UnitKey { get; set; } = string.Empty;
        public string UnitTitle { get; set; } = string.Empty;
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

    public sealed class SourceListDto
    {
        public int TotalCount { get; set; }
        public List<SourceDto> Items { get; set; } = new();
    }

    /// <summary>[2026-09-23][Claude] ใช้ทั้ง POST และ PATCH (PATCH ส่งเฉพาะฟิลด์ที่ไม่เป็น null)</summary>
    public sealed class SourceRequest
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
        public string? GwpVersion { get; set; }
    }

    // [2026-09-23][Claude] Topics (api/v2/topics)
    public sealed class TopicChildDto
    {
        public string EmissionSourceKey { get; set; } = string.Empty;
        public string? EmissionSource { get; set; }
        public string? ActivityGroup { get; set; }
        public float EmissionSharePercent { get; set; }
    }

    public sealed class TopicDto
    {
        public string TopicKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Remark { get; set; }
        public string TargetKey { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public List<TopicChildDto> Childs { get; set; } = new();
    }

    public sealed class CreateTopicRequest
    {
        public string? Title { get; set; }
        public string? Remark { get; set; }
        public string? TargetKey { get; set; }
        public string? EmissionSourceKey { get; set; }
        public decimal? UnitConversionRatio { get; set; }
        public float? EmissionSharePercent { get; set; }
        public bool? IsActive { get; set; }
    }

    public sealed class UpdateTopicRequest { public string? Title { get; set; } public string? Remark { get; set; } }

    // [2026-09-23][Claude] Events (api/v2/events/ingest) — ส่งครั้งละไม่เกิน 500 รายการ
    public sealed class IngestEventItem
    {
        public string TopicKey { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string? ExternalRefId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Remark { get; set; } = string.Empty;
    }

    public sealed class IngestItemResultDto
    {
        public int Index { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? EventKey { get; set; }
        public string? ExternalRefId { get; set; }
        public string? Code { get; set; }
        public string? Message { get; set; }
        public bool Replayed { get; set; }
    }

    public sealed class IngestResultDto
    {
        public string? BatchId { get; set; }
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public List<IngestItemResultDto> Results { get; set; } = new();
    }

    public sealed class CreateGroupRequest { public string? Title { get; set; } public int? Scope { get; set; } public string? Remark { get; set; } }
    public sealed class UpdateGroupRequest { public string? Title { get; set; } public string? Remark { get; set; } }

    // [2026-09-23][Claude] PATCH payloads — ส่งเฉพาะฟิลด์ที่ไม่เป็น null (V20 ข้ามฟิลด์ null)
    public sealed class UpdateCorporateRequest { public string? Title { get; set; } }
    public sealed class UpdateCompanyRequest { public string? Title { get; set; } public float? EquityShare { get; set; } }
    public sealed class UpdateFacilityRequest { public string? Title { get; set; } }
    #endregion

    #region corporates
    /// <summary>ดึง Corporate ทั้งหมดของ Subscription (รวม inactive) ไล่ทุกหน้า</summary>
    public async Task<ApiResult<List<CorporateDto>>> GetAllCorporatesAsync(CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var all = new List<CorporateDto>();
        int page = 1;
        while (true)
        {
            var res = await SendAsync<PagedResult<CorporateDto>>(() => new HttpRequestMessage(HttpMethod.Get, $"api/v2/corporates?includeInactive=true&page={page}&pageSize=200"), ct);
            if (!res.Success || res.Data == null)
            {
                return new ApiResult<List<CorporateDto>>(false, res.StatusCode, null, res.Error);
            }

            all.AddRange(res.Data.Items);
            if (page >= res.Data.TotalPages || res.Data.Items.Count == 0) break;
            page++;
        }
        return new ApiResult<List<CorporateDto>>(true, HttpStatusCode.OK, all, null);
        #endregion
    }

    /// <summary>POST api/v2/corporates (201 = สร้างแล้ว, 409 = Code ซ้ำ/โควตาเต็ม)</summary>
    public Task<ApiResult<CorporateDto>> CreateCorporateAsync(CreateCorporateRequest request, CancellationToken ct = default)
        => SendAsync<CorporateDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/corporates") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/corporates/{key}</summary>
    public Task<ApiResult<CorporateDto>> UpdateCorporateAsync(string corporateKey, UpdateCorporateRequest request, CancellationToken ct = default)
        => SendAsync<CorporateDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/corporates/{Uri.EscapeDataString(corporateKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region companies
    /// <summary>[2026-09-23][Claude] ดึง Company ทั้งหมดใต้ Corporate (รวม inactive) ไล่ทุกหน้า</summary>
    public Task<ApiResult<List<CompanyDto>>> GetAllCompaniesAsync(string corporateKey, CancellationToken ct = default)
        => GetAllPagesAsync<CompanyDto>($"api/v2/companies?corporateKey={Uri.EscapeDataString(corporateKey)}&includeInactive=true", ct);

    /// <summary>[2026-09-23][Claude] POST api/v2/companies (201 = สร้างแล้ว, 409 = Code ซ้ำใน Corporate/โควตาเต็ม)</summary>
    public Task<ApiResult<CompanyDto>> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken ct = default)
        => SendAsync<CompanyDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/companies") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/companies/{key}</summary>
    public Task<ApiResult<CompanyDto>> UpdateCompanyAsync(string companyKey, UpdateCompanyRequest request, CancellationToken ct = default)
        => SendAsync<CompanyDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/companies/{Uri.EscapeDataString(companyKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region facilities
    /// <summary>[2026-09-23][Claude] ดึง Facility ทั้งหมดใต้ Company (รวม inactive) ไล่ทุกหน้า</summary>
    public Task<ApiResult<List<FacilityDto>>> GetAllFacilitiesAsync(string companyKey, CancellationToken ct = default)
        => GetAllPagesAsync<FacilityDto>($"api/v2/facilities?companyKey={Uri.EscapeDataString(companyKey)}&includeInactive=true", ct);

    /// <summary>[2026-09-23][Claude] POST api/v2/facilities (201 = สร้างแล้ว, 409 = Title ซ้ำใน Company/โควตาเต็ม)</summary>
    public Task<ApiResult<FacilityDto>> CreateFacilityAsync(CreateFacilityRequest request, CancellationToken ct = default)
        => SendAsync<FacilityDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/facilities") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/facilities/{key} (409 = Title ซ้ำใน Company)</summary>
    public Task<ApiResult<FacilityDto>> UpdateFacilityAsync(string facilityKey, UpdateFacilityRequest request, CancellationToken ct = default)
        => SendAsync<FacilityDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/facilities/{Uri.EscapeDataString(facilityKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region emission units
    /// <summary>[2026-09-23][Claude] GET api/v2/emission-units?includeDefault=true (Default + Custom ของ Subscription)</summary>
    public async Task<ApiResult<List<UnitDto>>> GetUnitsAsync(CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var res = await SendAsync<UnitListDto>(() => new HttpRequestMessage(HttpMethod.Get, "api/v2/emission-units?includeDefault=true"), ct);
        return new ApiResult<List<UnitDto>>(res.Success, res.StatusCode, res.Data?.Items, res.Error);
        #endregion
    }

    /// <summary>[2026-09-23][Claude] POST api/v2/emission-units (201 = สร้าง Custom, 409 = ชื่อซ้ำ Default/Custom)</summary>
    public Task<ApiResult<UnitDto>> CreateUnitAsync(CreateUnitRequest request, CancellationToken ct = default)
        => SendAsync<UnitDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/emission-units") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/emission-units/{key} (แก้ได้เฉพาะ Custom)</summary>
    public Task<ApiResult<UnitDto>> UpdateUnitAsync(string unitKey, UpdateUnitRequest request, CancellationToken ct = default)
        => SendAsync<UnitDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/emission-units/{Uri.EscapeDataString(unitKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region emission groups
    /// <summary>[2026-09-23][Claude] GET api/v2/emission-groups?includeDefault=true</summary>
    public async Task<ApiResult<List<GroupDto>>> GetGroupsAsync(CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var res = await SendAsync<GroupListDto>(() => new HttpRequestMessage(HttpMethod.Get, "api/v2/emission-groups?includeDefault=true"), ct);
        return new ApiResult<List<GroupDto>>(res.Success, res.StatusCode, res.Data?.Items, res.Error);
        #endregion
    }

    /// <summary>[2026-09-23][Claude] POST api/v2/emission-groups (201 = สร้าง Custom, 409 = Title+Scope ซ้ำ)</summary>
    public Task<ApiResult<GroupDto>> CreateGroupAsync(CreateGroupRequest request, CancellationToken ct = default)
        => SendAsync<GroupDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/emission-groups") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/emission-groups/{key} (แก้ได้เฉพาะ Custom)</summary>
    public Task<ApiResult<GroupDto>> UpdateGroupAsync(string groupKey, UpdateGroupRequest request, CancellationToken ct = default)
        => SendAsync<GroupDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/emission-groups/{Uri.EscapeDataString(groupKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region emission sources
    /// <summary>[2026-09-23][Claude] GET api/v2/emission-sources?includeDefault=true</summary>
    public async Task<ApiResult<List<SourceDto>>> GetSourcesAsync(CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var res = await SendAsync<SourceListDto>(() => new HttpRequestMessage(HttpMethod.Get, "api/v2/emission-sources?includeDefault=true"), ct);
        return new ApiResult<List<SourceDto>>(res.Success, res.StatusCode, res.Data?.Items, res.Error);
        #endregion
    }

    /// <summary>[2026-09-23][Claude] POST api/v2/emission-sources (201 = สร้าง Custom, 409 = Title ซ้ำ)</summary>
    public Task<ApiResult<SourceDto>> CreateSourceAsync(SourceRequest request, CancellationToken ct = default)
        => SendAsync<SourceDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/emission-sources") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/emission-sources/{key} (แก้ได้เฉพาะ Custom)</summary>
    public Task<ApiResult<SourceDto>> UpdateSourceAsync(string sourceKey, SourceRequest request, CancellationToken ct = default)
        => SendAsync<SourceDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/emission-sources/{Uri.EscapeDataString(sourceKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region topics
    /// <summary>[2026-09-23][Claude] GET api/v2/topics (รวม inactive) ไล่ทุกหน้า</summary>
    public Task<ApiResult<List<TopicDto>>> GetAllTopicsAsync(CancellationToken ct = default)
        => GetAllPagesAsync<TopicDto>("api/v2/topics?includeInactive=true", ct);

    /// <summary>[2026-09-23][Claude] POST api/v2/topics (201 = สร้าง, 409 topic_already_exists = ชื่อซ้ำใน Target เดียวกัน)</summary>
    public Task<ApiResult<TopicDto>> CreateTopicAsync(CreateTopicRequest request, CancellationToken ct = default)
        => SendAsync<TopicDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/topics") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);

    /// <summary>[2026-09-23][Claude] PATCH api/v2/topics/{key} (title / remark)</summary>
    public Task<ApiResult<TopicDto>> UpdateTopicAsync(string topicKey, UpdateTopicRequest request, CancellationToken ct = default)
        => SendAsync<TopicDto>(() => new HttpRequestMessage(HttpMethod.Patch, $"api/v2/topics/{Uri.EscapeDataString(topicKey)}") { Content = JsonContent.Create(request, options: JsonOptions) }, ct);
    #endregion

    #region events
    /// <summary>
    /// [2026-09-23][Claude] POST api/v2/events/ingest — ผลรายรายการอยู่ใน results ทั้งกรณี 201/200 และ 400 batch_failed (ทุกรายการถูกปฏิเสธ)
    /// จึงอ่าน body เป็น IngestResultDto เสมอเมื่อมี results
    /// </summary>
    public async Task<ApiResult<IngestResultDto>> IngestEventsAsync(List<IngestEventItem> events, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        var res = await SendAsync<IngestResultDto>(() => new HttpRequestMessage(HttpMethod.Post, "api/v2/events/ingest") { Content = JsonContent.Create(new { events }, options: JsonOptions) }, ct);
        if (res.Success) return res;

        // 400 batch_failed: body = { code, message, batchId, results } -> คืนผลรายรายการให้ผู้เรียก
        if (res.StatusCode == HttpStatusCode.BadRequest && res.Error?.Code == "batch_failed" && !string.IsNullOrWhiteSpace(res.Error.Body))
        {
            try
            {
                var failed = JsonSerializer.Deserialize<IngestResultDto>(res.Error.Body, JsonOptions);
                if (failed != null && failed.Results.Count > 0) return new ApiResult<IngestResultDto>(true, res.StatusCode, failed, res.Error);
            }
            catch (JsonException)
            {
                // ใช้ผล error เดิม
            }
        }
        return res;
        #endregion
    }
    #endregion

    #region private helpers
    /// <summary>[2026-09-23][Claude] GET แบบแบ่งหน้า (page/pageSize=200) รวมทุกหน้า — url ต้องมี query อยู่แล้ว</summary>
    private async Task<ApiResult<List<T>>> GetAllPagesAsync<T>(string url, CancellationToken ct)
    {
        #region hide [2026-09-23]
        var all = new List<T>();
        int page = 1;
        while (true)
        {
            int currentPage = page;
            var res = await SendAsync<PagedResult<T>>(() => new HttpRequestMessage(HttpMethod.Get, $"{url}&page={currentPage}&pageSize=200"), ct);
            if (!res.Success || res.Data == null)
            {
                return new ApiResult<List<T>>(false, res.StatusCode, null, res.Error);
            }

            all.AddRange(res.Data.Items);
            if (page >= res.Data.TotalPages || res.Data.Items.Count == 0) break;
            page++;
        }
        return new ApiResult<List<T>>(true, HttpStatusCode.OK, all, null);
        #endregion
    }

    /// <summary>ส่งคำขอพร้อม retry (429 / 5xx / network) — requestFactory ต้องสร้าง HttpRequestMessage ใหม่ทุกครั้ง</summary>
    private async Task<ApiResult<T>> SendAsync<T>(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
    {
        #region hide [2026-09-23]
        for (int attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = requestFactory();
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(Backoff(attempt), ct);
                continue;
            }

            using (response)
            {
                bool retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (retryable && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? Backoff(attempt);
                    await Task.Delay(delay, ct);
                    continue;
                }

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
                error.Body = body;
                return new ApiResult<T>(false, response.StatusCode, default, error);
            }
        }
        #endregion
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt));
    #endregion

    public void Dispose() => _http.Dispose();
}
