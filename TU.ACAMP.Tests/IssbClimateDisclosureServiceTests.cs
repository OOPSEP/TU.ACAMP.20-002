using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;
using static TU.ACAMP.WebApp.Services.Reports.CfoReportService;

namespace TU.ACAMP.Tests;

/// <summary>[2026-09-24][Claude] ทดสอบการสร้างข้อมูลและไฟล์ Word ของ ISSB Climate Disclosure จาก CFO snapshot</summary>
public class IssbClimateDisclosureServiceTests
{
    private static CfoReportDocument NewSnapshot()
    {
        var calc = new CfoAuditReportDto
        {
            OrganizationTitle = "Demo Co.",
            ConsolidationApproach = "Operational Control",
            TotalScope1_tCO2e = 903.29m,
            TotalScope2_tCO2e = 1945.85m,
            TotalScope3_tCO2e = 619.17m,
            TotalSeparate_tCO2e = 34.05m
        };
        calc.AuditLines.Add(new ActivityAuditLineDto { ScopeNo = eTgoScopeNo.Scope3, ActivityGroupCode = "0303", ActivityGroupTitle = "Fuel- and Energy-Related", KgCO2e = 400_000m });
        calc.AuditLines.Add(new ActivityAuditLineDto { ScopeNo = eTgoScopeNo.Scope3, ActivityGroupCode = "0303", ActivityGroupTitle = "Fuel- and Energy-Related", KgCO2e = 49_830m });
        calc.AuditLines.Add(new ActivityAuditLineDto { ScopeNo = eTgoScopeNo.Scope3, ActivityGroupCode = "0305", ActivityGroupTitle = "Waste", KgCO2e = 90_650m });
        calc.AuditLines.Add(new ActivityAuditLineDto { ScopeNo = eTgoScopeNo.SeparateReport, ActivityGroupCode = "0403", ActivityGroupTitle = "Other Non-Kyoto", KgCO2e = 31_500m });

        return new CfoReportDocument
        {
            ReportKey = "abc123",
            Title = "รายงานการปล่อยก๊าซเรือนกระจก ประจำปี 2025",
            Level = eTopicLevel.Corporate,
            TargetId = 1,
            OrganizationTitle = "Demo Co.",
            CalculateYear = 2025,
            BaseYear = 2023,
            PeriodDescription = "1 ม.ค. 2025 - 31 ธ.ค. 2025",
            CalculateYearReport = calc,
            BaseYearReport = new CfoAuditReportDto { TotalScope1_tCO2e = 800m, TotalScope2_tCO2e = 2000m, TotalScope3_tCO2e = 600m }
        };
    }

    [Fact]
    public void ApplyMetricsFromSnapshot_CopiesTotalsAndGroupsScope3Categories()
    {
        var doc = new IssbClimateDisclosureDocument { Governance = "คงเดิม" };
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(doc, NewSnapshot());

        Assert.Equal(2849.14m, doc.Reporting.Scope1And2);
        Assert.Equal(3468.31m, doc.Reporting.Gross);
        Assert.NotNull(doc.Base);
        Assert.Equal(2, doc.Scope3Categories.Count);
        Assert.Equal(449.83m, doc.Scope3Categories.Single(c => c.Code == "0303").Total_tCO2e);
        Assert.Single(doc.SeparateCategories);
        Assert.Equal("คงเดิม", doc.Governance); // ไม่แตะข้อความของผู้ใช้
    }

    [Fact]
    public void ApplyMetricsFromSnapshot_BaseYearSameAsReportingYear_HasNoBaseAndStripsCodeFromTitle()
    {
        // [2026-09-24][Claude] เคสจากไฟล์จริง: snapshot ปี 2026 ตั้งปีฐาน 2026, สร้างก่อนสิ้นปี, ชื่อองค์กรมีรหัสนำหน้า
        var cfo = NewSnapshot();
        cfo.CalculateYear = 2026;
        cfo.BaseYear = 2026;
        cfo.OrganizationTitle = "D7-CRP-001 — Demo Co.";
        cfo.CreatedAt = new System.DateTimeOffset(2026, 9, 24, 15, 30, 0, System.TimeSpan.FromHours(7));

        var doc = new IssbClimateDisclosureDocument();
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(doc, cfo);

        Assert.Null(doc.Base);
        Assert.Equal(0, doc.BaseYear);
        Assert.True(doc.IsPartialYear);
        Assert.Equal("Demo Co.", doc.OrganizationTitle);
    }

    [Fact]
    public void ApplyMetricsFromSnapshot_TotalsEqualSumOfDisplayedRoundedScopes()
    {
        var cfo = NewSnapshot();
        cfo.CalculateYearReport.TotalScope1_tCO2e = 584.0496m;
        cfo.CalculateYearReport.TotalScope2_tCO2e = 1349.3054m;
        cfo.CalculateYearReport.TotalScope3_tCO2e = 413.5379m;

        var doc = new IssbClimateDisclosureDocument();
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(doc, cfo);

        Assert.Equal(1933.36m, doc.Reporting.Scope1And2); // 584.05 + 1,349.31
        Assert.Equal(2346.90m, doc.Reporting.Gross);      // + 413.54
    }

    [Fact]
    public void PercentChangeAndIntensity_HandleMissingValues()
    {
        Assert.Equal(12.5m, IssbClimateDisclosureService.PercentChange(900m, 800m));
        Assert.Null(IssbClimateDisclosureService.PercentChange(900m, 0m));
        Assert.Null(IssbClimateDisclosureService.PercentChange(900m, null));
        Assert.Equal(2m, IssbClimateDisclosureService.Intensity(100m, 50m));
        Assert.Null(IssbClimateDisclosureService.Intensity(100m, null));
    }

    [Fact]
    public void GenerateWord_ProducesDocxWithFourPillarsAndGuidanceForEmptyNarratives()
    {
        var doc = new IssbClimateDisclosureDocument { Strategy = "ความเสี่ยงจากราคาพลังงาน", RevenueMillionThb = 1000m };
        IssbClimateDisclosureService.ApplyMetricsFromSnapshot(doc, NewSnapshot());

        // GenerateWord ไม่ใช้ DB / storage จึงส่ง null ได้
        var svc = new IssbClimateDisclosureService(null!, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<IssbClimateDisclosureService>.Instance);
        var bytes = svc.GenerateWord(doc);

        using var wd = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var text = wd.MainDocumentPart!.Document.Body!.InnerText;

        Assert.Contains("การกำกับดูแล (Governance)", text);
        Assert.Contains("กลยุทธ์ (Strategy)", text);
        Assert.Contains("การบริหารความเสี่ยง (Risk Management)", text);
        Assert.Contains("ตัวชี้วัดและเป้าหมาย (Metrics and Targets)", text);
        Assert.Contains("ความเสี่ยงจากราคาพลังงาน", text);
        Assert.Contains(IssbClimateDisclosureService.GovernanceGuidance, text);   // Governance ว่าง -> ใส่คำแนะนำ
        Assert.Contains("2,849.14", text);                                          // Scope 1+2
        Assert.Contains("2.8491", text);                                            // intensity 2,849.14 / 1,000
    }
}
