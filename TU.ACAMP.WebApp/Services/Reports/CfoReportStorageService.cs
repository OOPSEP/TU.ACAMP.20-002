using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.WebApp.Services.Reports;

public class CfoReportStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly CfoReportService _cfoReportService;
    private readonly CfoWordReportService _cfoWordReportService;
    private readonly ILogger<CfoReportStorageService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public CfoReportStorageService(
        IWebHostEnvironment env,
        CfoReportService cfoReportService,
        CfoWordReportService cfoWordReportService,
        ILogger<CfoReportStorageService> logger)
    {
        _env = env;
        _cfoReportService = cfoReportService;
        _cfoWordReportService = cfoWordReportService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    private string GetStorageBaseDir(eTopicLevel level, int targetId)
    {
        var basePath = Path.Combine(_env.ContentRootPath, "Files", "Reports", $"{level}_{targetId}");
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }
        return basePath;
    }

    /// <summary>
    /// [2026-09-24][Claude] โฟลเดอร์ของ snapshot หนึ่งรายการ (ใช้เก็บเอกสารที่ต่อยอดจาก snapshot เช่น ISSB Climate Disclosure)
    /// คืน null เมื่อไม่พบ snapshot หรือ reportKey ไม่ถูกต้อง (กัน path traversal)
    /// </summary>
    public string? GetReportDirectory(string reportKey, eTopicLevel level, int targetId)
    {
        if (string.IsNullOrWhiteSpace(reportKey) || reportKey.Any(ch => !char.IsLetterOrDigit(ch))) return null;
        var dir = Path.Combine(GetStorageBaseDir(level, targetId), reportKey);
        return File.Exists(Path.Combine(dir, "report_snapshot.json")) ? dir : null;
    }

    public async Task<List<CfoReportSummaryItem>> GetReportsAsync(eTopicLevel level, int targetId)
    {
        var list = new List<CfoReportSummaryItem>();
        try
        {
            var baseDir = GetStorageBaseDir(level, targetId);
            var indexFile = Path.Combine(baseDir, "index.json");
            if (File.Exists(indexFile))
            {
                var json = await File.ReadAllTextAsync(indexFile);
                var items = JsonSerializer.Deserialize<List<CfoReportSummaryItem>>(json, _jsonOptions);
                if (items != null)
                {
                    return items.OrderByDescending(x => x.CreatedAt).ToList();
                }
            }

            // Fallback scan subdirectories
            foreach (var subDir in Directory.GetDirectories(baseDir))
            {
                var snapshotFile = Path.Combine(subDir, "report_snapshot.json");
                if (File.Exists(snapshotFile))
                {
                    var json = await File.ReadAllTextAsync(snapshotFile);
                    var doc = JsonSerializer.Deserialize<CfoReportDocument>(json, _jsonOptions);
                    if (doc != null)
                    {
                        list.Add(new CfoReportSummaryItem
                        {
                            ReportKey = doc.ReportKey,
                            Title = doc.Title,
                            Level = doc.Level,
                            TargetId = doc.TargetId,
                            OrganizationTitle = doc.OrganizationTitle,
                            OrganizationCode = doc.OrganizationCode,
                            BaseYear = doc.BaseYear,
                            CalculateYear = doc.CalculateYear,
                            CreatedAt = doc.CreatedAt,
                            CreatedBy = doc.CreatedBy,
                            Status = doc.Status,
                            TotalScope1_tCO2e = doc.CalculateYearReport.TotalScope1_tCO2e,
                            TotalScope2_tCO2e = doc.CalculateYearReport.TotalScope2_tCO2e,
                            TotalScope3_tCO2e = doc.CalculateYearReport.TotalScope3_tCO2e,
                            TotalGrossEmissions_tCO2e = doc.CalculateYearReport.TotalGrossEmissions_tCO2e,
                            HasExcelFile = File.Exists(Path.Combine(subDir, "report_ar5.xlsx")),
                            HasWordFile = File.Exists(Path.Combine(subDir, CfoWordTemplateMap.OutputFileName)),
                            FileDirectory = subDir
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading CFO report registry for {Level} ID {TargetId}", level, targetId);
        }

        return list.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public async Task<CfoReportDocument?> GetReportAsync(string reportKey, eTopicLevel level, int targetId)
    {
        try
        {
            var baseDir = GetStorageBaseDir(level, targetId);
            var reportDir = Path.Combine(baseDir, reportKey);
            var snapshotFile = Path.Combine(reportDir, "report_snapshot.json");
            if (File.Exists(snapshotFile))
            {
                var json = await File.ReadAllTextAsync(snapshotFile);
                return JsonSerializer.Deserialize<CfoReportDocument>(json, _jsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading CFO report snapshot {Key}", reportKey);
        }
        return null;
    }

    public async Task<CfoReportDocument> CreateReportSnapshotAsync(
        eTopicLevel level,
        int targetId,
        string title,
        int baseYear,
        int calculateYear,
        string createdBy,
        HashSet<int>? allowedSubscriptionIds = null,
        string? expiryAcknowledgmentReason = null,
        CfoReportService.CfoReportOverridesDto? overrides = null,
        List<int>? inScopeChildIds = null,
        List<string>? inScopeChildTitles = null,
        // [2026-09-18][Claude] รับค่า threshold จริงจาก wizard และผลคำนวณที่ pre-flight ทำไว้แล้ว เพื่อไม่ต้องคำนวณซ้ำ
        decimal thresholdPercent = 5.0m,
        CfoReportService.CfoAuditReportDto? precomputedCalcReport = null,
        CfoReportService.CfoAuditReportDto? precomputedBaseReport = null,
        // [2026-09-18][Claude] รายงานความคืบหน้ากลับไปหา UI ทีละขั้น ไม่ให้แถบค้างอยู่ค่าเดียวยาว ๆ
        IProgress<(int Percent, string Label)>? progress = null)
    {
        void Report(int percent, string label) => progress?.Report((percent, label));

        Report(8, $"Calculating emissions for year {calculateYear}...");
        var calcReport = precomputedCalcReport
            ?? await _cfoReportService.GenerateReportAsync(level, targetId, calculateYear, allowedSubscriptionIds, thresholdPercent, expiryAcknowledgmentReason, inScopeChildIds);

        Report(26, baseYear > 0 ? $"Calculating base year {baseYear}..." : "Skipping base year...");
        CfoReportService.CfoAuditReportDto? baseReport = precomputedBaseReport;
        if (baseReport == null && baseYear > 0)
        {
            baseReport = await _cfoReportService.GenerateReportAsync(level, targetId, baseYear, allowedSubscriptionIds, thresholdPercent, null, inScopeChildIds);
        }

        Report(38, "Creating report folder...");
        var reportKey = Guid.NewGuid().ToString("N");
        var baseDir = GetStorageBaseDir(level, targetId);
        var reportDir = Path.Combine(baseDir, reportKey);
        Directory.CreateDirectory(reportDir);

        var doc = new CfoReportDocument
        {
            ReportKey = reportKey,
            Title = string.IsNullOrWhiteSpace(title) ? $"รายงานการปล่อยก๊าซเรือนกระจก ประจำปี {calculateYear}" : title.Trim(),
            Level = level,
            TargetId = targetId,
            OrganizationTitle = !string.IsNullOrWhiteSpace(overrides?.OrganizationTitle) ? overrides.OrganizationTitle : calcReport.OrganizationTitle,
            OrganizationCode = calcReport.OrganizationCode,
            // [2026-09-15][Antigravity] Overrides for Fr-01 and Fr-03.2
            GhgTeamName = overrides?.GhgTeamName ?? $"{calcReport.LevelDisplay} GHG Team",
            CertifierOrgTitle = overrides?.CertifierOrgTitle ?? (!string.IsNullOrWhiteSpace(overrides?.OrganizationTitle) ? overrides.OrganizationTitle : calcReport.OrganizationTitle),
            Scope3CheckedCategories = overrides?.Scope3CheckedCategories?.ToList() ?? new List<int>(),
            // [2026-09-16][Antigravity] In-Scope Organizational Boundary (Option 1)
            InScopeChildIds = inScopeChildIds,
            InScopeChildTitles = inScopeChildTitles,
            BaseYear = baseYear,
            CalculateYear = calculateYear,
            PeriodDescription = calculateYear > 0 ? $"1 ม.ค. {calculateYear} - 31 ธ.ค. {calculateYear}" : "Historical",
            CreatedAt = AppTimeZone.LocalNow,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "Auditor / Verifier" : createdBy.Trim(),
            Status = "Verified / Locked",
            CalculateYearReport = calcReport,
            BaseYearReport = baseReport,
            HasExcelSnapshot = true,
            HasWordSnapshot = false
        };

        // [2026-09-18][Claude] คัดลอกค่า overrides ทั้งชุดลง snapshot
        // เดิมเก็บแค่ GhgTeamName / CertifierOrgTitle / Scope3CheckedCategories ทำให้ snapshot.json
        // มีค่า null เกือบทั้งหมดทั้งที่ค่าเหล่านี้ถูกเขียนลงไฟล์ Excel แล้ว -> หน้า View
        // ได้ข้อมูลไม่ครบ และสร้างไฟล์ซ้ำจาก snapshot จะไม่ได้ไฟล์เดิม
        // [2026-09-25][Gemini] กำหนดวันที่ปัจจุบัน (AppTimeZone.LocalNow.Date) เป็นค่า default อัตโนมัติหากไม่ได้ระบุ
        doc.PreparationDate = overrides?.PreparationDate ?? AppTimeZone.LocalNow.Date;
        doc.RegistrationDate = overrides?.RegistrationDate ?? AppTimeZone.LocalNow.Date;
        doc.Fr02DiagramDate = overrides?.Fr02DiagramDate ?? doc.PreparationDate;
        doc.Fr031DiagramDate = overrides?.Fr031DiagramDate ?? doc.PreparationDate;
        doc.Fr031CompletionDate = overrides?.Fr031CompletionDate ?? doc.PreparationDate;

        if (overrides != null)
        {
            doc.OrganizationImageBase64 = overrides.OrganizationImageBase64;
            doc.CalculatePeriodText = overrides.CalculatePeriodText;
            doc.CalculateProductionAmount = overrides.CalculateProductionAmount;
            doc.CalculateProductionUnit = overrides.CalculateProductionUnit;
            doc.CalcProductItems = overrides.CalcProductItems;
            doc.BasePeriodText = overrides.BasePeriodText;
            doc.BaseProductionAmount = overrides.BaseProductionAmount;
            doc.BaseProductionUnit = overrides.BaseProductionUnit;
            doc.BaseProductItems = overrides.BaseProductItems;
            doc.OrgInfoMultiLine = overrides.OrgInfoMultiLine;
            doc.OrgInfo1 = overrides.OrgInfo1;
            doc.OrgInfo2 = overrides.OrgInfo2;
            doc.OrgInfo3 = overrides.OrgInfo3;
            doc.OrgInfo4 = overrides.OrgInfo4;
            doc.OrgInfo5 = overrides.OrgInfo5;
            doc.ContactAddress = overrides.ContactAddress;
            doc.Fr02ImageBase64 = overrides.Fr02ImageBase64;
            doc.Fr031ImageBase64 = overrides.Fr031ImageBase64;
            doc.Fr032Categories = overrides.Fr032Categories;

            // Fr-03.2: ถ้า Wizard ส่งผลประเมินรายหมวดมา ให้สรุปหมวดที่ "มีแหล่งปล่อย" เก็บไว้ด้วย
            if (overrides.Fr032Categories != null && overrides.Fr032Categories.Count > 0)
            {
                doc.Scope3CheckedCategories = overrides.Fr032Categories
                    .Where(c => c.HasSource)
                    .Select(c => c.CategoryNo)
                    .OrderBy(n => n)
                    .ToList();
            }
        }

        // Multi-Year Progress and Alignment Comparison (M-08)
        Report(44, "Comparing base year and reporting year...");
        if (baseReport != null && baseReport.TotalGrossEmissions_tCO2e > 0)
        {
            var baseGross = baseReport.TotalGrossEmissions_tCO2e;
            var calcGross = calcReport.TotalGrossEmissions_tCO2e;
            var grossDelta = calcGross - baseGross;
            var grossPct = baseGross > 0 ? (grossDelta * 100m / baseGross) : 0m;

            var baseS1 = baseReport.TotalScope1_tCO2e;
            var calcS1 = calcReport.TotalScope1_tCO2e;
            var s1Delta = calcS1 - baseS1;
            var s1Pct = baseS1 > 0 ? (s1Delta * 100m / baseS1) : 0m;

            var baseS2 = baseReport.TotalScope2_tCO2e;
            var calcS2 = calcReport.TotalScope2_tCO2e;
            var s2Delta = calcS2 - baseS2;
            var s2Pct = baseS2 > 0 ? (s2Delta * 100m / baseS2) : 0m;

            var baseS3 = baseReport.TotalScope3_tCO2e;
            var calcS3 = calcReport.TotalScope3_tCO2e;
            var s3Delta = calcS3 - baseS3;
            var s3Pct = baseS3 > 0 ? (s3Delta * 100m / baseS3) : 0m;

            var baseSep = baseReport.TotalSeparate_tCO2e;
            var calcSep = calcReport.TotalSeparate_tCO2e;
            var sepDelta = calcSep - baseSep;
            var sepPct = baseSep > 0 ? (sepDelta * 100m / baseSep) : 0m;

            doc.Comparison = new MultiYearComparisonDto
            {
                Scope1Delta_tCO2e = s1Delta,
                Scope1PercentChange = s1Pct,
                Scope2Delta_tCO2e = s2Delta,
                Scope2PercentChange = s2Pct,
                Scope3Delta_tCO2e = s3Delta,
                Scope3PercentChange = s3Pct,
                GrossDelta_tCO2e = grossDelta,
                GrossPercentChange = grossPct,
                SeparateDelta_tCO2e = sepDelta,
                SeparatePercentChange = sepPct
            };
        }

        // 1. Export & Store Excel AR5 Snapshot File (B-04)
        // [2026-09-18][Claude] ExportToExcel เป็นงาน CPU แบบ synchronous ยาวที่สุดในไปป์ไลน์นี้
        // ถ้ารันบน circuit thread แถบความคืบหน้าจะค้างนิ่งจนกว่าจะเสร็จ — ย้ายออกจาก UI thread (ไม่แตะ DbContext)
        Report(52, "Building AR5 workbook (Fr-01 ... Fr-05)...");
        var excelBytes = await Task.Run(() => _cfoReportService.ExportToExcel(calcReport, baseReport, baseYear, overrides));

        Report(72, "Writing Excel snapshot file...");
        var excelPath = Path.Combine(reportDir, "report_ar5.xlsx");
        await File.WriteAllBytesAsync(excelPath, excelBytes);

        // [2026-09-23][Claude] 1.1 สร้างรายงาน Word จากเทมเพลต อบก. โดยอ่านจาก doc (snapshot) ชุดเดียวกับที่จะบันทึกเป็น JSON
        // Word ล้มเหลวไม่ทำให้ snapshot ล้ม (Excel ใช้ส่ง อบก. ได้ตามเดิม) แต่บันทึกเหตุผลลง ExportVerificationMessages ให้เห็นในหน้า Ledger
        Report(76, "Building CFO Word report...");
        try
        {
            var wordResult = await Task.Run(() => _cfoWordReportService.Generate(doc));
            await File.WriteAllBytesAsync(Path.Combine(reportDir, CfoWordTemplateMap.OutputFileName), wordResult.Bytes);
            doc.HasWordSnapshot = true;
            foreach (var warning in wordResult.Warnings)
            {
                calcReport.ExportVerificationMessages.Add($"[Word] {warning}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building CFO Word report for snapshot {Key}", reportKey);
            doc.HasWordSnapshot = false;
            calcReport.ExportVerificationMessages.Add($"[Word] สร้างรายงาน Word ไม่สำเร็จ: {ex.Message}");
        }

        // 2. Write report_snapshot.json
        Report(85, "Writing snapshot JSON...");
        var snapshotFile = Path.Combine(reportDir, "report_snapshot.json");
        var finalJson = await Task.Run(() => JsonSerializer.Serialize(doc, _jsonOptions));
        await File.WriteAllTextAsync(snapshotFile, finalJson, Encoding.UTF8);

        // 3. Update Index File
        Report(95, "Updating report ledger index...");
        var summaries = await GetReportsAsync(level, targetId);
        var existing = summaries.FirstOrDefault(s => s.ReportKey == reportKey);
        if (existing != null) summaries.Remove(existing);

        summaries.Insert(0, new CfoReportSummaryItem
        {
            ReportKey = doc.ReportKey,
            Title = doc.Title,
            Level = doc.Level,
            TargetId = doc.TargetId,
            OrganizationTitle = doc.OrganizationTitle,
            OrganizationCode = doc.OrganizationCode,
            BaseYear = doc.BaseYear,
            CalculateYear = doc.CalculateYear,
            CreatedAt = doc.CreatedAt,
            CreatedBy = doc.CreatedBy,
            Status = doc.Status,
            TotalScope1_tCO2e = doc.CalculateYearReport.TotalScope1_tCO2e,
            TotalScope2_tCO2e = doc.CalculateYearReport.TotalScope2_tCO2e,
            TotalScope3_tCO2e = doc.CalculateYearReport.TotalScope3_tCO2e,
            TotalGrossEmissions_tCO2e = doc.CalculateYearReport.TotalGrossEmissions_tCO2e,
            HasExcelFile = true,
            HasWordFile = doc.HasWordSnapshot,
            FileDirectory = reportDir
        });

        var indexFile = Path.Combine(baseDir, "index.json");
        await File.WriteAllTextAsync(indexFile, JsonSerializer.Serialize(summaries, _jsonOptions), Encoding.UTF8);

        Report(99, "Finalizing...");
        return doc;
    }

    public async Task<byte[]?> GetExcelBytesAsync(string reportKey, eTopicLevel level, int targetId)
    {
        var baseDir = GetStorageBaseDir(level, targetId);
        var excelPath = Path.Combine(baseDir, reportKey, "report_ar5.xlsx");
        if (File.Exists(excelPath))
        {
            return await File.ReadAllBytesAsync(excelPath);
        }
        return null;
    }

    // [2026-09-23][Claude] ดาวน์โหลดไฟล์ Word ที่สร้างไว้ตอนสร้าง snapshot (รายงานเก่าก่อนมีฟีเจอร์นี้จะไม่มีไฟล์ → คืน null)
    public async Task<byte[]?> GetWordBytesAsync(string reportKey, eTopicLevel level, int targetId)
    {
        var baseDir = GetStorageBaseDir(level, targetId);
        var wordPath = Path.Combine(baseDir, reportKey, CfoWordTemplateMap.OutputFileName);
        if (File.Exists(wordPath))
        {
            return await File.ReadAllBytesAsync(wordPath);
        }
        return null;
    }

    public async Task<bool> DeleteReportAsync(string reportKey, eTopicLevel level, int targetId)
    {
        try
        {
            var baseDir = GetStorageBaseDir(level, targetId);
            var reportDir = Path.Combine(baseDir, reportKey);
            if (Directory.Exists(reportDir))
            {
                Directory.Delete(reportDir, true);
            }

            var indexFile = Path.Combine(baseDir, "index.json");
            var summaries = await GetReportsAsync(level, targetId);
            summaries.RemoveAll(s => s.ReportKey == reportKey);
            await File.WriteAllTextAsync(indexFile, JsonSerializer.Serialize(summaries, _jsonOptions), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting CFO report snapshot {Key}", reportKey);
            return false;
        }
    }
}
