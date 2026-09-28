using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TU.ACAMP.Tests;

// [2026-09-23][Claude] ทดสอบการเติมรายงาน Word ลงเทมเพลต อบก. (Files/Template/*.docx) และตัวคำนวณ tCO2e แยกก๊าซ
public class CfoWordReportServiceTests
{
    private const int Scope1SummaryTableIndex = 17;   // ข้อ 5.1 ในเทมเพลตปัจจุบัน
    private const int BaseYearTableIndex = 22;        // ข้อ 6.2
    private const int Scope1MonitoringTableIndex = 13; // ข้อ 4.1

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

    private static string FindWordTemplate()
    {
        var templateDir = Path.Combine(FindWebAppDir(), "Files", "Template");
        var file = Directory.GetFiles(templateDir, "*.docx")
            .FirstOrDefault(f => Path.GetFileName(f).Contains("AR5", StringComparison.OrdinalIgnoreCase)
                                 && !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal));
        Assert.NotNull(file);
        return file!;
    }

    private static CfoWordReportService CreateService()
    {
        // ชี้ ContentRoot / WebRoot ไปที่โปรเจกต์ WebApp เพื่อให้หารูป default (Files/Images, wwwroot/images) เจอ
        var webAppDir = FindWebAppDir();
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.ContentRootPath).Returns(webAppDir);
        env.SetupGet(e => e.WebRootPath).Returns(Path.Combine(webAppDir, "wwwroot"));
        return new CfoWordReportService(env.Object, NullLogger<CfoWordReportService>.Instance);
    }

    private static CfoReportService.ActivityAuditLineDto Line(int id, eTgoScopeNo scope, string title, decimal qty, decimal co2Ef, decimal ch4Ef = 0m)
    {
        // EF รวม = EF รายก๊าซ x GWP (ให้สอดคล้องกันเพื่อไม่ให้เกิด warning)
        var ef = co2Ef * 1m + ch4Ef * 28m;
        return new CfoReportService.ActivityAuditLineDto
        {
            TopicId = id,
            TopicTitle = title,
            ActivityGroupId = (int)scope,
            ActivityGroupCode = $"0{(int)scope}0{id % 10}",
            ScopeNo = scope,
            SourceId = 100 + id,
            SourceName = $"EF-{id}",
            ActivityUnitName = "ลิตร",
            EfUnitName = "ลิตร",
            Quantity = qty,
            EF = ef,
            CO2 = co2Ef,
            CH4 = ch4Ef,
            KgCO2e = qty * ef,
            OriginSource = "TGO EF",
            LocationTitle = "โรงงานทดสอบ A"
        };
    }

    private static CfoReportService.CfoAuditReportDto Report(int year, List<CfoReportService.ActivityAuditLineDto> lines)
    {
        decimal Sum(eTgoScopeNo s) => lines.Where(l => l.ScopeNo == s).Sum(l => l.KgCO2e) / 1000m;
        return new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "บริษัท ทดสอบ จำกัด",
            Year = year,
            AuditLines = lines,
            TotalScope1_tCO2e = Sum(eTgoScopeNo.Scope1),
            TotalScope2_tCO2e = Sum(eTgoScopeNo.Scope2),
            TotalScope3_tCO2e = Sum(eTgoScopeNo.Scope3),
            TotalSeparate_tCO2e = Sum(eTgoScopeNo.SeparateReport)
        };
    }

    private static CfoReportDocument BuildDocument()
    {
        // Scope 1 = 12 รายการ (เทมเพลตมี 10 แถว → ต้องโคลนแถวเพิ่ม), Scope 2 = 1, Scope 3 = 0
        var calcLines = Enumerable.Range(1, 12)
            .Select(i => Line(i, eTgoScopeNo.Scope1, $"แหล่งปล่อย S1-{i:00}", 1000m + i, 2.7m, 0.001m))
            .ToList();
        calcLines.Add(Line(50, eTgoScopeNo.Scope2, "ไฟฟ้า", 50000m, 0.4999m));

        var baseLines = new List<CfoReportService.ActivityAuditLineDto>
        {
            Line(1, eTgoScopeNo.Scope1, "แหล่งปล่อย S1-01", 900m, 2.7m),
            Line(99, eTgoScopeNo.Scope1, "[RETIRED] หม้อไอน้ำเดิม", 500m, 3.1m)
        };

        return new CfoReportDocument
        {
            OrganizationTitle = "บริษัท ทดสอบ จำกัด",
            ContactAddress = "99 ถนนทดสอบ กรุงเทพฯ",
            Level = eTopicLevel.Corporate,
            BaseYear = 2024,
            CalculateYear = 2025,
            PeriodDescription = "1 ม.ค. 2025 - 31 ธ.ค. 2025",
            CreatedAt = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.FromHours(7)),
            CalculateProductionAmount = 200m,
            CalculateProductionUnit = "ตัน",
            CalculateYearReport = Report(2025, calcLines),
            BaseYearReport = Report(2024, baseLines)
        };
    }

    private static List<W.Table> OpenTables(byte[] bytes, out string fullText)
    {
        using var ms = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(ms, false);
        var body = doc.MainDocumentPart!.Document.Body!;
        fullText = string.Concat(body.Descendants<W.Text>().Select(t => t.Text));
        return body.Elements<W.Table>().Select(t => (W.Table)t.CloneNode(true)).ToList();
    }

    private static string CellText(W.TableRow row, int col)
        => string.Concat(row.Elements<W.TableCell>().ElementAt(col).Descendants<W.Text>().Select(t => t.Text));

    [Fact]
    public void GasCalculator_UsesConvertedQuantityTimesEfTimesGwp()
    {
        var line = Line(1, eTgoScopeNo.Scope1, "Diesel", 1000m, 2.7m, 0.001m);
        line.Ratio = 2m;
        line.SharePercent = 50m;

        var g = CfoGasEmissionCalculator.Calculate(line);

        // ปริมาณที่แปลงแล้ว = 1000 x 2 x 50% = 1000
        Assert.Equal(1000m * 2.7m * 1m / 1000m, g.CO2);
        Assert.Equal(1000m * 0.001m * 28m / 1000m, g.CH4);
        Assert.Equal(0m, g.N2O);
    }

    [Fact]
    public void Generate_FillsTemplate_KeepsTotalsEqualToReport()
    {
        var doc = BuildDocument();
        var result = CreateService().GenerateFromTemplate(doc, FindWordTemplate());

        Assert.NotEmpty(result.Bytes);
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("ไม่พบ", StringComparison.Ordinal));

        var tables = OpenTables(result.Bytes, out var fullText);

        // หน้าปก + ข้อ 2
        Assert.Contains("บริษัท ทดสอบ จำกัด", fullText);
        Assert.Contains("99 ถนนทดสอบ กรุงเทพฯ", fullText);

        // [2026-09-23][Claude] บทนำ 3 ย่อหน้า มีชื่อองค์กร และย่อหน้าคำแนะนำของเทมเพลตถูกแทนแล้ว
        Assert.DoesNotContain("อธิบายความเป็นมาและวัตถุประสงค์", fullText);
        Assert.Contains("บริษัท ทดสอบ จำกัด ตระหนักถึงความสำคัญ", fullText);
        Assert.Contains("เพื่อให้บริษัท ทดสอบ จำกัดทราบปริมาณ", fullText);

        // ข้อมูลตัวอย่างของเทมเพลตต้องถูกล้าง
        Assert.DoesNotContain("HCFC-22", fullText);
        Assert.DoesNotContain("xxx", fullText);
        Assert.DoesNotContain("ถ่านหิน", fullText);

        // ข้อ 5.1: หัว 2 แถว + ข้อมูล 12 แถว (โคลนเกินโควตา 10) + แถวรวม 1
        var s1Rows = tables[Scope1SummaryTableIndex].Elements<W.TableRow>().ToList();
        Assert.Equal(2 + 12 + 1, s1Rows.Count);
        var footer = s1Rows.Last();
        Assert.Equal(doc.CalculateYearReport.TotalScope1_tCO2e.ToString("#,##0.0000", CultureInfo.InvariantCulture), CellText(footer, 9));

        // [2026-09-23][Claude] รูป 5 กรอบ (หน้าปก, 3.1.1, 3.1.2, 3.1.3, 7.2) ถูกแทนด้วยรูป default ครบ ไม่เหลือกรอบเปล่า
        Assert.DoesNotContain(result.Warnings, w => w.Contains("กรอบ", StringComparison.Ordinal));
        using (var ms = new MemoryStream(result.Bytes))
        using (var word = WordprocessingDocument.Open(ms, false))
        {
            var body = word.MainDocumentPart!.Document.Body!;
            Assert.Equal(5, body.Descendants<DocumentFormat.OpenXml.Drawing.Pictures.Picture>().Count());
            Assert.Equal(5, word.MainDocumentPart.ImageParts.Count());
            Assert.DoesNotContain(body.Descendants<DocumentFormat.OpenXml.Drawing.GraphicData>(),
                g => g.Uri?.Value == CfoWordTemplateMap.WordprocessingShapeUri);
        }

        // [2026-09-23][Claude] ข้อ 3.2.x: ที่ตั้ง/ตำแหน่ง = หน่วยงานของหัวข้อ, ความสำคัญเทียบ Materiality 5%
        // (Scope 2 ≈ 43% ของการปล่อยรวม = มาก, Scope 1 แต่ละแหล่ง < 5% = น้อย)
        Assert.Contains("โรงงานทดสอบ A", fullText);
        Assert.Contains("มีนัยสำคัญมาก", fullText);
        Assert.Contains("มีนัยสำคัญน้อย", fullText);

        // [2026-09-23][Claude] ข้อ 8 ข้อมูลตัวอย่างของเทมเพลตถูกแทน, ข้อ 6.3 สรุปความแตกต่าง, ข้อ 3.1.5 เทียบหน่วยงาน
        Assert.DoesNotContain("น้ำมันดีเซลรถขนสินค้า", fullText);
        Assert.DoesNotContain("ระบุรายละเอียดเกี่ยวกับความแตกต่าง", fullText);
        Assert.Contains("ปริมาณการปล่อยก๊าซเรือนกระจกรวม (ประเภทที่ 1+2+3) ปีฐาน 2024", fullText);
        Assert.Contains("ไม่มีหน่วยงานที่เพิ่มเข้ามาหรือไม่นับรวม", fullText);

        // หัวกระดาษ "องค์กร" = ชื่อองค์กร
        using (var hs = new MemoryStream(result.Bytes))
        using (var hw = WordprocessingDocument.Open(hs, false))
        {
            var headerText = string.Concat(hw.MainDocumentPart!.HeaderParts
                .SelectMany(h => h.Header.Descendants<W.Text>()).Select(t => t.Text));
            Assert.Contains("บริษัท ทดสอบ จำกัด", headerText);
        }

        // ข้อ 4.1: เครื่องหมายถูกตัวอย่าง (Symbol) ต้องไม่เหลือ
        Assert.Empty(tables[Scope1MonitoringTableIndex].Descendants<W.SymbolChar>());

        // ข้อ 6.2: รายการที่ไม่มีในปีรายงาน / รายการใหม่
        var baseText = string.Concat(tables[BaseYearTableIndex].Descendants<W.Text>().Select(t => t.Text));
        Assert.Contains("ไม่มีรายการนี้ในปีรายงาน 2025", baseText);
        Assert.Contains("เริ่มมีรายการนี้ในปีรายงาน 2025", baseText);
    }

    [Fact]
    public void Generate_WithoutBaseYearAndScope3_DoesNotThrow()
    {
        var doc = BuildDocument();
        doc.BaseYearReport = null;
        doc.BaseYear = 0;
        doc.CalculateProductionAmount = null;

        var result = CreateService().GenerateFromTemplate(doc, FindWordTemplate());

        Assert.NotEmpty(result.Bytes);
        var tables = OpenTables(result.Bytes, out var fullText);
        Assert.DoesNotContain("xxx", fullText);
        Assert.NotEmpty(tables);
    }
}
