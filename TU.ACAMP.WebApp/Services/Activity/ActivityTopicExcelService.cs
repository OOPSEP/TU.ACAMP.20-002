using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

public enum eImportMappingState
{
    Resolved = 0,       // จับคู่ emission source ได้แล้ว (auto หรือผู้ใช้เลือกเอง)
    NeedsMapping = 1,   // fallback ไป Unknown (Id = -1) — ต้องให้ผู้ใช้เลือกเอง
    Invalid = 2         // ผิดเชิงโครงสร้าง เช่น ไม่มี Topic Title
}

public enum eExistingTopicAction
{
    Skip = 0,
    Overwrite = 1,
    ImportAsNew = 2
}

public enum eTopicRowAction
{
    Default = 0,
    Skip = 1,
    Remove = 2
}

public class ActivityTopicImportItemDto
{
    public int RowNumber { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public string MasterTopicTitle { get; set; } = string.Empty;
    public string ParentTopicTitle { get; set; } = string.Empty;
    public bool IsChildTopic { get; set; } = false;
    public int ChildIndex { get; set; } = 0; // 0 = Master / 1st child, 1 = 2nd child, etc.

    public eImportMappingState MappingState { get; set; } = eImportMappingState.Resolved;
    public bool IsManuallyMapped { get; set; } = false;
    public decimal ExcelEF { get; set; }
    public string ExcelUnit { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;
    public string EmissionGroupTitle { get; set; } = string.Empty;
    public string ActivityGroupTitle { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal EF { get; set; }
    public string Remark { get; set; } = string.Empty;
    public float EmissionSharePercent { get; set; } = 100f;

    public bool IsValid { get; set; } = true;
    public List<string> ValidationErrors { get; set; } = new();

    // Resolved / Matched properties
    public int? ActivityGroupId { get; set; }
    public string ActivityGroupCode { get; set; } = string.Empty;
    public eTgoScopeNo Scope { get; set; } = eTgoScopeNo.Scope1;

    public int? EmissionGroupId { get; set; }
    public int? EmissionSourceId { get; set; }
    public string MatchedSourceName { get; set; } = string.Empty;
    public decimal MatchedEF { get; set; }
    public int? MatchedUnitId { get; set; }
    public string MatchedUnit { get; set; } = string.Empty;
    public decimal UnitConversionRatio { get; set; } = 1m;
    public bool IsCustomSourceNeeded { get; set; } = false;

    // Database Matching
    public bool IsExistingMasterMatched { get; set; } = false;
    public int? MatchedMasterTopicId { get; set; }
    public eExistingTopicAction ExistingAction { get; set; } = eExistingTopicAction.Skip;
    public eTopicRowAction RowAction { get; set; } = eTopicRowAction.Default;
}

public class ActivityTopicExcelService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    /// <summary>ความยาวขั้นต่ำของคำค้นที่อนุญาตให้ทำ substring matching ได้</summary>
    private const int MinSubstringSearchLength = 4;

    /// <summary>จำนวน source ที่ substring match ได้สูงสุด ถ้าเกินนี้ถือว่ากำกวม</summary>
    private const int MaxAmbiguousMatches = 1;

    /// <summary>ช่วง UnitConversionRatio ที่ถือว่าเป็นการแปลงหน่วยตามปกติ นอกช่วงนี้ถือว่าจับคู่ source ผิด</summary>
    private const decimal MinPlausibleConversionRatio = 0.1m;
    private const decimal MaxPlausibleConversionRatio = 10m;

    // Bilingual translation vocabulary for semantic mapping resolution against database
    private static readonly Dictionary<string, string> SynonymTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        { "ดีเซล", "diesel" },
        { "น้ำมันดีเซล", "diesel" },
        { "เบนซิน", "gasoline" },
        { "น้ำมันเบนซิน", "gasoline" },
        { "แก๊สโซฮอล์", "gasohol" },
        { "น้ำมันเตา", "fuel oil" },
        { "ก๊าซธรรมชาติ", "natural gas" },
        { "ก๊าซหุงต้ม", "lpg" },
        { "ก๊าซปิโตรเลียมเหลว", "lpg" },
        { "ถ่านหิน", "coal" },
        { "ลิกไนต์", "lignite" },
        { "แอนทราไซต์", "anthracite" },
        { "บิทูมินัส", "bituminous" },
        { "แกลบ", "paddy husk" },
        { "ชานอ้อย", "baggasse" },
        { "ขี้เลื่อย", "saw dust" },
        { "ฟืน", "fuel wood" },
        { "เศษไม้", "wood" },
        { "ไม้ยางพารา", "wood" },
        { "กะลาปาล์ม", "palm kernel shell" },
        { "ซังข้าวโพด", "cob" },
        { "ก๊าซชีวภาพ", "biogas" },
        { "น้ำประปา", "น้ำประปา" },
        { "ประปา", "น้ำประปา" },
        { "ไฟฟ้า", "ไฟฟ้าแบบ grid mix" },
        { "กระดาษ", "กระดาษ" }
    };

    public ActivityTopicExcelService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    private static string FormatScope(eTgoScopeNo scope) => scope switch
    {
        eTgoScopeNo.Scope1 => "Scope 1",
        eTgoScopeNo.Scope2 => "Scope 2",
        eTgoScopeNo.Scope3 => "Scope 3",
        eTgoScopeNo.SeparateReport => "Separate Report",
        _ => "Scope 1"
    };

    private static eTgoScopeNo ParseScope(string? scopeStr)
    {
        if (string.IsNullOrWhiteSpace(scopeStr)) return eTgoScopeNo.Scope1;
        var s = scopeStr.Trim().ToLowerInvariant();
        if (s.Contains("2") || s.Contains("scope 2") || s.Contains("scope2")) return eTgoScopeNo.Scope2;
        if (s.Contains("3") || s.Contains("scope 3") || s.Contains("scope3")) return eTgoScopeNo.Scope3;
        if (s.Contains("4") || s.Contains("separate") || s.Contains("biogenic") || s.Contains("ชีวภาพ")) return eTgoScopeNo.SeparateReport;
        return eTgoScopeNo.Scope1;
    }

    private static float ParseSharePercent(string? shareStr)
    {
        if (string.IsNullOrWhiteSpace(shareStr)) return 100f;
        var cleaned = shareStr.Replace("%", "").Trim();
        if (float.TryParse(cleaned, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            if (val > 0 && val <= 1f && shareStr.Contains('.'))
            {
                return val * 100f;
            }
            return Math.Clamp(val, 0f, 100f);
        }
        return 100f;
    }

    /// <summary>
    /// ค้นหาและจับคู่ Emission Source โดยอ้างอิงจากฐานข้อมูล TbEmissionMapping และ TbEmissionSource โดยตรง (Database-Driven)
    /// </summary>
    private static (TbEmissionSource? Source, TbEmissionMapping? Mapping) ResolveSourceFromDatabase(
        string sourceName,
        string emGroupStr,
        string actGroupStr,
        string scopeStr,
        List<TbEmissionSource> availableSources,
        List<TbEmissionMapping> allMappings)
    {
        if (string.IsNullOrWhiteSpace(sourceName)) return (null, null);

        var cleaned = sourceName.Replace("(Stationary)", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("(Mobile)", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("(Scope 1)", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("(Scope 2)", "", StringComparison.OrdinalIgnoreCase)
                                .Replace("(Scope 3)", "", StringComparison.OrdinalIgnoreCase)
                                .Trim();

        var lower = cleaned.ToLowerInvariant();
        var parsedScope = ParseScope(scopeStr);

        // Normalize vocabulary using semantic dictionary
        var searchTerm = cleaned;
        foreach (var (th, en) in SynonymTranslations)
        {
            if (lower.Contains(th.ToLowerInvariant()))
            {
                searchTerm = lower.Replace(th.ToLowerInvariant(), en);
                break;
            }
        }
        var searchLower = searchTerm.ToLowerInvariant();

        // 1. Search TbEmissionMapping directly from database
        TbEmissionMapping? matchedMapping = null;

        // Exact match on Mapping Title or Child Titles
        matchedMapping = allMappings.FirstOrDefault(m =>
            m.Title.Equals(cleaned, StringComparison.OrdinalIgnoreCase) ||
            m.Title.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            m.Scope1ChildTitle.Equals(cleaned, StringComparison.OrdinalIgnoreCase) ||
            m.Scope1ChildTitle.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) ||
            m.Scope2ChildTitle.Equals(cleaned, StringComparison.OrdinalIgnoreCase) ||
            m.Scope2ChildTitle.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

        // Substring match on Mapping Title
        if (matchedMapping == null
            && cleaned.Length >= MinSubstringSearchLength
            && searchLower.Length >= MinSubstringSearchLength)
        {
            matchedMapping = allMappings.FirstOrDefault(m =>
                m.Title.Contains(cleaned, StringComparison.OrdinalIgnoreCase) ||
                m.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase));
        }

        if (matchedMapping != null)
        {
            int primarySrcId = 0;
            if (parsedScope == eTgoScopeNo.Scope2 && matchedMapping.Scope2ChildId > 0)
            {
                primarySrcId = matchedMapping.Scope2ChildId;
            }
            else if (matchedMapping.Scope1ChildId > 0)
            {
                primarySrcId = matchedMapping.Scope1ChildId;
            }
            else if (matchedMapping.Scope2ChildId > 0)
            {
                primarySrcId = matchedMapping.Scope2ChildId;
            }

            if (primarySrcId > 0)
            {
                var src = availableSources.FirstOrDefault(s => s.Id == primarySrcId);
                if (src != null)
                {
                    return (src, matchedMapping);
                }
            }
        }

        // 2. Direct Search in availableSources (when no direct TbEmissionMapping match was found)

        // 2.1 Exact match — ปลอดภัยเสมอ ไม่ต้องมีเงื่อนไขความยาว
        var sourceCandidate = availableSources.FirstOrDefault(s => s.Title.Equals(cleaned, StringComparison.OrdinalIgnoreCase))
                           ?? availableSources.FirstOrDefault(s => s.Title.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

        // 2.2 Substring match — อนุญาตเฉพาะคำค้นที่ยาวพอ และต้องได้ผลลัพธ์เดียวเท่านั้น
        if (sourceCandidate == null
            && cleaned.Length >= MinSubstringSearchLength
            && searchLower.Length >= MinSubstringSearchLength)
        {
            var substringMatches = availableSources
                .Where(s => s.Title.Contains(cleaned, StringComparison.OrdinalIgnoreCase)
                         || s.Title.Contains(searchLower, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // กำกวม (เจอหลายตัว) ให้ปล่อยเป็น null เพื่อไปเข้าเส้นทาง NeedsMapping แทนการเดา
            if (substringMatches.Count > 0 && substringMatches.Count <= MaxAmbiguousMatches)
            {
                sourceCandidate = substringMatches[0];
            }
        }

        if (sourceCandidate != null)
        {
            var fallbackMapping = allMappings.FirstOrDefault(m => m.Scope1ChildId == sourceCandidate.Id || m.Scope2ChildId == sourceCandidate.Id);
            return (sourceCandidate, fallbackMapping);
        }

        return (null, null);
    }

    /// <summary>
    /// ส่งออกรายการ Activity Topics เป็นไฟล์ Excel (.xlsx) ตามมาตรฐาน ACAMP Standard Headers (Source, Emission, Activity)
    /// </summary>
    public async Task<byte[]> ExportTopicsToExcelAsync(List<TbActivityTopic> topics, int? selectedYear, string contextName)
    {
        #region hide [8/29/2026]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Activity Topics");

        // Header Formatting
        var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
        var headerFg = XLColor.White;

        string[] headers = new[]
        {
            "#",
            "Title / Topic Name",
            "Topic Type",
            "Source",
            "Emission",
            "Activity",
            "Scope",
            "Quantity",
            "Unit",
            "EF",
            "Share (%)",
            "kgCO2e",
            "Remark"
        };

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = headerFg;
            cell.Style.Fill.BackgroundColor = headerBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        int rowIdx = 2;
        int itemSeq = 1;

        foreach (var topic in topics)
        {
            var activeChilds = topic.Childs.Where(c => !c.IsDeleted && c.IsActive).ToList();
            if (!activeChilds.Any() && topic.Childs.Any()) activeChilds.Add(topic.Childs.First());

            var qty = topic.Events.Where(e => !e.IsDeleted && (!selectedYear.HasValue || selectedYear.Value <= 0 || e.TimeStamp.Year == selectedYear.Value)).Sum(e => e.Quantity);
            var isMultiChild = activeChilds.Count > 1;

            for (int childIdx = 0; childIdx < activeChilds.Count; childIdx++)
            {
                var ch = activeChilds[childIdx];
                var grp = ch.ActivityGroup;
                var src = ch.Source;
                // [2026-09-19][Antigravity] แสดงชื่อ Emission Source และค่า EF ที่แท้จริงเสมอ ไม่ mask ชื่อเป็น [Custom EF - System Internal]
                var emGroup = src?.Group ?? topic.EmissionGroup;
                var scopeNo = grp?.TgoScopeNo ?? src?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                var scopeStr = grp != null ? FormatScope(grp.TgoScopeNo) : (src?.Group != null ? FormatScope(src.Group.TgoScopeNo) : "-");
                var emGroupTitle = emGroup?.Title ?? "-";
                var grpTitle = grp?.Title ?? "-";
                var srcTitle = src?.Title ?? "-";
                var unitTitle = ch.ActivityUnit?.Title ?? src?.Unit?.Title ?? "-";

                var displayEf = (src?.EF ?? 0m) * (ch.UnitConversionRatio ?? 1m);
                var childKg = EmissionCalculator.CalculateKgCO2e(qty, ch);

                bool isMasterRow = (childIdx == 0);

                // Apply ACAMP Standard Naming for Child Topics
                string displayTopicTitle = topic.Title;
                if (!isMasterRow)
                {
                    if (scopeNo == eTgoScopeNo.Scope3)
                    {
                        displayTopicTitle = $"[การได้มา] {topic.Title}";
                    }
                    else if (scopeNo == eTgoScopeNo.SeparateReport)
                    {
                        displayTopicTitle = $"[Biogenic] {topic.Title}";
                    }
                    else
                    {
                        displayTopicTitle = topic.Title;
                    }
                }

                if (isMasterRow)
                {
                    worksheet.Cell(rowIdx, 1).Value = itemSeq++;
                    worksheet.Cell(rowIdx, 2).Value = displayTopicTitle;
                    worksheet.Cell(rowIdx, 3).Value = isMultiChild ? "Master (Primary)" : "Master";
                }
                else
                {
                    worksheet.Cell(rowIdx, 1).Value = string.Empty;
                    worksheet.Cell(rowIdx, 2).Value = displayTopicTitle;
                    worksheet.Cell(rowIdx, 3).Value = scopeNo == eTgoScopeNo.Scope3 ? "Child (Scope 3 WTT)" : (scopeNo == eTgoScopeNo.SeparateReport ? "Child (Biogenic)" : $"Child ({childIdx + 1})");
                }

                worksheet.Cell(rowIdx, 4).Value = srcTitle;
                worksheet.Cell(rowIdx, 5).Value = emGroupTitle;
                worksheet.Cell(rowIdx, 6).Value = grpTitle;
                worksheet.Cell(rowIdx, 7).Value = scopeStr;

                worksheet.Cell(rowIdx, 8).Value = (double)qty;
                worksheet.Cell(rowIdx, 8).Style.NumberFormat.Format = "#,##0.00";

                worksheet.Cell(rowIdx, 9).Value = unitTitle;

                worksheet.Cell(rowIdx, 10).Value = (double)displayEf;
                worksheet.Cell(rowIdx, 10).Style.NumberFormat.Format = "0.0000";

                worksheet.Cell(rowIdx, 11).Value = (double)ch.EmissionSharePercent;
                worksheet.Cell(rowIdx, 11).Style.NumberFormat.Format = "0.0";

                worksheet.Cell(rowIdx, 12).Value = (double)childKg;
                worksheet.Cell(rowIdx, 12).Style.NumberFormat.Format = "#,##0.00";

                worksheet.Cell(rowIdx, 13).Value = isMasterRow ? topic.Remark : string.Empty;

                rowIdx++;
            }

            if (!activeChilds.Any())
            {
                worksheet.Cell(rowIdx, 1).Value = itemSeq++;
                worksheet.Cell(rowIdx, 2).Value = topic.Title;
                worksheet.Cell(rowIdx, 3).Value = "Master";
                worksheet.Cell(rowIdx, 4).Value = "-";
                worksheet.Cell(rowIdx, 5).Value = topic.EmissionGroup?.Title ?? "-";
                worksheet.Cell(rowIdx, 6).Value = "-";
                worksheet.Cell(rowIdx, 7).Value = "-";
                worksheet.Cell(rowIdx, 8).Value = (double)qty;
                worksheet.Cell(rowIdx, 9).Value = "-";
                worksheet.Cell(rowIdx, 10).Value = 0.0;
                worksheet.Cell(rowIdx, 11).Value = 100.0;
                worksheet.Cell(rowIdx, 12).Value = 0.0;
                worksheet.Cell(rowIdx, 13).Value = topic.Remark;
                rowIdx++;
            }
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();
        worksheet.Row(1).Height = 25;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }

    /// <summary>
    /// [2026-09-21][Gemini] V2: ส่งออกเฉพาะ Master Topics โดยตัดคอลัมน์ [Topic Type] ออก เพื่อให้ 1 แถวใน Excel = 1 Topic เสมอ
    /// ส่วน Child Sources (Scope 3 WTT และ Biogenic CO2) จะถูก Auto-Map ในเบื้องหลังผ่าน TbEmissionMapping เมื่อนำเข้ากลับมา
    /// </summary>
    public async Task<byte[]> ExportTopicsToExcelV2Async(List<TbActivityTopic> topics, int? selectedYear, string contextName)
    {
        #region hide [2026-09-21]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Activity Topics");

        // Header Formatting
        var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
        var headerFg = XLColor.White;

        string[] headers = new[]
        {
            "#",
            "Title / Topic Name",
            "Source",
            "Emission",
            "Activity",
            "Scope",
            "Quantity",
            "Unit",
            "EF",
            "Share (%)",
            "kgCO2e",
            "Remark"
        };

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = headerFg;
            cell.Style.Fill.BackgroundColor = headerBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        int rowIdx = 2;
        int itemSeq = 1;

        foreach (var topic in topics)
        {
            var activeChilds = topic.Childs.Where(c => !c.IsDeleted && c.IsActive).ToList();
            var ch = activeChilds.FirstOrDefault() ?? topic.Childs.FirstOrDefault();

            var qty = topic.Events.Where(e => !e.IsDeleted && (!selectedYear.HasValue || selectedYear.Value <= 0 || e.TimeStamp.Year == selectedYear.Value)).Sum(e => e.Quantity);

            if (ch != null)
            {
                var grp = ch.ActivityGroup;
                var src = ch.Source;
                var emGroup = src?.Group ?? topic.EmissionGroup;
                var scopeStr = grp != null ? FormatScope(grp.TgoScopeNo) : (src?.Group != null ? FormatScope(src.Group.TgoScopeNo) : "-");
                var emGroupTitle = emGroup?.Title ?? "-";
                var grpTitle = grp?.Title ?? "-";
                var srcTitle = src?.Title ?? "-";
                var unitTitle = ch.ActivityUnit?.Title ?? src?.Unit?.Title ?? "-";

                var displayEf = (src?.EF ?? 0m) * (ch.UnitConversionRatio ?? 1m);
                var topicKg = EmissionCalculator.CalculateKgCO2e(qty, ch);

                worksheet.Cell(rowIdx, 1).Value = itemSeq++;
                worksheet.Cell(rowIdx, 2).Value = topic.Title;
                worksheet.Cell(rowIdx, 3).Value = srcTitle;
                worksheet.Cell(rowIdx, 4).Value = emGroupTitle;
                worksheet.Cell(rowIdx, 5).Value = grpTitle;
                worksheet.Cell(rowIdx, 6).Value = scopeStr;

                worksheet.Cell(rowIdx, 7).Value = (double)qty;
                worksheet.Cell(rowIdx, 7).Style.NumberFormat.Format = "#,##0.00";

                worksheet.Cell(rowIdx, 8).Value = unitTitle;

                worksheet.Cell(rowIdx, 9).Value = (double)displayEf;
                worksheet.Cell(rowIdx, 9).Style.NumberFormat.Format = "0.0000";

                worksheet.Cell(rowIdx, 10).Value = (double)ch.EmissionSharePercent;
                worksheet.Cell(rowIdx, 10).Style.NumberFormat.Format = "0.0";

                worksheet.Cell(rowIdx, 11).Value = (double)topicKg;
                worksheet.Cell(rowIdx, 11).Style.NumberFormat.Format = "#,##0.00";

                worksheet.Cell(rowIdx, 12).Value = topic.Remark;

                rowIdx++;
            }
            else
            {
                worksheet.Cell(rowIdx, 1).Value = itemSeq++;
                worksheet.Cell(rowIdx, 2).Value = topic.Title;
                worksheet.Cell(rowIdx, 3).Value = "-";
                worksheet.Cell(rowIdx, 4).Value = topic.EmissionGroup?.Title ?? "-";
                worksheet.Cell(rowIdx, 5).Value = "-";
                worksheet.Cell(rowIdx, 6).Value = "-";
                worksheet.Cell(rowIdx, 7).Value = (double)qty;
                worksheet.Cell(rowIdx, 8).Value = "-";
                worksheet.Cell(rowIdx, 9).Value = 0.0;
                worksheet.Cell(rowIdx, 10).Value = 100.0;
                worksheet.Cell(rowIdx, 11).Value = 0.0;
                worksheet.Cell(rowIdx, 12).Value = topic.Remark;

                rowIdx++;
            }
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();
        worksheet.Row(1).Height = 25;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }

    /// <summary>
    /// สร้างแม่แบบไฟล์ Excel (.xlsx) สำหรับการนำเข้าเฉพาะ Master Topics ตามมาตรฐาน ACAMP Standard Headers (Source, Emission, Activity)
    /// </summary>
    public async Task<byte[]> GenerateTemplateExcelAsync()
    {
        #region hide [8/29/2026]
        using var workbook = new XLWorkbook();

        // Sheet 1: Template
        var wsTemplate = workbook.Worksheets.Add("Import Activity Topics");

        var headerBg = XLColor.FromHtml("#059669");
        var headerFg = XLColor.White;

        string[] headers = new[]
        {
            "#",
            "Title / Topic Name*",
            "Source*",
            "Emission",
            "Activity",
            "Scope",
            "Unit",
            "EF",
            "Share (%)",
            "Remark"
        };

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = wsTemplate.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = headerFg;
            cell.Style.Fill.BackgroundColor = headerBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        // Sample Master Topics Data Rows (Scope 3 WTT / Biogenic are automatically handled by ACAMP)
        object[][] sampleRows = new object[][]
        {
            // 1. Scope 1 Diesel (Auto-maps [การได้มา] Scope 3 WTT and [Biogenic] Biogenic CO2)
            new object[] { 1, "เครื่องกำเนิดไฟฟ้าดีเซลสำรอง", "ดีเซล (Stationary)", "การเผาไหม้ที่อยู่กับที่ (Stationary Combustion)", "การเผาไหม้เชื้อเพลิงอยู่กับที่", "Scope 1", "L", 2.7080, 100, "เครื่องกำเนิดไฟฟ้า 500 kVA อาคาร A" },
            
            // 2. Scope 2 Electricity (Auto-maps [การได้มา] Scope 3 WTT)
            new object[] { 2, "ไฟฟ้าจากการไฟฟ้าส่วนภูมิภาค (PEA) มิเตอร์หลักท่าข้าวเดชอุดม", "ไฟฟ้าแบบ grid mix ปี 2022-2024; LCIA method IPCC 2013 GWP 100a V1.03 (CFO Scope2)", "การใช้พลังงานทางอ้อม (Purchased Electricity)", "การใช้พลังงานไฟฟ้า", "Scope 2", "kWh", 0.4750, 100, "มิเตอร์หลัก MEA/PEA อาคารสำนักงาน" },
            
            // 3. Scope 3 Tap Water
            new object[] { 3, "การใช้น้ำประปาอาคาร", "น้ำประปา", "สินค้าและบริการที่ซื้อมา (Purchased Goods and Services)", "การใช้น้ำประปา", "Scope 3", "m3", 0.7200, 100, "น้ำประปานครหลวง" }
        };

        for (int r = 0; r < sampleRows.Length; r++)
        {
            for (int c = 0; c < sampleRows[r].Length; c++)
            {
                wsTemplate.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(sampleRows[r][c]);
            }
            wsTemplate.Cell(r + 2, 8).Style.NumberFormat.Format = "0.0000";
            wsTemplate.Cell(r + 2, 9).Style.NumberFormat.Format = "0.0";
        }

        wsTemplate.Columns().AdjustToContents();
        wsTemplate.Row(1).Height = 25;
        wsTemplate.SheetView.FreezeRows(1);

        // [2026-09-15][Antigravity] เว้นว่าง 2 แถว (แถวที่ 5-6) แล้วจึงใส่คำอธิบายแม่แบบเป็นตัวอักษรสีเทา เริ่มต้นที่แถวที่ 7
        int noteRow = sampleRows.Length + 4; // แถวที่ 7 (เว้นว่าง 2 แถวจากข้อมูลตัวอย่างแถวที่ 4)

        var note1 = wsTemplate.Cell(noteRow, 1);
        note1.Value = "คำอธิบาย: ไฟล์นี้คือแม่แบบสำหรับนำเข้าหัวข้อกิจกรรม (Topics) ของระบบ CARBON ONE / ACAMP — แถวที่ 2-4 เป็นข้อมูลตัวอย่าง ให้ลบทิ้งหรือแก้ไขเป็นข้อมูลจริงก่อนนำเข้า";
        note1.Style.Font.Italic = true;
        note1.Style.Font.FontColor = XLColor.Gray;

        var note2 = wsTemplate.Cell(noteRow + 1, 1);
        note2.Value = "หมายเหตุ 1: กรอกเฉพาะรายการหลัก (Master Topics) เท่านั้น ระบบจะแตกรายการย่อยที่เกี่ยวข้อง ([การได้มา] Scope 3 WTT และ [Biogenic] Biogenic CO2) ให้อัตโนมัติเบื้องหลังตาม Emission Mapping";
        note2.Style.Font.Italic = true;
        note2.Style.Font.FontColor = XLColor.Gray;

        var note3 = wsTemplate.Cell(noteRow + 2, 1);
        note3.Value = "หมายเหตุ 2: คอลัมน์ Title / Topic Name* และ Source* เป็นข้อมูลสำคัญ ส่วนคอลัมน์ Emission, Activity, Scope, Unit, EF สามารถเว้นว่างไว้เพื่อใช้ค่าเริ่มต้นของระบบ หรือเลือกจับคู่ใหม่ในหน้าจอ Import";
        note3.Style.Font.Italic = true;
        note3.Style.Font.FontColor = XLColor.Gray;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }

    /// <summary>
    /// อ่านไฟล์ Excel นำเข้า แปลงข้อมูล และตรวจสอบความถูกต้อง
    /// ใช้งาน TbEmissionMapping จากฐานข้อมูลเป็นหลักในการจับคู่แหล่งกำเนิดและสร้าง Child Topics (Scope 3 WTT / Biogenic) อัตโนมัติ
    /// </summary>
    public async Task<List<ActivityTopicImportItemDto>> ParseImportExcelAsync(
        Stream stream,
        int? subscriptionId = null,
        eTopicLevel? level = null,
        int? targetId = null)
    {
        #region hide [8/29/2026]
        using var context = await _dbFactory.CreateDbContextAsync();
        var allGroups = await context.ActivityGroups.Where(g => !g.IsDeleted).ToListAsync();

        var emGroupQuery = context.EmissionGroups.Where(eg => !eg.IsDeleted && eg.IsActive);
        var unitQuery = context.Units.Where(u => !u.IsDeleted && u.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var sid = subscriptionId.Value;
            emGroupQuery = emGroupQuery.Where(eg => eg.SourceType == eSourceType.Default || eg.SubscriptionId == sid || eg.SubscriptionId == null);
            unitQuery = unitQuery.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == sid || u.SubscriptionId == null);
        }
        else
        {
            emGroupQuery = emGroupQuery.Where(eg => eg.SourceType == eSourceType.Default || eg.SubscriptionId == null);
            unitQuery = unitQuery.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == null);
        }

        var allEmissionGroups = await emGroupQuery.ToListAsync();
        var allUnits = await unitQuery.ToListAsync();

        // Fetch all available Emission Sources (Default + Subscription Custom)
        var sourceQuery = context.EmissionSources
            .Include(s => s.Unit)
            .Include(s => s.Group)
            .Where(s => !s.IsDeleted && s.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var sid = subscriptionId.Value;
            sourceQuery = sourceQuery.Where(s => s.SourceType == eSourceType.Default || s.SubscriptionId == sid || s.SubscriptionId == null);
        }
        else
        {
            sourceQuery = sourceQuery.Where(s => s.SourceType == eSourceType.Default || s.SubscriptionId == null);
        }

        var availableSources = await sourceQuery.ToListAsync();

        // Fetch all emission mappings from database
        var allMappings = await context.EmissionMappings
            .Where(m => !m.IsDeleted && (m.SubscriptionId == null || m.SubscriptionId == subscriptionId))
            .ToListAsync();

        // Fetch existing context topics for automatic master topic matching
        List<TbActivityTopic> existingContextTopics = new();
        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var topicQuery = context.Topics
                .Include(t => t.EmissionGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                .Where(t => t.SubscriptionId == subscriptionId.Value && !t.IsDeleted && t.IsActive);

            if (level.HasValue)
            {
                topicQuery = topicQuery.Where(t => t.Level == level.Value);
            }

            if (targetId.HasValue && targetId.Value > 0)
            {
                topicQuery = topicQuery.Where(t => t.TargetId == targetId.Value);
            }

            existingContextTopics = await topicQuery.ToListAsync();
        }

        var result = new List<ActivityTopicImportItemDto>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return result;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        // Dynamic Header Column Detection (Supports arbitrary column ordering)
        int colTitle = -1;
        int colSource = -1;
        int colEmGroup = -1;
        int colActGroup = -1;
        int colScope = -1;
        int colUnit = -1;
        int colEf = -1;
        int colShare = -1;
        int colRemark = -1;

        var firstRow = worksheet.Row(1);
        int lastCol = firstRow.LastCellUsed()?.Address.ColumnNumber ?? 15;

        for (int c = 1; c <= lastCol; c++)
        {
            var hText = GetCellString(firstRow.Cell(c)).ToLowerInvariant();
            if (string.IsNullOrEmpty(hText)) continue;

            // Skip #, No, Index headers
            if (hText == "#" || hText == "no" || hText == "sequence" || hText == "ลำดับ")
            {
                continue;
            }

            if (colTitle == -1 && (hText.Contains("title / topic name") || hText.Contains("title / activity") || hText.Contains("topic title") || hText.Contains("topic name") || hText.Contains("activity name") || hText.Contains("ชื่อกิจกรรม") || hText.Contains("ชื่อหัวข้อ") || hText.Contains("ชื่อรายการ") || (hText.Contains("title") && !hText.Contains("group") && !hText.Contains("source") && !hText.Contains("parent")) || (hText.Contains("activity") && !hText.Contains("group") && !hText.Contains("unit") && hText != "activity") || hText == "name"))
            {
                colTitle = c;
                continue;
            }

            if (colSource == -1 && (hText == "source" || hText == "source*" || hText.Contains("emission source") || hText.Contains("source name") || hText.Contains("แหล่งกำเนิด") || hText.Contains("แหล่งปล่อย") || hText.Contains("ปัจจัยการปล่อย") || (hText.Contains("source") && !hText.Contains("group") && !hText.Contains("ref"))))
            {
                colSource = c;
                continue;
            }

            if (colEmGroup == -1 && (hText == "emission" || hText.Contains("emission group") || hText.Contains("กลุ่มการปล่อย") || hText.Contains("กลุ่มแหล่งกำเนิด") || (hText.Contains("emission") && !hText.Contains("source") && !hText.Contains("factor") && !hText.Contains("share"))))
            {
                colEmGroup = c;
                continue;
            }

            if (colActGroup == -1 && (hText == "activity" || hText.Contains("activity group") || hText.Contains("กลุ่มกิจกรรม") || (hText.Contains("activity") && !hText.Contains("title") && !hText.Contains("name"))))
            {
                colActGroup = c;
                continue;
            }

            if (colScope == -1 && (hText == "scope" || hText.Contains("ขอบเขต")))
            {
                colScope = c;
                continue;
            }

            if (colUnit == -1 && (hText == "unit" || hText.Contains("standard unit") || hText.Contains("activity unit") || hText.Contains("หน่วย") || hText == "uom"))
            {
                colUnit = c;
                continue;
            }

            if (colEf == -1 && (hText == "ef" || hText.Contains("standard ef") || hText.Contains("emission factor") || hText.Contains("ef (kgco2e/unit)") || hText.Contains("ค่า ef") || (hText.Contains("ef") && !hText.Contains("ref") && !hText.Contains("kgco2e"))))
            {
                colEf = c;
                continue;
            }

            if (colShare == -1 && (hText.Contains("share") || hText.Contains("emission share") || hText.Contains("share (%)") || hText.Contains("share %") || hText.Contains("สัดส่วน") || hText.Contains("percentage")))
            {
                colShare = c;
                continue;
            }

            if (colRemark == -1 && (hText.Contains("remark") || hText.Contains("description") || hText.Contains("note") || hText.Contains("หมายเหตุ") || hText.Contains("คำอธิบาย")))
            {
                colRemark = c;
                continue;
            }
        }

        // Fallback to positional indexing only if essential headers were not recognized
        bool anyHeaderDetected = colTitle != -1 || colSource != -1 || colUnit != -1 || colEf != -1;
        if (!anyHeaderDetected)
        {
            colTitle = 2;
            colSource = 3;
            colEmGroup = 4;
            colActGroup = 5;
            colScope = 6;
            colUnit = 7;
            colEf = 8;
            colShare = 9;
            if (lastCol >= 10) colRemark = 10;
        }

        for (int row = 2; row <= lastRow; row++)
        {
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้าย (" ", "  ", หรือมากกว่า รวมถึง NBSP / whitespace)
            var rawTitle = colTitle > 0 ? TrimTitle(GetCellString(worksheet.Cell(row, colTitle))) : string.Empty;
            var sourceName = colSource > 0 ? GetCellString(worksheet.Cell(row, colSource)) : string.Empty;
            var emGroupStr = colEmGroup > 0 ? GetCellString(worksheet.Cell(row, colEmGroup)) : string.Empty;
            var actGroupStr = colActGroup > 0 ? GetCellString(worksheet.Cell(row, colActGroup)) : string.Empty;
            var scopeStr = colScope > 0 ? GetCellString(worksheet.Cell(row, colScope)) : string.Empty;
            var unitStr = colUnit > 0 ? GetCellString(worksheet.Cell(row, colUnit)) : string.Empty;
            var efStr = colEf > 0 ? GetCellString(worksheet.Cell(row, colEf)) : string.Empty;
            var shareStr = colShare > 0 ? GetCellString(worksheet.Cell(row, colShare)) : string.Empty;
            var remark = colRemark > 0 ? GetCellString(worksheet.Cell(row, colRemark)) : string.Empty;

            if (string.IsNullOrEmpty(rawTitle) && string.IsNullOrEmpty(sourceName) && string.IsNullOrEmpty(unitStr) && string.IsNullOrEmpty(efStr))
            {
                continue; // Skip empty row
            }

            // [2026-09-15][Antigravity] ข้ามแถวคำอธิบายและหมายเหตุของแม่แบบ เพื่อป้องกันข้อผิดพลาดกรณีผู้ใช้นำเข้าโดยไม่ได้ลบแถวดังกล่าว
            if (rawTitle.StartsWith("คำอธิบาย", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("หมายเหตุ", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("คำแนะนำ", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            decimal parsedEf = 0m;
            if (!string.IsNullOrEmpty(efStr) && decimal.TryParse(efStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var valEf))
            {
                parsedEf = valEf;
            }

            float parsedShare = ParseSharePercent(shareStr);

            // In Master-only import, every row is a Master Topic
            var item = new ActivityTopicImportItemDto
            {
                RowNumber = row,
                TopicTitle = rawTitle,
                MasterTopicTitle = rawTitle,
                ParentTopicTitle = string.Empty,
                IsChildTopic = false,
                ChildIndex = 0,
                SourceName = sourceName,
                EmissionGroupTitle = emGroupStr,
                ActivityGroupTitle = actGroupStr,
                Unit = unitStr,
                EF = parsedEf,
                ExcelEF = parsedEf,
                ExcelUnit = unitStr,
                Remark = remark,
                EmissionSharePercent = parsedShare
            };

            // Check against existing database topics
            if (existingContextTopics.Any() && !string.IsNullOrEmpty(rawTitle))
            {
                var matchedExistingTopic = existingContextTopics.FirstOrDefault(t => TrimTitle(t.Title).Equals(rawTitle, StringComparison.OrdinalIgnoreCase));
                if (matchedExistingTopic != null)
                {
                    item.IsExistingMasterMatched = true;
                    item.MatchedMasterTopicId = matchedExistingTopic.Id;
                    if (string.IsNullOrEmpty(emGroupStr) && matchedExistingTopic.EmissionGroup != null)
                    {
                        item.EmissionGroupId = matchedExistingTopic.EmissionGroupId;
                        item.EmissionGroupTitle = matchedExistingTopic.EmissionGroup.Title;
                    }
                }
            }

            // Validation 1: Topic / Master Title
            if (string.IsNullOrWhiteSpace(item.MasterTopicTitle))
            {
                item.IsValid = false;
                item.MappingState = eImportMappingState.Invalid;
                item.ValidationErrors.Add("Topic Title is required.");
            }

            // Validation 2: Emission Source Matching via Database Mapping Rules
            TbEmissionSource? matchedSource = null;
            TbEmissionMapping? matchedMapping = null;

            if (!string.IsNullOrWhiteSpace(sourceName) && !sourceName.Equals("Unknown", StringComparison.OrdinalIgnoreCase) && sourceName != "-1")
            {
                // Dynamic Database-Driven Lookup
                (matchedSource, matchedMapping) = ResolveSourceFromDatabase(sourceName, emGroupStr, actGroupStr, scopeStr, availableSources, allMappings);
            }
            else
            {
                // Fallback to Unknown Source (Id = -1) when source is omitted or specified as Unknown
                matchedSource = availableSources.FirstOrDefault(s => s.Id == -1);
                item.MappingState = eImportMappingState.NeedsMapping;
            }

            if (matchedSource != null)
            {
                item.EmissionSourceId = matchedSource.Id;
                item.MatchedSourceName = matchedSource.Title;
                item.EmissionGroupId = matchedSource.GroupId;
                item.EmissionGroupTitle = matchedSource.Group?.Title ?? emGroupStr;
                item.MatchedEF = matchedSource.EF;
                item.MatchedUnit = matchedSource.Unit?.Title ?? (string.IsNullOrWhiteSpace(unitStr) ? "-" : unitStr);
                item.MatchedUnitId = matchedSource.UnitId;

                // Automatically resolve ActivityGroup
                TbActivityGroup? resolvedActGroup = null;

                // 1) Match from user-provided Activity Group title
                if (!string.IsNullOrWhiteSpace(actGroupStr))
                {
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.Title.Equals(actGroupStr, StringComparison.OrdinalIgnoreCase))
                                    ?? allGroups.FirstOrDefault(g => g.Title.Contains(actGroupStr, StringComparison.OrdinalIgnoreCase));
                }

                // 2) Match from Emission Mapping logic
                if (resolvedActGroup == null)
                {
                    var actGroupId = TbEmissionMapping.GetActivityGroupIdForSource(matchedSource, allGroups);
                    if (actGroupId > 0)
                    {
                        resolvedActGroup = allGroups.FirstOrDefault(g => g.Id == actGroupId);
                    }
                }

                // 3) Match from user-provided Scope
                if (resolvedActGroup == null && !string.IsNullOrWhiteSpace(scopeStr))
                {
                    var parsedScope = ParseScope(scopeStr);
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.TgoScopeNo == parsedScope);
                }

                // 4) Fallback to Source Group Scope
                if (resolvedActGroup == null)
                {
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.TgoScopeNo == (matchedSource.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1));
                }

                if (resolvedActGroup != null)
                {
                    item.ActivityGroupId = resolvedActGroup.Id;
                    item.ActivityGroupCode = resolvedActGroup.Code;
                    item.ActivityGroupTitle = resolvedActGroup.Title;
                    item.Scope = resolvedActGroup.TgoScopeNo;
                }

                // Resolve Unit and Conversion Ratio
                if (!string.IsNullOrWhiteSpace(unitStr))
                {
                    var matchedUnit = allUnits.FirstOrDefault(u => u.Title.Equals(unitStr, StringComparison.OrdinalIgnoreCase));
                    if (matchedUnit != null)
                    {
                        item.MatchedUnitId = matchedUnit.Id;
                        item.MatchedUnit = matchedUnit.Title;
                    }
                }

                if (parsedEf > 0 && matchedSource.EF > 0 && parsedEf != matchedSource.EF)
                {
                    var ratio = parsedEf / matchedSource.EF;

                    if (ratio >= MinPlausibleConversionRatio && ratio <= MaxPlausibleConversionRatio)
                    {
                        // ต่างกันในระดับที่อธิบายได้ด้วยฐานหน่วยที่ต่างกัน — แปลงตามเดิม
                        item.UnitConversionRatio = ratio;
                        item.MatchedEF = parsedEf;
                    }
                    else
                    {
                        // ต่างกันมากเกินกว่าจะเป็นเรื่องหน่วย → น่าจะจับคู่ source ผิด ให้ผู้ใช้ยืนยันเอง
                        item.UnitConversionRatio = 1m;
                        item.MatchedEF = matchedSource.EF;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add($"EF from file ({parsedEf:N4}) differs from library source '{matchedSource.Title}' ({matchedSource.EF:N4}) by {ratio:N2}x. Please confirm the emission source.");
                    }
                }
                else
                {
                    item.UnitConversionRatio = 1m;
                    if (parsedEf == 0 && matchedSource.EF > 0)
                    {
                        item.EF = matchedSource.EF;
                    }
                }
            }
            else
            {
                // Source not found in library - register custom source on-the-fly
                if (!string.IsNullOrWhiteSpace(unitStr) || parsedEf > 0)
                {
                    item.MatchedSourceName = $"{sourceName} [Custom]";
                    item.MatchedEF = parsedEf;
                    item.IsCustomSourceNeeded = true;

                    // Unit: ยอมรับเฉพาะหน่วยที่ระบุมาและตรงกับไลบรารีจริง ห้ามเดาเป็นหน่วยตัวแรก
                    var matchedUnit = string.IsNullOrWhiteSpace(unitStr)
                        ? null
                        : allUnits.FirstOrDefault(u => u.Title.Equals(unitStr, StringComparison.OrdinalIgnoreCase));

                    if (matchedUnit != null)
                    {
                        item.MatchedUnitId = matchedUnit.Id;
                        item.MatchedUnit = matchedUnit.Title;
                    }
                    else
                    {
                        item.MatchedUnitId = null;
                        item.MatchedUnit = string.Empty;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(unitStr)
                            ? "Unit is required for a custom emission source. Please select the correct unit."
                            : $"Unit '{unitStr}' was not found in the library. Please select the correct unit.");
                    }

                    // Activity Group: ยอมรับเฉพาะที่ระบุชื่อมาชัดเจน — Scope อย่างเดียวระบุกลุ่มไม่ได้ ห้ามเดา
                    TbActivityGroup? resolvedActGroup = null;
                    if (!string.IsNullOrWhiteSpace(actGroupStr))
                    {
                        resolvedActGroup = allGroups.FirstOrDefault(g => g.Title.Equals(actGroupStr, StringComparison.OrdinalIgnoreCase))
                                        ?? allGroups.FirstOrDefault(g => g.Title.Contains(actGroupStr, StringComparison.OrdinalIgnoreCase));
                    }

                    if (resolvedActGroup != null)
                    {
                        item.ActivityGroupId = resolvedActGroup.Id;
                        item.ActivityGroupCode = resolvedActGroup.Code;
                        item.ActivityGroupTitle = resolvedActGroup.Title;
                        item.Scope = resolvedActGroup.TgoScopeNo;
                    }
                    else
                    {
                        item.ActivityGroupId = null;
                        item.ActivityGroupCode = string.Empty;
                        item.ActivityGroupTitle = string.Empty;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(actGroupStr)
                            ? "Activity Group could not be determined for this custom source. Please select it."
                            : $"Activity Group '{actGroupStr}' was not found in the library. Please select the correct one.");
                    }

                    // Emission Group: ยอมรับเฉพาะที่ระบุชื่อมาชัดเจน ห้าม fallback เป็นกลุ่มแรกในไลบรารี
                    var matchedEmGroup = string.IsNullOrWhiteSpace(emGroupStr)
                        ? null
                        : allEmissionGroups.FirstOrDefault(eg => eg.Title.Equals(emGroupStr, StringComparison.OrdinalIgnoreCase))
                          ?? allEmissionGroups.FirstOrDefault(eg => eg.Title.Contains(emGroupStr, StringComparison.OrdinalIgnoreCase));

                    if (matchedEmGroup != null)
                    {
                        item.EmissionGroupId = matchedEmGroup.Id;
                        item.EmissionGroupTitle = matchedEmGroup.Title;
                    }
                    else
                    {
                        item.EmissionGroupId = null;
                        item.EmissionGroupTitle = emGroupStr;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(emGroupStr)
                            ? "Emission Group could not be determined for this custom source. Please select it."
                            : $"Emission Group '{emGroupStr}' was not found in the library. Please select the correct one.");
                    }
                }
                else
                {
                    // หา source ไม่เจอ และไม่มีข้อมูลพอจะสร้าง custom source
                    // → ให้ผู้ใช้เลือก source เองในไดอะล็อก แทนการตีตกเป็น Invalid
                    matchedSource = availableSources.FirstOrDefault(s => s.Id == -1);
                    item.EmissionSourceId = matchedSource?.Id ?? -1;
                    item.MatchedSourceName = matchedSource?.Title ?? string.Empty;
                    item.MappingState = eImportMappingState.NeedsMapping;
                    item.ValidationErrors.Add($"Source '{sourceName}' could not be resolved from the library. Please select the correct emission source.");
                }
            }

            result.Add(item);

            // Auto-expand Child Topics directly from matched TbEmissionMapping (Scope 3 WTT / Biogenic CO2)
            if (item.IsValid && matchedMapping != null)
            {
                int childCount = 0;

                // 1) Scope 3 WTT Child with Standard Prefix [การได้มา]
                if (matchedMapping.Scope3ChildId > 0)
                {
                    var s3Source = availableSources.FirstOrDefault(s => s.Id == matchedMapping.Scope3ChildId);
                    if (s3Source != null)
                    {
                        var s3ActGroupId = TbEmissionMapping.GetActivityGroupIdForSource(s3Source, allGroups);
                        var s3ActGroup = allGroups.FirstOrDefault(g => g.Id == s3ActGroupId)
                                      ?? allGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.Scope3);

                        childCount++;
                        var s3Item = new ActivityTopicImportItemDto
                        {
                            RowNumber = row,
                            TopicTitle = $"[การได้มา] {item.TopicTitle}",
                            MasterTopicTitle = item.MasterTopicTitle,
                            ParentTopicTitle = item.MasterTopicTitle,
                            IsChildTopic = true,
                            ChildIndex = childCount,
                            SourceName = s3Source.Title,
                            MatchedSourceName = s3Source.Title,
                            EmissionSourceId = s3Source.Id,
                            EmissionGroupId = s3Source.GroupId,
                            EmissionGroupTitle = s3Source.Group?.Title ?? item.EmissionGroupTitle,
                            ActivityGroupId = s3ActGroup?.Id,
                            ActivityGroupCode = s3ActGroup?.Code ?? string.Empty,
                            ActivityGroupTitle = s3ActGroup?.Title ?? "Scope 3 (WTT)",
                            Scope = eTgoScopeNo.Scope3,
                            Unit = s3Source.Unit?.Title ?? item.Unit,
                            MatchedUnit = s3Source.Unit?.Title ?? item.MatchedUnit,
                            MatchedUnitId = s3Source.UnitId,
                            EF = s3Source.EF,
                            MatchedEF = s3Source.EF,
                            UnitConversionRatio = matchedMapping.UnitConversionRatio ?? 1m,
                            EmissionSharePercent = 100f,
                            Remark = "Auto-mapped [การได้มา] Scope 3 WTT",
                            IsValid = true,
                            IsExistingMasterMatched = item.IsExistingMasterMatched,
                            MatchedMasterTopicId = item.MatchedMasterTopicId
                        };
                        result.Add(s3Item);
                    }
                }

                // 2) Biogenic CO2 Child (Separate Report) with Standard Prefix [Biogenic]
                if (matchedMapping.BiogenicChildId > 0)
                {
                    var bioSource = availableSources.FirstOrDefault(s => s.Id == matchedMapping.BiogenicChildId);
                    if (bioSource != null)
                    {
                        var bioActGroupId = TbEmissionMapping.GetActivityGroupIdForSource(bioSource, allGroups);
                        var bioActGroup = allGroups.FirstOrDefault(g => g.Id == bioActGroupId)
                                       ?? allGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.SeparateReport);

                        childCount++;
                        var bioItem = new ActivityTopicImportItemDto
                        {
                            RowNumber = row,
                            TopicTitle = $"[Biogenic] {item.TopicTitle}",
                            MasterTopicTitle = item.MasterTopicTitle,
                            ParentTopicTitle = item.MasterTopicTitle,
                            IsChildTopic = true,
                            ChildIndex = childCount,
                            SourceName = bioSource.Title,
                            MatchedSourceName = bioSource.Title,
                            EmissionSourceId = bioSource.Id,
                            EmissionGroupId = bioSource.GroupId,
                            EmissionGroupTitle = bioSource.Group?.Title ?? item.EmissionGroupTitle,
                            ActivityGroupId = bioActGroup?.Id,
                            ActivityGroupCode = bioActGroup?.Code ?? string.Empty,
                            ActivityGroupTitle = bioActGroup?.Title ?? "Biogenic CO2",
                            Scope = eTgoScopeNo.SeparateReport,
                            Unit = bioSource.Unit?.Title ?? item.Unit,
                            MatchedUnit = bioSource.Unit?.Title ?? item.MatchedUnit,
                            MatchedUnitId = bioSource.UnitId,
                            EF = bioSource.EF,
                            MatchedEF = bioSource.EF,
                            UnitConversionRatio = 1m,
                            EmissionSharePercent = TbEmissionMapping.BiogenicChildEmissionSharePercent, // [2026-09-24][Claude] เดิมคูณ BiogenicSharePercent ซ้ำ
                            Remark = "Auto-mapped [Biogenic] Report in Biogenic CO2",
                            IsValid = true,
                            IsExistingMasterMatched = item.IsExistingMasterMatched,
                            MatchedMasterTopicId = item.MatchedMasterTopicId
                        };
                        result.Add(bioItem);
                    }
                }
            }
        }

        return result;
        #endregion
    }

    /// <summary>
    /// [2026-09-21][Gemini] V4: ปรับปรุงการนำเข้า Activity Topics รองรับทั้งไฟล์ Template และไฟล์ Export ข้อมูลเดิม
    /// โดยจำแนกแถว Master Topic ออกจาก Child Topic (Scope 3 WTT / Biogenic) ได้อย่างถูกต้อง ไม่ทำให้ Child กลายเป็น Master ซ้ำซ้อน
    /// </summary>
    public async Task<List<ActivityTopicImportItemDto>> ParseImportExcelV4Async(
        Stream stream,
        int? subscriptionId = null,
        eTopicLevel? level = null,
        int? targetId = null)
    {
        #region hide [2026-09-21]
        using var context = await _dbFactory.CreateDbContextAsync();
        var allGroups = await context.ActivityGroups.Where(g => !g.IsDeleted).ToListAsync();

        var emGroupQuery = context.EmissionGroups.Where(eg => !eg.IsDeleted && eg.IsActive);
        var unitQuery = context.Units.Where(u => !u.IsDeleted && u.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var sid = subscriptionId.Value;
            emGroupQuery = emGroupQuery.Where(eg => eg.SourceType == eSourceType.Default || eg.SubscriptionId == sid || eg.SubscriptionId == null);
            unitQuery = unitQuery.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == sid || u.SubscriptionId == null);
        }
        else
        {
            emGroupQuery = emGroupQuery.Where(eg => eg.SourceType == eSourceType.Default || eg.SubscriptionId == null);
            unitQuery = unitQuery.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == null);
        }

        var allEmissionGroups = await emGroupQuery.ToListAsync();
        var allUnits = await unitQuery.ToListAsync();

        // Fetch all available Emission Sources (Default + Subscription Custom)
        var sourceQuery = context.EmissionSources
            .Include(s => s.Unit)
            .Include(s => s.Group)
            .Where(s => !s.IsDeleted && s.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var sid = subscriptionId.Value;
            sourceQuery = sourceQuery.Where(s => s.SourceType == eSourceType.Default || s.SubscriptionId == sid || s.SubscriptionId == null);
        }
        else
        {
            sourceQuery = sourceQuery.Where(s => s.SourceType == eSourceType.Default || s.SubscriptionId == null);
        }

        var availableSources = await sourceQuery.ToListAsync();

        // Fetch all emission mappings from database
        var allMappings = await context.EmissionMappings
            .Where(m => !m.IsDeleted && (m.SubscriptionId == null || m.SubscriptionId == subscriptionId))
            .ToListAsync();

        // Fetch existing context topics for automatic master topic matching
        List<TbActivityTopic> existingContextTopics = new();
        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var topicQuery = context.Topics
                .Include(t => t.EmissionGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                .Where(t => t.SubscriptionId == subscriptionId.Value && !t.IsDeleted && t.IsActive);

            if (level.HasValue)
            {
                topicQuery = topicQuery.Where(t => t.Level == level.Value);
            }

            if (targetId.HasValue && targetId.Value > 0)
            {
                topicQuery = topicQuery.Where(t => t.TargetId == targetId.Value);
            }

            existingContextTopics = await topicQuery.ToListAsync();
        }

        var result = new List<ActivityTopicImportItemDto>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return result;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        // Dynamic Header Column Detection (Supports arbitrary column ordering)
        int colSeq = -1;
        int colTitle = -1;
        int colTopicType = -1;
        int colSource = -1;
        int colEmGroup = -1;
        int colActGroup = -1;
        int colScope = -1;
        int colUnit = -1;
        int colEf = -1;
        int colShare = -1;
        int colRemark = -1;

        var firstRow = worksheet.Row(1);
        int lastCol = firstRow.LastCellUsed()?.Address.ColumnNumber ?? 15;

        for (int c = 1; c <= lastCol; c++)
        {
            var hText = GetCellString(firstRow.Cell(c)).ToLowerInvariant();
            if (string.IsNullOrEmpty(hText)) continue;

            if (colSeq == -1 && (hText == "#" || hText == "no" || hText == "no." || hText == "sequence" || hText == "ลำดับ"))
            {
                colSeq = c;
                continue;
            }

            if (colTopicType == -1 && (hText == "topic type" || hText == "type" || hText == "topic_type" || hText.Contains("topic type") || hText == "ประเภท" || hText.Contains("ประเภทหัวข้อ") || hText.Contains("ประเภท")))
            {
                colTopicType = c;
                continue;
            }

            if (colTitle == -1 && (hText.Contains("title / topic name") || hText.Contains("title / activity") || hText.Contains("topic title") || hText.Contains("topic name") || hText.Contains("activity name") || hText.Contains("ชื่อกิจกรรม") || hText.Contains("ชื่อหัวข้อ") || hText.Contains("ชื่อรายการ") || (hText.Contains("title") && !hText.Contains("group") && !hText.Contains("source") && !hText.Contains("parent")) || (hText.Contains("activity") && !hText.Contains("group") && !hText.Contains("unit") && hText != "activity") || hText == "name"))
            {
                colTitle = c;
                continue;
            }

            if (colSource == -1 && (hText == "source" || hText == "source*" || hText.Contains("emission source") || hText.Contains("source name") || hText.Contains("แหล่งกำเนิด") || hText.Contains("แหล่งปล่อย") || hText.Contains("ปัจจัยการปล่อย") || (hText.Contains("source") && !hText.Contains("group") && !hText.Contains("ref"))))
            {
                colSource = c;
                continue;
            }

            if (colEmGroup == -1 && (hText == "emission" || hText.Contains("emission group") || hText.Contains("กลุ่มการปล่อย") || hText.Contains("กลุ่มแหล่งกำเนิด") || (hText.Contains("emission") && !hText.Contains("source") && !hText.Contains("factor") && !hText.Contains("share"))))
            {
                colEmGroup = c;
                continue;
            }

            if (colActGroup == -1 && (hText == "activity" || hText.Contains("activity group") || hText.Contains("กลุ่มกิจกรรม") || (hText.Contains("activity") && !hText.Contains("title") && !hText.Contains("name"))))
            {
                colActGroup = c;
                continue;
            }

            if (colScope == -1 && (hText == "scope" || hText.Contains("ขอบเขต")))
            {
                colScope = c;
                continue;
            }

            if (colUnit == -1 && (hText == "unit" || hText.Contains("standard unit") || hText.Contains("activity unit") || hText.Contains("หน่วย") || hText == "uom"))
            {
                colUnit = c;
                continue;
            }

            if (colEf == -1 && (hText == "ef" || hText.Contains("standard ef") || hText.Contains("emission factor") || hText.Contains("ef (kgco2e/unit)") || hText.Contains("ค่า ef") || (hText.Contains("ef") && !hText.Contains("ref") && !hText.Contains("kgco2e"))))
            {
                colEf = c;
                continue;
            }

            if (colShare == -1 && (hText.Contains("share") || hText.Contains("emission share") || hText.Contains("share (%)") || hText.Contains("share %") || hText.Contains("สัดส่วน") || hText.Contains("percentage")))
            {
                colShare = c;
                continue;
            }

            if (colRemark == -1 && (hText.Contains("remark") || hText.Contains("description") || hText.Contains("note") || hText.Contains("หมายเหตุ") || hText.Contains("คำอธิบาย")))
            {
                colRemark = c;
                continue;
            }
        }

        // Fallback to positional indexing only if essential headers were not recognized
        bool anyHeaderDetected = colTitle != -1 || colSource != -1 || colUnit != -1 || colEf != -1;
        if (!anyHeaderDetected)
        {
            colSeq = 1;
            colTitle = 2;
            colSource = 3;
            colEmGroup = 4;
            colActGroup = 5;
            colScope = 6;
            colUnit = 7;
            colEf = 8;
            colShare = 9;
            if (lastCol >= 10) colRemark = 10;
        }

        // Helper local functions for Master vs Child row classification
        bool IsChildRow(int rowNum, bool hasPriorMaster)
        {
            var title = colTitle > 0 ? TrimTitle(GetCellString(worksheet.Cell(rowNum, colTitle))) : string.Empty;
            var tType = colTopicType > 0 ? GetCellString(worksheet.Cell(rowNum, colTopicType)).Trim() : string.Empty;
            var seq = colSeq > 0 ? GetCellString(worksheet.Cell(rowNum, colSeq)).Trim() : string.Empty;

            if (tType.Contains("Child", StringComparison.OrdinalIgnoreCase)) return true;
            if (title.StartsWith("[การได้มา]", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("[Biogenic]", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("[WTT]", StringComparison.OrdinalIgnoreCase) ||
                title.StartsWith("[Biogenic CO2]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (hasPriorMaster && string.IsNullOrEmpty(seq) && !tType.Contains("Master", StringComparison.OrdinalIgnoreCase) && colTopicType > 0)
            {
                return true;
            }
            return false;
        }

        bool HasExplicitChildRows(int masterRowNum)
        {
            for (int nextRow = masterRowNum + 1; nextRow <= lastRow; nextRow++)
            {
                var nextTitle = colTitle > 0 ? TrimTitle(GetCellString(worksheet.Cell(nextRow, colTitle))) : string.Empty;
                var nextSource = colSource > 0 ? GetCellString(worksheet.Cell(nextRow, colSource)) : string.Empty;
                if (string.IsNullOrEmpty(nextTitle) && string.IsNullOrEmpty(nextSource)) continue;

                if (nextTitle.StartsWith("คำอธิบาย", StringComparison.OrdinalIgnoreCase) ||
                    nextTitle.StartsWith("หมายเหตุ", StringComparison.OrdinalIgnoreCase) ||
                    nextTitle.StartsWith("คำแนะนำ", StringComparison.OrdinalIgnoreCase) ||
                    nextTitle.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return IsChildRow(nextRow, true);
            }
            return false;
        }

        ActivityTopicImportItemDto? currentMaster = null;
        int currentMasterChildIndex = 0;

        for (int row = 2; row <= lastRow; row++)
        {
            var rawTitle = colTitle > 0 ? TrimTitle(GetCellString(worksheet.Cell(row, colTitle))) : string.Empty;
            var sourceName = colSource > 0 ? GetCellString(worksheet.Cell(row, colSource)) : string.Empty;
            var emGroupStr = colEmGroup > 0 ? GetCellString(worksheet.Cell(row, colEmGroup)) : string.Empty;
            var actGroupStr = colActGroup > 0 ? GetCellString(worksheet.Cell(row, colActGroup)) : string.Empty;
            var scopeStr = colScope > 0 ? GetCellString(worksheet.Cell(row, colScope)) : string.Empty;
            var unitStr = colUnit > 0 ? GetCellString(worksheet.Cell(row, colUnit)) : string.Empty;
            var efStr = colEf > 0 ? GetCellString(worksheet.Cell(row, colEf)) : string.Empty;
            var shareStr = colShare > 0 ? GetCellString(worksheet.Cell(row, colShare)) : string.Empty;
            var remark = colRemark > 0 ? GetCellString(worksheet.Cell(row, colRemark)) : string.Empty;

            if (string.IsNullOrEmpty(rawTitle) && string.IsNullOrEmpty(sourceName) && string.IsNullOrEmpty(unitStr) && string.IsNullOrEmpty(efStr))
            {
                continue; // Skip empty row
            }

            if (rawTitle.StartsWith("คำอธิบาย", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("หมายเหตุ", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("คำแนะนำ", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            decimal parsedEf = 0m;
            if (!string.IsNullOrEmpty(efStr) && decimal.TryParse(efStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var valEf))
            {
                parsedEf = valEf;
            }

            float parsedShare = ParseSharePercent(shareStr);

            bool isChild = IsChildRow(row, currentMaster != null);

            string masterTitle;
            if (isChild)
            {
                currentMasterChildIndex++;
                masterTitle = currentMaster != null ? currentMaster.MasterTopicTitle : rawTitle;

                // Strip standard prefixes to match parent master topic title
                foreach (var prefix in new[] { "[การได้มา]", "[Biogenic]", "[WTT]", "[Biogenic CO2]" })
                {
                    if (rawTitle.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = rawTitle.Substring(prefix.Length).Trim();
                        if (!string.IsNullOrEmpty(candidate))
                        {
                            masterTitle = candidate;
                        }
                        break;
                    }
                }
            }
            else
            {
                masterTitle = rawTitle;
                currentMasterChildIndex = 0;
            }

            var item = new ActivityTopicImportItemDto
            {
                RowNumber = row,
                TopicTitle = rawTitle,
                MasterTopicTitle = masterTitle,
                ParentTopicTitle = isChild ? masterTitle : string.Empty,
                IsChildTopic = isChild,
                ChildIndex = isChild ? currentMasterChildIndex : 0,
                SourceName = sourceName,
                EmissionGroupTitle = emGroupStr,
                ActivityGroupTitle = actGroupStr,
                Unit = unitStr,
                EF = parsedEf,
                ExcelEF = parsedEf,
                ExcelUnit = unitStr,
                Remark = remark,
                EmissionSharePercent = parsedShare
            };

            if (!isChild)
            {
                currentMaster = item;
            }

            // Check against existing database topics (Master only)
            if (!isChild && existingContextTopics.Any() && !string.IsNullOrEmpty(rawTitle))
            {
                var matchedExistingTopic = existingContextTopics.FirstOrDefault(t => TrimTitle(t.Title).Equals(rawTitle, StringComparison.OrdinalIgnoreCase));
                if (matchedExistingTopic != null)
                {
                    item.IsExistingMasterMatched = true;
                    item.MatchedMasterTopicId = matchedExistingTopic.Id;
                    item.ExistingAction = eExistingTopicAction.Skip;
                }
            }
            else if (isChild && currentMaster != null)
            {
                item.IsExistingMasterMatched = currentMaster.IsExistingMasterMatched;
                item.MatchedMasterTopicId = currentMaster.MatchedMasterTopicId;
                item.ExistingAction = currentMaster.ExistingAction;
            }

            if (string.IsNullOrEmpty(rawTitle))
            {
                item.IsValid = false;
                item.ValidationErrors.Add("Topic Title is required.");
            }

            // Semantic and Direct Emission Source Matching
            TbEmissionSource? matchedSource = null;
            TbEmissionMapping? matchedMapping = null;

            if (!string.IsNullOrEmpty(sourceName))
            {
                // Exact Match
                matchedSource = availableSources.FirstOrDefault(s => s.Title.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

                // Cleaned Title Match
                if (matchedSource == null)
                {
                    var cleanSrc = sourceName.Replace(" [Custom]", "").Trim();
                    matchedSource = availableSources.FirstOrDefault(s => s.Title.Equals(cleanSrc, StringComparison.OrdinalIgnoreCase));
                }

                // Substring Matching (Only for specific queries)
                if (matchedSource == null)
                {
                    var cleanSrc = sourceName.Replace(" [Custom]", "").Trim();
                    if (cleanSrc.Length >= MinSubstringSearchLength)
                    {
                        var candidateMatches = availableSources
                            .Where(s => s.Title.Contains(cleanSrc, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (candidateMatches.Count == MaxAmbiguousMatches)
                        {
                            matchedSource = candidateMatches[0];
                        }
                    }
                }

                // Semantic Bilingual Matching
                if (matchedSource == null)
                {
                    var lowerSrc = sourceName.ToLowerInvariant();
                    foreach (var pair in SynonymTranslations)
                    {
                        if (lowerSrc.Contains(pair.Key))
                        {
                            matchedSource = availableSources.FirstOrDefault(s => s.Title.Contains(pair.Value, StringComparison.OrdinalIgnoreCase));
                            if (matchedSource != null) break;
                        }
                    }
                }
            }

            // Find mapping if source exists
            if (matchedSource != null)
            {
                matchedMapping = allMappings.FirstOrDefault(m => m.Scope1ChildId == matchedSource.Id || m.Scope2ChildId == matchedSource.Id);
            }

            if (matchedSource != null && matchedSource.Id != -1)
            {
                item.EmissionSourceId = matchedSource.Id;
                item.MatchedSourceName = matchedSource.Title;
                item.EmissionGroupId = matchedSource.GroupId;
                item.EmissionGroupTitle = matchedSource.Group?.Title ?? emGroupStr;
                item.MatchedEF = matchedSource.EF;
                item.MatchedUnit = matchedSource.Unit?.Title ?? (string.IsNullOrWhiteSpace(unitStr) ? "-" : unitStr);
                item.MatchedUnitId = matchedSource.UnitId;

                // Automatically resolve ActivityGroup
                TbActivityGroup? resolvedActGroup = null;

                // 1) Match from user-provided Activity Group title
                if (!string.IsNullOrWhiteSpace(actGroupStr))
                {
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.Title.Equals(actGroupStr, StringComparison.OrdinalIgnoreCase))
                                    ?? allGroups.FirstOrDefault(g => g.Title.Contains(actGroupStr, StringComparison.OrdinalIgnoreCase));
                }

                // 2) Match from Emission Mapping logic
                if (resolvedActGroup == null)
                {
                    var actGroupId = TbEmissionMapping.GetActivityGroupIdForSource(matchedSource, allGroups);
                    if (actGroupId > 0)
                    {
                        resolvedActGroup = allGroups.FirstOrDefault(g => g.Id == actGroupId);
                    }
                }

                // 3) Match from user-provided Scope
                if (resolvedActGroup == null && !string.IsNullOrWhiteSpace(scopeStr))
                {
                    var parsedScope = ParseScope(scopeStr);
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.TgoScopeNo == parsedScope);
                }

                // 4) Fallback to Source Group Scope
                if (resolvedActGroup == null)
                {
                    resolvedActGroup = allGroups.FirstOrDefault(g => g.TgoScopeNo == (matchedSource.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1));
                }

                if (resolvedActGroup != null)
                {
                    item.ActivityGroupId = resolvedActGroup.Id;
                    item.ActivityGroupCode = resolvedActGroup.Code;
                    item.ActivityGroupTitle = resolvedActGroup.Title;
                    item.Scope = resolvedActGroup.TgoScopeNo;
                }

                // Resolve Unit and Conversion Ratio
                if (!string.IsNullOrWhiteSpace(unitStr))
                {
                    var matchedUnit = allUnits.FirstOrDefault(u => u.Title.Equals(unitStr, StringComparison.OrdinalIgnoreCase));
                    if (matchedUnit != null)
                    {
                        item.MatchedUnitId = matchedUnit.Id;
                        item.MatchedUnit = matchedUnit.Title;
                    }
                }

                if (parsedEf > 0 && matchedSource.EF > 0 && parsedEf != matchedSource.EF)
                {
                    var ratio = parsedEf / matchedSource.EF;

                    if (ratio >= MinPlausibleConversionRatio && ratio <= MaxPlausibleConversionRatio)
                    {
                        item.UnitConversionRatio = ratio;
                        item.MatchedEF = parsedEf;
                    }
                    else
                    {
                        item.UnitConversionRatio = 1m;
                        item.MatchedEF = matchedSource.EF;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add($"EF from file ({parsedEf:N4}) differs from library source '{matchedSource.Title}' ({matchedSource.EF:N4}) by {ratio:N2}x. Please confirm the emission source.");
                    }
                }
                else
                {
                    item.UnitConversionRatio = 1m;
                    if (parsedEf == 0 && matchedSource.EF > 0)
                    {
                        item.EF = matchedSource.EF;
                    }
                }
            }
            else
            {
                // Source not found in library - register custom source on-the-fly
                if (!string.IsNullOrWhiteSpace(unitStr) || parsedEf > 0)
                {
                    item.MatchedSourceName = $"{sourceName} [Custom]";
                    item.MatchedEF = parsedEf;
                    item.IsCustomSourceNeeded = true;

                    var matchedUnit = string.IsNullOrWhiteSpace(unitStr)
                        ? null
                        : allUnits.FirstOrDefault(u => u.Title.Equals(unitStr, StringComparison.OrdinalIgnoreCase));

                    if (matchedUnit != null)
                    {
                        item.MatchedUnitId = matchedUnit.Id;
                        item.MatchedUnit = matchedUnit.Title;
                    }
                    else
                    {
                        item.MatchedUnitId = null;
                        item.MatchedUnit = string.Empty;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(unitStr)
                            ? "Unit is required for a custom emission source. Please select the correct unit."
                            : $"Unit '{unitStr}' was not found in the library. Please select the correct unit.");
                    }

                    TbActivityGroup? resolvedActGroup = null;
                    if (!string.IsNullOrWhiteSpace(actGroupStr))
                    {
                        resolvedActGroup = allGroups.FirstOrDefault(g => g.Title.Equals(actGroupStr, StringComparison.OrdinalIgnoreCase))
                                        ?? allGroups.FirstOrDefault(g => g.Title.Contains(actGroupStr, StringComparison.OrdinalIgnoreCase));
                    }

                    if (resolvedActGroup != null)
                    {
                        item.ActivityGroupId = resolvedActGroup.Id;
                        item.ActivityGroupCode = resolvedActGroup.Code;
                        item.ActivityGroupTitle = resolvedActGroup.Title;
                        item.Scope = resolvedActGroup.TgoScopeNo;
                    }
                    else
                    {
                        item.ActivityGroupId = null;
                        item.ActivityGroupCode = string.Empty;
                        item.ActivityGroupTitle = string.Empty;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(actGroupStr)
                            ? "Activity Group could not be determined for this custom source. Please select it."
                            : $"Activity Group '{actGroupStr}' was not found in the library. Please select the correct one.");
                    }

                    var matchedEmGroup = string.IsNullOrWhiteSpace(emGroupStr)
                        ? null
                        : allEmissionGroups.FirstOrDefault(eg => eg.Title.Equals(emGroupStr, StringComparison.OrdinalIgnoreCase))
                          ?? allEmissionGroups.FirstOrDefault(eg => eg.Title.Contains(emGroupStr, StringComparison.OrdinalIgnoreCase));

                    if (matchedEmGroup != null)
                    {
                        item.EmissionGroupId = matchedEmGroup.Id;
                        item.EmissionGroupTitle = matchedEmGroup.Title;
                    }
                    else
                    {
                        item.EmissionGroupId = null;
                        item.EmissionGroupTitle = emGroupStr;
                        item.MappingState = eImportMappingState.NeedsMapping;
                        item.ValidationErrors.Add(string.IsNullOrWhiteSpace(emGroupStr)
                            ? "Emission Group could not be determined for this custom source. Please select it."
                            : $"Emission Group '{emGroupStr}' was not found in the library. Please select the correct one.");
                    }
                }
                else
                {
                    matchedSource = availableSources.FirstOrDefault(s => s.Id == -1);
                    item.EmissionSourceId = matchedSource?.Id ?? -1;
                    item.MatchedSourceName = matchedSource?.Title ?? string.Empty;
                    item.MappingState = eImportMappingState.NeedsMapping;
                    item.ValidationErrors.Add($"Source '{sourceName}' could not be resolved from the library. Please select the correct emission source.");
                }
            }

            result.Add(item);

            // Auto-expand Child Topics directly from matched TbEmissionMapping (Scope 3 WTT / Biogenic CO2)
            // ONLY if this Master Topic does NOT already have explicit child rows following it in the Excel file
            if (!isChild && item.IsValid && matchedMapping != null && !HasExplicitChildRows(row))
            {
                int childCount = 0;

                // 1) Scope 3 WTT Child with Standard Prefix [การได้มา]
                if (matchedMapping.Scope3ChildId > 0)
                {
                    var s3Source = availableSources.FirstOrDefault(s => s.Id == matchedMapping.Scope3ChildId);
                    if (s3Source != null)
                    {
                        var s3ActGroupId = TbEmissionMapping.GetActivityGroupIdForSource(s3Source, allGroups);
                        var s3ActGroup = allGroups.FirstOrDefault(g => g.Id == s3ActGroupId)
                                      ?? allGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.Scope3);

                        childCount++;
                        var s3Item = new ActivityTopicImportItemDto
                        {
                            RowNumber = row,
                            TopicTitle = $"[การได้มา] {item.TopicTitle}",
                            MasterTopicTitle = item.MasterTopicTitle,
                            ParentTopicTitle = item.MasterTopicTitle,
                            IsChildTopic = true,
                            ChildIndex = childCount,
                            SourceName = s3Source.Title,
                            MatchedSourceName = s3Source.Title,
                            EmissionSourceId = s3Source.Id,
                            EmissionGroupId = s3Source.GroupId,
                            EmissionGroupTitle = s3Source.Group?.Title ?? item.EmissionGroupTitle,
                            ActivityGroupId = s3ActGroup?.Id,
                            ActivityGroupCode = s3ActGroup?.Code ?? string.Empty,
                            ActivityGroupTitle = s3ActGroup?.Title ?? "Scope 3 (WTT)",
                            Scope = eTgoScopeNo.Scope3,
                            Unit = s3Source.Unit?.Title ?? item.Unit,
                            MatchedUnit = s3Source.Unit?.Title ?? item.MatchedUnit,
                            MatchedUnitId = s3Source.UnitId,
                            EF = s3Source.EF,
                            MatchedEF = s3Source.EF,
                            UnitConversionRatio = matchedMapping.UnitConversionRatio ?? 1m,
                            EmissionSharePercent = 100f,
                            Remark = "Auto-mapped [การได้มา] Scope 3 WTT",
                            IsValid = true,
                            IsExistingMasterMatched = item.IsExistingMasterMatched,
                            MatchedMasterTopicId = item.MatchedMasterTopicId
                        };
                        result.Add(s3Item);
                    }
                }

                // 2) Biogenic CO2 Child (Separate Report) with Standard Prefix [Biogenic]
                if (matchedMapping.BiogenicChildId > 0)
                {
                    var bioSource = availableSources.FirstOrDefault(s => s.Id == matchedMapping.BiogenicChildId);
                    if (bioSource != null)
                    {
                        var bioActGroupId = TbEmissionMapping.GetActivityGroupIdForSource(bioSource, allGroups);
                        var bioActGroup = allGroups.FirstOrDefault(g => g.Id == bioActGroupId)
                                       ?? allGroups.FirstOrDefault(g => g.TgoScopeNo == eTgoScopeNo.SeparateReport);

                        childCount++;
                        var bioItem = new ActivityTopicImportItemDto
                        {
                            RowNumber = row,
                            TopicTitle = $"[Biogenic] {item.TopicTitle}",
                            MasterTopicTitle = item.MasterTopicTitle,
                            ParentTopicTitle = item.MasterTopicTitle,
                            IsChildTopic = true,
                            ChildIndex = childCount,
                            SourceName = bioSource.Title,
                            MatchedSourceName = bioSource.Title,
                            EmissionSourceId = bioSource.Id,
                            EmissionGroupId = bioSource.GroupId,
                            EmissionGroupTitle = bioSource.Group?.Title ?? item.EmissionGroupTitle,
                            ActivityGroupId = bioActGroup?.Id,
                            ActivityGroupCode = bioActGroup?.Code ?? string.Empty,
                            ActivityGroupTitle = bioActGroup?.Title ?? "Biogenic CO2",
                            Scope = eTgoScopeNo.SeparateReport,
                            Unit = bioSource.Unit?.Title ?? item.Unit,
                            MatchedUnit = bioSource.Unit?.Title ?? item.MatchedUnit,
                            MatchedUnitId = bioSource.UnitId,
                            EF = bioSource.EF,
                            MatchedEF = bioSource.EF,
                            UnitConversionRatio = 1m,
                            EmissionSharePercent = TbEmissionMapping.BiogenicChildEmissionSharePercent, // [2026-09-24][Claude] เดิมคูณ BiogenicSharePercent ซ้ำ
                            Remark = "Auto-mapped [Biogenic] Report in Biogenic CO2",
                            IsValid = true,
                            IsExistingMasterMatched = item.IsExistingMasterMatched,
                            MatchedMasterTopicId = item.MatchedMasterTopicId
                        };
                        result.Add(bioItem);
                    }
                }
            }
        }

        return result;
        #endregion
    }

    /// <summary>
    /// ใช้ค่า Emission Source ที่ผู้ใช้เลือกเองในไดอะล็อก แล้วคำนวณ Unit / EF / UnitConversionRatio ใหม่
    /// </summary>
    public void ApplyManualSource(
        ActivityTopicImportItemDto item,
        TbEmissionSource source,
        TbActivityGroup activityGroup)
    {
        #region hide [2026-09-07]
        item.EmissionSourceId = source.Id;
        item.MatchedSourceName = source.Title;
        item.EmissionGroupId = source.GroupId;                        // บังคับให้ตรงกับ source เสมอ
        item.EmissionGroupTitle = source.Group?.Title ?? string.Empty;
        item.MatchedUnitId = source.UnitId;
        item.MatchedUnit = source.Unit?.Title ?? item.ExcelUnit;

        item.ActivityGroupId = activityGroup.Id;
        item.ActivityGroupCode = activityGroup.Code;
        item.ActivityGroupTitle = activityGroup.Title;
        item.Scope = activityGroup.TgoScopeNo;              // derived เสมอ

        // สูตรเดียวกับ auto-match เดิม (บรรทัด 774-787)
        if (item.ExcelEF > 0 && source.EF > 0 && item.ExcelEF != source.EF)
        {
            item.UnitConversionRatio = item.ExcelEF / source.EF;
            item.MatchedEF = item.ExcelEF;
        }
        else
        {
            item.UnitConversionRatio = 1m;
            item.MatchedEF = source.EF;
        }

        item.IsCustomSourceNeeded = false;
        item.IsManuallyMapped = true;
        item.MappingState = eImportMappingState.Resolved;
        item.ValidationErrors.Clear();
        item.IsValid = true;
        #endregion
    }

    /// <summary>
    /// [2026-09-19][Antigravity] ตัดช่องว่างหัวและท้าย ทั้ง space เดี่ยว (" "), หลายช่องว่าง ("  "), tabs, NBSP (\u00A0), และ whitespace ทุกชนิด
    /// </summary>
    public static string TrimTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        char[] whitespaceChars = [' ', '\t', '\r', '\n', '\u00A0', '\u200B', '\uFEFF', '\u3000'];
        return title.Trim(whitespaceChars).Trim();
    }

    private static string GetCellString(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return string.Empty;
        try
        {
            var val = cell.Value;
            if (val.IsBlank) return string.Empty;
            if (val.IsText) return val.GetText().Trim();
            if (val.IsNumber) return val.GetNumber().ToString(System.Globalization.CultureInfo.InvariantCulture).Trim();
            return cell.GetFormattedString().Trim();
        }
        catch
        {
            return cell.GetString()?.Trim() ?? string.Empty;
        }
    }
}
