using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>
/// [2026-09-24][Antigravity] Comprehensive end-to-end triangulation and export tests for Excel AR5, Word CFO, and ESG (ISSB IFRS S2) reports.
/// Uses seeded snapshot data (DatabaseSeeder7 Demo Foods Industry) to verify mathematical reconciliation across all three formats.
/// </summary>
public class ReportTriangulationExportTests
{
    private static string FindWebAppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TU.ACAMP.WebApp")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "TU.ACAMP.WebApp");
    }

    private static (CfoReportDocument doc, string snapshotDir) LoadSeededSnapshot()
    {
        var webAppDir = FindWebAppDir();
        var snapshotDir = Path.Combine(webAppDir, "Files", "Reports", "Corporate_1", "54fa59b4b5fe4cf08945a6471fbdcdb1");
        var jsonPath = Path.Combine(snapshotDir, "report_snapshot.json");
        Assert.True(File.Exists(jsonPath), $"Snapshot JSON must exist at: {jsonPath}");

        var json = File.ReadAllText(jsonPath);
        var doc = JsonSerializer.Deserialize<CfoReportDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(doc);
        Assert.NotNull(doc!.CalculateYearReport);
        return (doc, snapshotDir);
    }

    private static (CfoReportService excelService, CfoWordReportService wordService, IssbClimateDisclosureService issbService, CfoFr032ExcelService fr032Service) CreateServices()
    {
        var webAppDir = FindWebAppDir();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.SetupGet(e => e.ContentRootPath).Returns(webAppDir);
        mockEnv.SetupGet(e => e.WebRootPath).Returns(Path.Combine(webAppDir, "wwwroot"));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        var config = new ConfigurationManager();

        var excelService = new CfoReportService(
            mockFactory.Object,
            mockEnv.Object,
            NullLogger<CfoReportService>.Instance,
            config);

        var wordService = new CfoWordReportService(
            mockEnv.Object,
            NullLogger<CfoWordReportService>.Instance,
            config);

        var storageService = new CfoReportStorageService(
            mockEnv.Object,
            excelService,
            wordService,
            NullLogger<CfoReportStorageService>.Instance);

        var issbService = new IssbClimateDisclosureService(
            mockFactory.Object,
            storageService,
            NullLogger<IssbClimateDisclosureService>.Instance);

        var fr032Service = new CfoFr032ExcelService();

        return (excelService, wordService, issbService, fr032Service);
    }

    [Fact]
    public void Test1_ExcelReport_GeneratesValidWorkbookAndFr04Sheets()
    {
        // [2026-09-24][Antigravity] ทดสอบการสร้าง Excel AR5 จาก snapshot และตรวจสอบชีตสำคัญ
        var (doc, _) = LoadSeededSnapshot();
        var (excelService, _, _, _) = CreateServices();

        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            PreparationDate = doc.PreparationDate,
            OrganizationImageBase64 = doc.OrganizationImageBase64,
            CalculatePeriodText = doc.CalculatePeriodText,
            CalculateProductionAmount = doc.CalculateProductionAmount,
            CalculateProductionUnit = doc.CalculateProductionUnit,
            CalcProductItems = doc.CalcProductItems,
            BasePeriodText = doc.BasePeriodText,
            BaseProductionAmount = doc.BaseProductionAmount,
            BaseProductionUnit = doc.BaseProductionUnit,
            BaseProductItems = doc.BaseProductItems,
            OrgInfoMultiLine = doc.OrgInfoMultiLine,
            ContactAddress = doc.ContactAddress,
            RegistrationDate = doc.RegistrationDate,
            GhgTeamName = doc.GhgTeamName,
            CertifierOrgTitle = doc.CertifierOrgTitle,
            Fr02ImageBase64 = doc.Fr02ImageBase64,
            Fr031ImageBase64 = doc.Fr031ImageBase64,
            Fr032Categories = doc.Fr032Categories
        };

        var excelBytes = excelService.ExportToExcel(doc.CalculateYearReport, doc.BaseYearReport, doc.BaseYear, overrides);
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 100_000, "Excel output must be full TGO AR5 workbook (> 100 KB)");

        using var ms = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(ms);

        // ตรวจสอบว่ามีชีตครบตามเกณฑ์ อบก.
        Assert.NotNull(workbook.Worksheet("Fr-01"));
        Assert.NotNull(workbook.Worksheet("Fr-02"));
        Assert.NotNull(workbook.Worksheet("Fr-03.1"));
        Assert.NotNull(workbook.Worksheet("Fr-03.2"));
        Assert.NotNull(workbook.Worksheet("Fr-04.1"));
        Assert.NotNull(workbook.Worksheet("Activity Data Audit Registry"));

        // Fr-04.1 Scope Totals Verification (Dynamic rows)
        var wsCalc = workbook.Worksheet("Fr-04.1");
        int s1Count = Math.Max(14, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope1));
        int s1Start = 10;
        int s1End = s1Start + s1Count - 1;
        int s1TotalRow = s1End + 1;

        int sepCount = Math.Max(5, doc.CalculateYearReport.AuditLines.Count(l => (int)l.ScopeNo == 4));
        int sepStart = s1TotalRow + 1;
        int sepEnd = sepStart + sepCount - 1;
        int sepTotalRow = sepEnd + 1;

        int s2Count = Math.Max(4, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope2));
        int s2Start = sepTotalRow + 1;
        int s2End = s2Start + s2Count - 1;
        int s2TotalRow = s2End + 1;

        int s3Count = Math.Max(7, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope3));
        int s3Start = s2TotalRow + 1;
        int s3End = s3Start + s3Count - 1;
        int s3TotalRow = s3End + 1;

        decimal excelS1 = 0m, excelS2 = 0m, excelS3 = 0m, excelSep = 0m;
        for (int r = s1Start; r <= s1End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS1 += v;
        for (int r = sepStart; r <= sepEnd; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelSep += v;
        for (int r = s2Start; r <= s2End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS2 += v;
        for (int r = s3Start; r <= s3End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS3 += v;

        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalScope1_tCO2e, 2), Math.Round(excelS1, 2));
        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalScope2_tCO2e, 2), Math.Round(excelS2, 2));
        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalScope3_tCO2e, 2), Math.Round(excelS3, 2));
        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalSeparate_tCO2e, 2), Math.Round(excelSep, 2));

        // [2026-09-25][Gemini] ยืนยันว่าไม่มีแถวใดถูกยุบรวมเป็น "[รวม N รายการที่เกินโควตา] อื่นๆ / Consolidated Remaining"
        foreach (var cell in wsCalc.CellsUsed())
        {
            var text = cell.GetString();
            Assert.DoesNotContain("เกินโควตา", text);
            Assert.DoesNotContain("Consolidated Remaining", text);
        }

        // [2026-09-25][Antigravity] ตรวจสอบว่า Fr-03.2 มีการประเมินนัยสำคัญ (คะแนน E/F/H, หมายเหตุ O, และตารางเกณฑ์ A36) ครบถ้วน
        var wsFr032 = workbook.Worksheet("Fr-03.2");
        Assert.True(wsFr032.Cell("E19").TryGetValue(out double infVal) && infVal > 0, "Fr-03.2 Col E (Influence) must be populated");
        Assert.True(wsFr032.Cell("F19").TryGetValue(out double riskVal) && riskVal > 0, "Fr-03.2 Col F (Risk) must be populated");
        Assert.True(wsFr032.Cell("H19").TryGetValue(out double secVal) && secVal > 0, "Fr-03.2 Col H (Sector) must be populated");
        Assert.False(string.IsNullOrWhiteSpace(wsFr032.Cell("O19").GetString()), "Fr-03.2 Col O (Remark) must be populated");
        Assert.False(string.IsNullOrWhiteSpace(wsFr032.Cell("A36").GetString()), "Fr-03.2 Rubric description matrix must be populated at row 36");

        // [2026-09-25][Antigravity] ยืนยันว่าแม้ export โดยส่ง overrides = null ระบบก็ยังประเมิน Fr-03.2 ให้อัตโนมัติ ไม่ปล่อยว่าง
        var excelBytesNull = excelService.ExportToExcel(doc.CalculateYearReport, doc.BaseYearReport, doc.BaseYear, null);
        using var msNull = new MemoryStream(excelBytesNull);
        using var wbNull = new XLWorkbook(msNull);
        var wsFr032Null = wbNull.Worksheet("Fr-03.2");
        Assert.True(wsFr032Null.Cell("E19").TryGetValue(out double infNull) && infNull > 0, "Fr-03.2 Col E must be auto-evaluated when overrides is null");
        Assert.False(string.IsNullOrWhiteSpace(wsFr032Null.Cell("O19").GetString()), "Fr-03.2 Remark must be auto-evaluated when overrides is null");
        Assert.False(string.IsNullOrWhiteSpace(wsFr032Null.Cell("A36").GetString()), "Fr-03.2 Rubric matrix must be written when overrides is null");
    }

    [Fact]
    public void Test2_CfoWordReport_GeneratesValidDocxWithExpectedTables()
    {
        // [2026-09-24][Antigravity] ทดสอบการสร้าง Word CFO จาก snapshot และตรวจสอบโครงสร้างตาราง
        var (doc, _) = LoadSeededSnapshot();
        var (_, wordService, _, _) = CreateServices();

        var wordResult = wordService.Generate(doc);
        Assert.NotNull(wordResult.Bytes);
        Assert.True(wordResult.Bytes.Length > 200_000, "CFO Word output must be valid document (> 200 KB)");

        using var ms = new MemoryStream(wordResult.Bytes);
        using var wd = WordprocessingDocument.Open(ms, false);
        var body = wd.MainDocumentPart?.Document?.Body;
        Assert.NotNull(body);

        var tables = body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().ToList();
        Assert.True(tables.Count >= 20, $"Word template must have multiple tables (found {tables.Count})");
    }

    [Fact]
    public void Test3_IssbClimateDisclosure_GeneratesValidWordDocument()
    {
        // [2026-09-24][Antigravity] ทดสอบการสร้าง ESG Word Report (ISSB IFRS S2)
        var (doc, _) = LoadSeededSnapshot();
        var (_, _, issbService, _) = CreateServices();

        var issbDoc = new IssbClimateDisclosureDocument
        {
            Governance = "คณะกรรมการบริษัทมีนโยบายควบคุมการปล่อยก๊าซเรือนกระจกอย่างเคร่งครัด",
            Strategy = "มุ่งเน้นการเพิ่มประสิทธิภาพการใช้พลังงานและติดตั้งโซลาร์รูฟท็อป",
            RiskManagement = "ประเมินความเสี่ยงด้านการเปลี่ยนแปลงสภาพภูมิอากาศประจำปี",
            RevenueMillionThb = 1500m
        };

        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(issbDoc, doc);

        // ตรวจสอบการคัดลอกค่าจาก snapshot สู่ ISSB Document
        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalScope1_tCO2e, 2), Math.Round(issbDoc.Reporting.Scope1, 2));
        Assert.Equal(Math.Round(doc.CalculateYearReport.TotalScope2_tCO2e, 2), Math.Round(issbDoc.Reporting.Scope2, 2));
        // [2026-09-24][Antigravity] ISSB Gross เป็นผลบวกของค่าที่ปัดเศษแล้ว (584.05 + 1349.31 + 413.54 = 2346.90) จึงต่างจาก unrounded snapshot ไม่เกิน 0.01 tCO2e
        Assert.Equal(issbDoc.Reporting.Scope1 + issbDoc.Reporting.Scope2 + issbDoc.Reporting.Scope3, issbDoc.Reporting.Gross);
        Assert.True(Math.Abs(doc.CalculateYearReport.TotalGrossEmissions_tCO2e - issbDoc.Reporting.Gross) <= 0.01m);

        var esgBytes = issbService.GenerateWord(issbDoc);
        Assert.NotNull(esgBytes);
        Assert.True(esgBytes.Length > 2000, "ESG Word document must have valid size");

        using var ms = new MemoryStream(esgBytes);
        using var wd = WordprocessingDocument.Open(ms, false);
        var text = wd.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;

        Assert.Contains("IFRS S2", text);
        Assert.Contains("Governance", text);
        Assert.Contains("Strategy", text);
        Assert.Contains("Risk Management", text);
        Assert.Contains("Metrics and Targets", text);
    }

    [Fact]
    public void Test4_TriangulationReconciliation_AllThreeFormats_ReconcileWithZeroDiscrepancy()
    {
        // [2026-09-24][Antigravity] สอบทานความสอดคล้องข้าม 3 รูปแบบรายงาน (Excel, Word, ESG) ต้องตรงกัน 100%
        var (doc, _) = LoadSeededSnapshot();
        var (excelService, wordService, issbService, _) = CreateServices();

        // 1. Snapshot Totals
        var snapS1 = Math.Round(doc.CalculateYearReport.TotalScope1_tCO2e, 2);
        var snapS2 = Math.Round(doc.CalculateYearReport.TotalScope2_tCO2e, 2);
        var snapS3 = Math.Round(doc.CalculateYearReport.TotalScope3_tCO2e, 2);
        var snapGross = Math.Round(doc.CalculateYearReport.TotalGrossEmissions_tCO2e, 2);
        var snapSep = Math.Round(doc.CalculateYearReport.TotalSeparate_tCO2e, 2);

        // 2. Excel Totals
        var excelBytes = excelService.ExportToExcel(doc.CalculateYearReport, doc.BaseYearReport, doc.BaseYear, null);
        using var msExcel = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(msExcel);
        var wsCalc = workbook.Worksheet("Fr-04.1");
        int s1Count = Math.Max(14, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope1));
        int s1Start = 10;
        int s1End = s1Start + s1Count - 1;
        int s1TotalRow = s1End + 1;

        int sepCount = Math.Max(5, doc.CalculateYearReport.AuditLines.Count(l => (int)l.ScopeNo == 4));
        int sepStart = s1TotalRow + 1;
        int sepEnd = sepStart + sepCount - 1;
        int sepTotalRow = sepEnd + 1;

        int s2Count = Math.Max(4, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope2));
        int s2Start = sepTotalRow + 1;
        int s2End = s2Start + s2Count - 1;
        int s2TotalRow = s2End + 1;

        int s3Count = Math.Max(7, doc.CalculateYearReport.AuditLines.Count(l => l.ScopeNo == eTgoScopeNo.Scope3));
        int s3Start = s2TotalRow + 1;
        int s3End = s3Start + s3Count - 1;
        int s3TotalRow = s3End + 1;

        decimal excelS1 = 0m, excelS2 = 0m, excelS3 = 0m, excelSep = 0m;
        for (int r = s1Start; r <= s1End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS1 += v;
        for (int r = sepStart; r <= sepEnd; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelSep += v;
        for (int r = s2Start; r <= s2End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS2 += v;
        for (int r = s3Start; r <= s3End; r++) if (wsCalc.Cell($"AO{r}").TryGetValue(out decimal v)) excelS3 += v;
        var excelGross = excelS1 + excelS2 + excelS3;

        // 3. ESG Totals
        var issbDoc = new IssbClimateDisclosureDocument();
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(issbDoc, doc);
        var esgS1 = Math.Round(issbDoc.Reporting.Scope1, 2);
        var esgS2 = Math.Round(issbDoc.Reporting.Scope2, 2);
        var esgS3 = Math.Round(issbDoc.Reporting.Scope3, 2);
        var esgGross = Math.Round(issbDoc.Reporting.Gross, 2);
        var esgSep = Math.Round(issbDoc.Reporting.Separate, 2);

        // 4. Assert Triangulation (Diff <= 0.01 tCO2e across all 3 reports)
        Assert.Equal(snapS1, Math.Round(excelS1, 2));
        Assert.Equal(snapS1, esgS1);

        Assert.Equal(snapS2, Math.Round(excelS2, 2));
        Assert.Equal(snapS2, esgS2);

        Assert.Equal(snapS3, Math.Round(excelS3, 2));
        Assert.Equal(snapS3, esgS3);

        Assert.Equal(snapSep, Math.Round(excelSep, 2));
        Assert.Equal(snapSep, esgSep);

        Assert.Equal(snapGross, Math.Round(excelGross, 2));
        // [2026-09-24][Antigravity] esgGross รวมจากค่าที่ปัดเศษ 2 ตำแหน่ง จึงยอมรับ diff <= 0.01 tCO2e
        Assert.True(Math.Abs(snapGross - esgGross) <= 0.01m, $"snapGross ({snapGross}) and esgGross ({esgGross}) must differ by <= 0.01");
        Assert.True(Math.Abs(Math.Round(excelGross, 2) - esgGross) <= 0.01m, $"excelGross ({Math.Round(excelGross, 2)}) and esgGross ({esgGross}) must differ by <= 0.01");
    }

    [Fact]
    public void Test5_ExportAllReportsToPhysicalFiles_WithStandardFileNaming()
    {
        // [2026-09-24][Antigravity] ส่งออกไฟล์จริงทั้ง Excel, Word, ESG ลงโฟลเดอร์สำหรับตรวจสอบทางกายภาพ
        var (doc, _) = LoadSeededSnapshot();
        var (excelService, wordService, issbService, fr032Service) = CreateServices();

        // [2026-09-25][Antigravity] กำหนดให้ผลการทดสอบไปใช้ D:\temp\output ห้ามใส่ใน project หลัก
        var outputDir = GetTestOutputDir();

        var orgTitle = doc.OrganizationTitle;

        // 1. Export Excel AR5
        var excelName = ExportFileNameHelper.Generate("topics_cfo_ar5", orgTitle, "xlsx");
        var excelBytes = excelService.ExportToExcel(doc.CalculateYearReport, doc.BaseYearReport, doc.BaseYear, CreateOverrides(doc));
        var excelPath = Path.Combine(outputDir, excelName);
        File.WriteAllBytes(excelPath, excelBytes);
        Assert.True(File.Exists(excelPath) && new FileInfo(excelPath).Length > 0);

        // 2. Export Word CFO
        var wordName = ExportFileNameHelper.Generate("cfo_report", orgTitle, "docx");
        var wordResult = wordService.Generate(doc);
        var wordPath = Path.Combine(outputDir, wordName);
        File.WriteAllBytes(wordPath, wordResult.Bytes);
        Assert.True(File.Exists(wordPath) && new FileInfo(wordPath).Length > 0);

        // 3. Export ESG Word (ISSB IFRS S2)
        var esgName = ExportFileNameHelper.Generate("issb_climate_disclosure", orgTitle, "docx");
        var issbDoc = new IssbClimateDisclosureDocument
        {
            Governance = "คณะกรรมการบริษัทกำกับดูแลการเปิดเผยข้อมูลสภาพภูมิอากาศ",
            Strategy = "กลยุทธ์ Decarbonization มุ่งสู่ความเป็นกลางทางคาร์บอน",
            RiskManagement = "ระบบบริหารความเสี่ยงแบบองค์รวม Enterprise Risk Management",
            RevenueMillionThb = 1850m
        };
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(issbDoc, doc);
        var esgBytes = issbService.GenerateWord(issbDoc);
        var esgPath = Path.Combine(outputDir, esgName);
        File.WriteAllBytes(esgPath, esgBytes);
        Assert.True(File.Exists(esgPath) && new FileInfo(esgPath).Length > 0);

        // 4. Export Fr-03.2 Standalone Excel
        var fr032Name = ExportFileNameHelper.Generate("topics_fr032", orgTitle, "xlsx");
        var fr032Categories = doc.Fr032Categories ?? new();
        var fr032Bytes = fr032Service.Export(fr032Categories, 5.0m, orgTitle);
        var fr032Path = Path.Combine(outputDir, fr032Name);
        File.WriteAllBytes(fr032Path, fr032Bytes);
        Assert.True(File.Exists(fr032Path) && new FileInfo(fr032Path).Length > 0);

        // Round-trip verification on Fr-03.2
        using var importStream = new MemoryStream(fr032Bytes);
        var importResult = fr032Service.Parse(importStream);
        Assert.True(importResult.IsValid, "Fr-03.2 round-trip import must be valid");
    }

    [Fact]
    public void Test6_ScenarioA_FullYear2025_Vs_BaseYear2023_Reconciliation()
    {
        // [2026-09-24][Antigravity] ทดสอบ Scenario A: ปีรายงาน 2025 (12 เดือนเต็ม) เทียบปีฐาน 2023 (12 เดือนเต็ม)
        var (templateDoc, _) = LoadSeededSnapshot();
        var (excelService, wordService, issbService, _) = CreateServices();

        // โคลนเอกสารจำลองสำหรับปี 2025 vs 2023
        var doc2025 = new CfoReportDocument
        {
            ReportKey = "test_scenario_a_2025_2023",
            Title = "GHG Emissions Report Year 2025",
            Level = templateDoc.Level,
            TargetId = templateDoc.TargetId,
            OrganizationTitle = templateDoc.OrganizationTitle,
            OrganizationCode = templateDoc.OrganizationCode,
            CalculateYear = 2025,
            BaseYear = 2023,
            CalculatePeriodText = "1 Jan 2025 - 31 Dec 2025",
            BasePeriodText = "1 Jan 2023 - 31 Dec 2023",
            GhgTeamName = templateDoc.GhgTeamName,
            CertifierOrgTitle = templateDoc.CertifierOrgTitle,
            ContactAddress = templateDoc.ContactAddress,
            RegistrationDate = DateTime.Now,
            CreatedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(7)),
            Scope3CheckedCategories = templateDoc.Scope3CheckedCategories,
            Fr032Categories = templateDoc.Fr032Categories,
            CalculateYearReport = new CfoReportService.CfoAuditReportDto
            {
                OrganizationTitle = templateDoc.OrganizationTitle,
                OrganizationCode = templateDoc.OrganizationCode,
                Year = 2025,
                TotalScope1_tCO2e = 903.29m,
                TotalScope2_tCO2e = 1945.85m,
                TotalScope3_tCO2e = 610.21m,
                TotalSeparate_tCO2e = 54.98m,
                AuditLines = templateDoc.CalculateYearReport.AuditLines
            },
            BaseYearReport = new CfoReportService.CfoAuditReportDto
            {
                OrganizationTitle = templateDoc.OrganizationTitle,
                OrganizationCode = templateDoc.OrganizationCode,
                Year = 2023,
                TotalScope1_tCO2e = 850.00m,
                TotalScope2_tCO2e = 1800.00m,
                TotalScope3_tCO2e = 550.00m,
                TotalSeparate_tCO2e = 40.00m,
                AuditLines = templateDoc.CalculateYearReport.AuditLines
            },
            Comparison = new MultiYearComparisonDto
            {
                Scope1Delta_tCO2e = 53.29m,
                Scope1PercentChange = 6.27m,
                Scope2Delta_tCO2e = 145.85m,
                Scope2PercentChange = 8.10m,
                Scope3Delta_tCO2e = 60.21m,
                Scope3PercentChange = 10.95m,
                GrossDelta_tCO2e = 259.35m,
                GrossPercentChange = 8.10m,
                SeparateDelta_tCO2e = 14.98m,
                SeparatePercentChange = 37.45m
            }
        };

        // 1. Export Excel (Fr-04.1 ปี 2025, Fr-04.2 ปีฐาน 2023, Fr-05)
        var excelBytes = excelService.ExportToExcel(doc2025.CalculateYearReport, doc2025.BaseYearReport, 2023, CreateOverrides(doc2025));
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 100_000);

        using var msExcel = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(msExcel);
        Assert.NotNull(workbook.Worksheet("Fr-04.1"));
        Assert.NotNull(workbook.Worksheet("Fr-04.2"));
        Assert.NotNull(workbook.Worksheet("Fr-05"));

        // 2. Export Word CFO (ข้อ 5 สรุป 2025, ข้อ 6.2 ขอบเขตปีฐาน 2023, ข้อ 6.3 สรุปผลต่าง)
        var wordResult = wordService.Generate(doc2025);
        Assert.NotNull(wordResult.Bytes);
        Assert.True(wordResult.Bytes.Length > 200_000);

        using var msWord = new MemoryStream(wordResult.Bytes);
        using var wd = WordprocessingDocument.Open(msWord, false);
        var body = wd.MainDocumentPart?.Document?.Body;
        Assert.NotNull(body);
        var text = body!.InnerText;
        Assert.Contains("2023", text);

        // 3. Export ESG Word (IFRS S2 Metrics เปรียบเทียบปี 2025 กับ 2023)
        var issbDoc = new IssbClimateDisclosureDocument
        {
            Governance = "คณะกรรมการบริษัทกำกับดูแลการเปิดเผยข้อมูลสภาพภูมิอากาศ",
            Strategy = "กลยุทธ์ Decarbonization มุ่งสู่ความเป็นกลางทางคาร์บอน",
            RiskManagement = "ระบบบริหารความเสี่ยงแบบองค์รวม Enterprise Risk Management",
            RevenueMillionThb = 2100m,
            BaseRevenueMillionThb = 1950m
        };
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(issbDoc, doc2025);
        Assert.NotNull(issbDoc.Base);
        Assert.Equal(2023, issbDoc.BaseYear);
        Assert.Equal(2025, issbDoc.ReportingYear);
        Assert.False(issbDoc.IsPartialYear);

        var esgBytes = issbService.GenerateWord(issbDoc);
        Assert.NotNull(esgBytes);
        Assert.True(esgBytes.Length > 2000);

        // 4. [2026-09-25][Antigravity] บันทึกไฟล์ Scenario A ลง D:\temp\output ห้ามใส่ใน project หลัก
        var outputDir = GetTestOutputDir();

        var excelPath = Path.Combine(outputDir, ExportFileNameHelper.Generate("topics_cfo_ar5_scenario_a_2025", doc2025.OrganizationTitle, "xlsx"));
        var wordPath = Path.Combine(outputDir, ExportFileNameHelper.Generate("cfo_report_scenario_a_2025", doc2025.OrganizationTitle, "docx"));
        var esgPath = Path.Combine(outputDir, ExportFileNameHelper.Generate("issb_climate_disclosure_scenario_a_2025", doc2025.OrganizationTitle, "docx"));

        File.WriteAllBytes(excelPath, excelBytes);
        File.WriteAllBytes(wordPath, wordResult.Bytes);
        File.WriteAllBytes(esgPath, esgBytes);

        Assert.True(File.Exists(excelPath) && File.Exists(wordPath) && File.Exists(esgPath));
    }

    /// <summary>
    /// [2026-09-25][Antigravity] กำหนดให้ผลการทดสอบหรือไฟล์ชั่วคราวไปใช้ path D:\temp\output ห้ามใส่ใน project หลัก
    /// </summary>
    private static string GetTestOutputDir()
    {
        const string preferredDir = @"D:\temp\output";
        string outputDir;
        try
        {
            if (!Directory.Exists(preferredDir))
            {
                Directory.CreateDirectory(preferredDir);
            }
            outputDir = preferredDir;
        }
        catch
        {
            outputDir = Path.Combine(Path.GetTempPath(), "output");
            Directory.CreateDirectory(outputDir);
        }
        return outputDir;
    }

    private static CfoReportService.CfoReportOverridesDto CreateOverrides(CfoReportDocument doc)
    {
        return new CfoReportService.CfoReportOverridesDto
        {
            PreparationDate = doc.PreparationDate,
            OrganizationImageBase64 = doc.OrganizationImageBase64,
            CalculatePeriodText = doc.CalculatePeriodText,
            CalculateProductionAmount = doc.CalculateProductionAmount,
            CalculateProductionUnit = doc.CalculateProductionUnit,
            CalcProductItems = doc.CalcProductItems,
            BasePeriodText = doc.BasePeriodText,
            BaseProductionAmount = doc.BaseProductionAmount,
            BaseProductionUnit = doc.BaseProductionUnit,
            BaseProductItems = doc.BaseProductItems,
            OrgInfoMultiLine = doc.OrgInfoMultiLine,
            ContactAddress = doc.ContactAddress,
            RegistrationDate = doc.RegistrationDate,
            GhgTeamName = doc.GhgTeamName,
            CertifierOrgTitle = doc.CertifierOrgTitle,
            Fr02ImageBase64 = doc.Fr02ImageBase64,
            Fr031ImageBase64 = doc.Fr031ImageBase64,
            Fr032Categories = doc.Fr032Categories
        };
    }

    // [2026-09-25][Gemini] ตรวจสอบว่าหากไม่มีการส่งวันที่มา (เนื่องจากนำ UI วันที่ออกจาก Widget) ระบบจะ fallback ไปใช้วันที่ปัจจุบันตาม AppTimeZone.LocalNow.Date เสมอ
    [Fact]
    public void Test7_DefaultDates_FallbackToCurrentDate_WhenNotSpecified()
    {
        var (doc, _) = LoadSeededSnapshot();
        var (excelService, _, _, _) = CreateServices();

        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            PreparationDate = null,
            RegistrationDate = null,
            Fr02DiagramDate = null,
            Fr031DiagramDate = null,
            Fr031CompletionDate = null
        };

        var bytes = excelService.ExportToExcel(doc.CalculateYearReport, doc.BaseYearReport, doc.BaseYear, overrides);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        var wsFr01 = wb.Worksheet("Fr-01");
        var wsFr02 = wb.Worksheet("Fr-02");
        var wsFr031 = wb.Worksheet("Fr-03.1");

        var today = AppTimeZone.LocalNow.Date;
        Assert.Equal(today, wsFr01.Cell("J4").GetDateTime().Date);
        Assert.Equal(today, wsFr02.Cell("M4").GetDateTime().Date);
        Assert.Equal(today, wsFr031.Cell("M4").GetDateTime().Date);
        Assert.Equal(today, wsFr031.Cell("K26").GetDateTime().Date);
    }
}
