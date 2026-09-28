using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;
using static TU.ACAMP.WebApp.Services.Reports.CfoReportService;

namespace TU.ACAMP.Tests;

/// <summary>[2026-09-24][Claude] ทดสอบ Import / Export Excel ของตาราง Fr-03.2</summary>
public class CfoFr032ExcelServiceTests
{
    private static List<CfoFr032CategoryDto> NewCategories() =>
        Enumerable.Range(1, 15).Select(i => new CfoFr032CategoryDto { CategoryNo = i, CategoryTitle = $"Category {i}" }).ToList();

    [Fact]
    public void ExportThenImport_RoundTripsEditableFields()
    {
        var svc = new CfoFr032ExcelService();
        var source = NewCategories();
        source[0].HasSource = true; source[0].MagnitudePercent = 12.5m;
        source[0].InfluenceScore = 4; source[0].RiskScore = 3; source[0].SectorGuidanceScore = 5; source[0].OutsourcingScore = 2; source[0].EngagementScore = 1;
        source[0].Remark = "ทบทวนแล้ว";
        source[5].HasSource = true; source[5].InfluenceScore = 2;

        var bytes = svc.Export(source, 5m, "Demo");
        var parsed = svc.Parse(new MemoryStream(bytes));
        Assert.True(parsed.IsValid, string.Join(" | ", parsed.Errors));
        Assert.Equal(15, parsed.Rows.Count);

        var target = NewCategories();
        target[0].IsAutoScored = true;
        Assert.Equal(15, svc.Apply(target, parsed));

        Assert.True(target[0].HasSource);
        Assert.Equal(4, target[0].InfluenceScore);
        Assert.Equal(3, target[0].RiskScore);
        Assert.Equal(5, target[0].SectorGuidanceScore);
        Assert.Equal(2, target[0].OutsourcingScore);
        Assert.Equal(1, target[0].EngagementScore);
        Assert.Equal("ทบทวนแล้ว", target[0].Remark);
        Assert.False(target[0].IsAutoScored);
        Assert.Equal(0m, target[0].MagnitudePercent); // Magnitude ไม่ถูกอ่านกลับ
        Assert.Equal(2, target[5].InfluenceScore);
        Assert.Null(target[5].RiskScore);
        Assert.False(target[1].HasSource);
    }

    [Fact]
    public void Import_InvalidScore_RejectsWholeFile()
    {
        var svc = new CfoFr032ExcelService();
        var bytes = svc.Export(NewCategories(), 5m, "Demo");

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(CfoFr032ExcelService.ScoringSheetName);
        ws.Cell(6, 3).Value = "Y";
        ws.Cell(6, 5).Value = 7;          // Influence นอกช่วง 1-5
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var parsed = svc.Parse(ms);
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Contains("Influence"));

        var target = NewCategories();
        Assert.Equal(0, svc.Apply(target, parsed));
        Assert.False(target[0].HasSource);
    }

    [Fact]
    public void Import_HasSourceNo_ClearsScores()
    {
        var svc = new CfoFr032ExcelService();
        var bytes = svc.Export(NewCategories(), 5m, "Demo");

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet(CfoFr032ExcelService.ScoringSheetName);
        ws.Cell(6, 3).Value = "N";
        ws.Cell(6, 5).Value = 3;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var target = NewCategories();
        target[0].HasSource = true;
        target[0].InfluenceScore = 5;
        svc.Apply(target, svc.Parse(ms));

        Assert.False(target[0].HasSource);
        Assert.Null(target[0].InfluenceScore);
    }
}
