using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Emissions;
using Xunit;

namespace TU.ACAMP.Tests;

public class EmissionSourceImportScopeTests
{
    private IDbContextFactory<ApplicationDbContext> CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options);
        // [2026-09-19][Antigravity] Mock emission groups ครบทุกขอบเขต 1, 2, 3, 4
        context.EmissionGroups.Add(new TbEmissionGroup
        {
            Id = 1,
            Title = "Stationary Combustion",
            TitleTh = "การเผาไหม้อยู่กับที่",
            Code = "0101",
            TgoScopeNo = eTgoScopeNo.Scope1,
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.EmissionGroups.Add(new TbEmissionGroup
        {
            Id = 2,
            Title = "Electricity, grid mix",
            TitleTh = "การซื้อกระแสไฟฟ้า (ไฟฟ้า)",
            Code = "0201",
            TgoScopeNo = eTgoScopeNo.Scope2,
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.EmissionGroups.Add(new TbEmissionGroup
        {
            Id = 3,
            Title = "Purchased Goods and Services",
            TitleTh = "สินค้าและบริการที่ซื้อ",
            Code = "0301",
            TgoScopeNo = eTgoScopeNo.Scope3,
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.EmissionGroups.Add(new TbEmissionGroup
        {
            Id = 4,
            Title = "Biogenic CO2 (Stationary)",
            TitleTh = "กลุ่มสาร Biogenic CO2 สำหรับกรณี Stationary Combustion",
            Code = "0402-BIO",
            TgoScopeNo = eTgoScopeNo.SeparateReport,
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.Units.Add(new TbEmissionUnit
        {
            Id = 1,
            Title = "kWh",
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.Units.Add(new TbEmissionUnit
        {
            Id = 2,
            Title = "kg",
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.SaveChanges();

        var factory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(default)).Returns(() => Task.FromResult(new ApplicationDbContext(options)));
        return factory.Object;
    }

    [Theory]
    // Scope 1
    [InlineData("ขอบเขต 1", eTgoScopeNo.Scope1)]
    [InlineData("1", eTgoScopeNo.Scope1)]
    [InlineData("scope 1", eTgoScopeNo.Scope1)]
    [InlineData("Scope 1", eTgoScopeNo.Scope1)]
    [InlineData("scope1", eTgoScopeNo.Scope1)]
    [InlineData("ขอบเขตที่ 1", eTgoScopeNo.Scope1)]
    [InlineData("1.0", eTgoScopeNo.Scope1)]
    // Scope 2
    [InlineData("ขอบเขต 2", eTgoScopeNo.Scope2)]
    [InlineData("2", eTgoScopeNo.Scope2)]
    [InlineData("scope 2", eTgoScopeNo.Scope2)]
    [InlineData("Scope 2", eTgoScopeNo.Scope2)]
    [InlineData("scope2", eTgoScopeNo.Scope2)]
    [InlineData("ขอบเขตที่ 2", eTgoScopeNo.Scope2)]
    [InlineData("2.0", eTgoScopeNo.Scope2)]
    // Scope 3
    [InlineData("ขอบเขต 3", eTgoScopeNo.Scope3)]
    [InlineData("3", eTgoScopeNo.Scope3)]
    [InlineData("scope 3", eTgoScopeNo.Scope3)]
    [InlineData("Scope 3", eTgoScopeNo.Scope3)]
    [InlineData("scope3", eTgoScopeNo.Scope3)]
    [InlineData("ขอบเขตที่ 3", eTgoScopeNo.Scope3)]
    [InlineData("3.0", eTgoScopeNo.Scope3)]
    // Scope 4 / Separate Report
    [InlineData("ขอบเขต 4", eTgoScopeNo.SeparateReport)]
    [InlineData("4", eTgoScopeNo.SeparateReport)]
    [InlineData("scope 4", eTgoScopeNo.SeparateReport)]
    [InlineData("Scope 4", eTgoScopeNo.SeparateReport)]
    [InlineData("scope4", eTgoScopeNo.SeparateReport)]
    [InlineData("separate", eTgoScopeNo.SeparateReport)]
    [InlineData("biogenic", eTgoScopeNo.SeparateReport)]
    [InlineData("แยกรายงาน", eTgoScopeNo.SeparateReport)]
    [InlineData("ชีวภาพ", eTgoScopeNo.SeparateReport)]
    [InlineData("4.0", eTgoScopeNo.SeparateReport)]
    public void ParseScope_ShouldRecognizeAllScopes(string input, eTgoScopeNo expectedScope)
    {
        // [2026-09-19][Antigravity] ทดสอบ ParseScope รองรับทุกรูปแบบของทุกขอบเขต 1-4
        var scope = EmissionSourceExcelService.ParseScope(input);
        Assert.Equal(expectedScope, scope);
    }

    [Theory]
    [InlineData("ขอบเขต 1", eTgoScopeNo.Scope1, "Stationary Combustion", 1)]
    [InlineData("1", eTgoScopeNo.Scope1, "Stationary Combustion", 1)]
    [InlineData("scope 1", eTgoScopeNo.Scope1, "Stationary Combustion", 1)]
    [InlineData("ขอบเขต 2", eTgoScopeNo.Scope2, "Electricity, grid mix", 2)]
    [InlineData("2", eTgoScopeNo.Scope2, "Electricity, grid mix", 2)]
    [InlineData("scope 2", eTgoScopeNo.Scope2, "Electricity, grid mix", 2)]
    [InlineData("ขอบเขต 3", eTgoScopeNo.Scope3, "Purchased Goods and Services", 3)]
    [InlineData("3", eTgoScopeNo.Scope3, "Purchased Goods and Services", 3)]
    [InlineData("scope 3", eTgoScopeNo.Scope3, "Purchased Goods and Services", 3)]
    [InlineData("ขอบเขต 4", eTgoScopeNo.SeparateReport, "Biogenic CO2 (Stationary)", 4)]
    [InlineData("4", eTgoScopeNo.SeparateReport, "Biogenic CO2 (Stationary)", 4)]
    [InlineData("separate", eTgoScopeNo.SeparateReport, "Biogenic CO2 (Stationary)", 4)]
    public async Task ParseImportExcelAsync_CellScope_ShouldAssignCorrectScope(string scopeCellVal, eTgoScopeNo expectedScope, string groupName, int expectedGroupId)
    {
        // [2026-09-19][Antigravity] ทดสอบการอ่านค่า Scope ในเซลล์ Excel ครบทุกขอบเขต
        var dbFactory = CreateInMemoryDb(Guid.NewGuid().ToString());
        var service = new EmissionSourceExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sources");
        ws.Cell(1, 1).Value = (XLCellValue)"Title";
        ws.Cell(1, 2).Value = (XLCellValue)"Emission";
        ws.Cell(1, 3).Value = (XLCellValue)"Scope";
        ws.Cell(1, 4).Value = (XLCellValue)"Unit";
        ws.Cell(1, 5).Value = (XLCellValue)"EF";

        ws.Cell(2, 1).Value = (XLCellValue)$"Test Item {scopeCellVal}";
        ws.Cell(2, 2).Value = (XLCellValue)groupName;
        ws.Cell(2, 3).Value = (XLCellValue)scopeCellVal;
        ws.Cell(2, 4).Value = (XLCellValue)"kg";
        ws.Cell(2, 5).Value = (XLCellValue)1.234;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Single(items);
        var item = items[0];
        Assert.True(item.IsValid, string.Join("; ", item.ValidationErrors));
        Assert.Equal(expectedScope, item.Scope);
        Assert.Equal(expectedGroupId, item.GroupId);
    }

    [Theory]
    [InlineData("ขอบเขต 1", eTgoScopeNo.Scope1, 1)]
    [InlineData("1", eTgoScopeNo.Scope1, 1)]
    [InlineData("scope 1", eTgoScopeNo.Scope1, 1)]
    [InlineData("ขอบเขต 2", eTgoScopeNo.Scope2, 2)]
    [InlineData("2", eTgoScopeNo.Scope2, 2)]
    [InlineData("scope 2", eTgoScopeNo.Scope2, 2)]
    [InlineData("ขอบเขต 3", eTgoScopeNo.Scope3, 3)]
    [InlineData("3", eTgoScopeNo.Scope3, 3)]
    [InlineData("scope 3", eTgoScopeNo.Scope3, 3)]
    [InlineData("ขอบเขต 4", eTgoScopeNo.SeparateReport, 4)]
    [InlineData("4", eTgoScopeNo.SeparateReport, 4)]
    [InlineData("separate", eTgoScopeNo.SeparateReport, 4)]
    public async Task ParseImportExcelAsync_HeaderScope_ShouldDetectColScopeAndAssignScope(string scopeHeader, eTgoScopeNo expectedScope, int expectedGroupId)
    {
        // [2026-09-19][Antigravity] ทดสอบกรณีหัวคอลัมน์ตั้งชื่อตามขอบเขตต่าง ๆ 1-4
        var dbFactory = CreateInMemoryDb(Guid.NewGuid().ToString());
        var service = new EmissionSourceExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sources");
        ws.Cell(1, 1).Value = (XLCellValue)"Title";
        ws.Cell(1, 2).Value = (XLCellValue)scopeHeader; // Header ระบุ Scope เช่น "ขอบเขต 1", "2", "scope 3", "4"
        ws.Cell(1, 3).Value = (XLCellValue)"Unit";
        ws.Cell(1, 4).Value = (XLCellValue)"EF";

        ws.Cell(2, 1).Value = (XLCellValue)$"Test Item {scopeHeader}";
        ws.Cell(2, 2).Value = (XLCellValue)""; // เซลล์ว่าง แต่อาศัย headerScope
        ws.Cell(2, 3).Value = (XLCellValue)"kg";
        ws.Cell(2, 4).Value = (XLCellValue)0.5;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Single(items);
        var item = items[0];
        Assert.Equal(expectedScope, item.Scope);
        Assert.Equal(expectedGroupId, item.GroupId); // auto-matched to single group in scope
        Assert.True(item.IsValid, string.Join("; ", item.ValidationErrors));
    }

    [Theory]
    [InlineData("ขอบเขต 1", eTgoScopeNo.Scope1, 1)]
    [InlineData("1", eTgoScopeNo.Scope1, 1)]
    [InlineData("scope 1", eTgoScopeNo.Scope1, 1)]
    [InlineData("ขอบเขต 2", eTgoScopeNo.Scope2, 2)]
    [InlineData("2", eTgoScopeNo.Scope2, 2)]
    [InlineData("scope 2", eTgoScopeNo.Scope2, 2)]
    [InlineData("ขอบเขต 3", eTgoScopeNo.Scope3, 3)]
    [InlineData("3", eTgoScopeNo.Scope3, 3)]
    [InlineData("scope 3", eTgoScopeNo.Scope3, 3)]
    [InlineData("ขอบเขต 4", eTgoScopeNo.SeparateReport, 4)]
    [InlineData("4", eTgoScopeNo.SeparateReport, 4)]
    [InlineData("separate", eTgoScopeNo.SeparateReport, 4)]
    public async Task ParseImportExcelAsync_GroupColumnWithScopeText_ShouldResolveScopeAndGroup(string scopeInGroupCol, eTgoScopeNo expectedScope, int expectedGroupId)
    {
        // [2026-09-19][Antigravity] ทดสอบกรณีผู้ใช้ระบุค่า Scope ลงในคอลัมน์ Group/Emission ครบทุกขอบเขต
        var dbFactory = CreateInMemoryDb(Guid.NewGuid().ToString());
        var service = new EmissionSourceExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sources");
        ws.Cell(1, 1).Value = (XLCellValue)"Title";
        ws.Cell(1, 2).Value = (XLCellValue)"Emission";
        ws.Cell(1, 3).Value = (XLCellValue)"Unit";
        ws.Cell(1, 4).Value = (XLCellValue)"EF";

        ws.Cell(2, 1).Value = (XLCellValue)$"Test Item {scopeInGroupCol}";
        ws.Cell(2, 2).Value = (XLCellValue)scopeInGroupCol; // กรอก Scope ในช่อง Group
        ws.Cell(2, 3).Value = (XLCellValue)"kg";
        ws.Cell(2, 4).Value = (XLCellValue)0.5;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Single(items);
        var item = items[0];
        Assert.Equal(expectedScope, item.Scope);
        Assert.Equal(expectedGroupId, item.GroupId);
        Assert.True(item.IsValid, string.Join("; ", item.ValidationErrors));
    }

    // [2026-09-19][Antigravity] ทดสอบว่าคอลัมน์ Title ในแม่แบบนำเข้า Emission Source ไม่กว้างเกินไปตามข้อความหมายเหตุ (Width <= 35)
    [Fact]
    public async Task GenerateTemplateExcelAsync_TitleColumnWidth_IsCompactAndNotInflatedByNote()
    {
        var dbFactory = CreateInMemoryDb(Guid.NewGuid().ToString());
        var service = new EmissionSourceExcelService(dbFactory);

        var bytes = await service.GenerateTemplateExcelAsync();
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);

        var wsTemplate = workbook.Worksheet("Import Emission Sources");
        Assert.NotNull(wsTemplate);

        // คอลัมน์ที่ 2 (Title*) ต้องไม่กว้างเกินไป (ค่าที่ตั้งไว้คือ 32 ไม่ใช่ 200+ จากข้อความ hint)
        Assert.True(wsTemplate.Column(2).Width <= 35, $"Title column width should be <= 35, but was {wsTemplate.Column(2).Width}");
        Assert.True(wsTemplate.Column(2).Width >= 20, $"Title column width should be >= 20, but was {wsTemplate.Column(2).Width}");

        // ตรวจสอบว่าไม่มีคอลัมน์ Total EF ในแม่แบบแล้ว (ระบบคำนวณจากก๊าซ GHG อัตโนมัติ)
        var headerCells = wsTemplate.Row(1).CellsUsed().Select(c => c.GetString().Trim()).ToList();
        Assert.DoesNotContain("Total EF", headerCells, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Reference", headerCells, StringComparer.OrdinalIgnoreCase);

        // ตรวจสอบว่าแผ่นงาน Reference ถูกสร้างครบถ้วน
        var wsRef = workbook.Worksheet("Reference");
        Assert.NotNull(wsRef);

        // ทดสอบนำเข้าไฟล์แม่แบบกลับเข้ามา ต้อง parse ผ่านและคำนวณ EF จากก๊าซ GHG ได้ถูกต้อง
        ms.Position = 0;
        var parsed = await service.ParseImportExcelAsync(ms);
        Assert.Equal(4, parsed.Count);
        // ตัวอย่าง row 1 ก๊าซธรรมชาติ ต้องมี EF > 0 ที่คำนวณได้อัตโนมัติ
        Assert.True(parsed[0].EF > 0, "Parsed EF should be > 0 calculated from individual GHG gases.");
    }

    // [2026-09-19][Antigravity] ทดสอบฟังก์ชัน TrimTitle ในการตัดช่องว่างหัวและท้าย ทั้ง space เดี่ยว (" "), หลายช่องว่าง ("  "), tabs, NBSP, zero-width space
    [Theory]
    [InlineData("ดีเซล", "ดีเซล")]
    [InlineData(" ดีเซล", "ดีเซล")]
    [InlineData("ดีเซล ", "ดีเซล")]
    [InlineData("  ดีเซล  ", "ดีเซล")]
    [InlineData("   ดีเซล   ", "ดีเซล")]
    [InlineData("    น้ำมันดีเซล B7    ", "น้ำมันดีเซล B7")]
    [InlineData("\tน้ำมันดีเซล\t", "น้ำมันดีเซล")]
    [InlineData("\u00A0น้ำมันดีเซล\u00A0", "น้ำมันดีเซล")]
    [InlineData("  \u00A0น้ำมันดีเซล\u00A0  ", "น้ำมันดีเซล")]
    [InlineData("\u200Bน้ำมันดีเซล\u200B", "น้ำมันดีเซล")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TrimTitle_RemovesSingleMultipleAndSpecialWhitespaceAtHeadAndTail(string? input, string expected)
    {
        var result = EmissionSourceExcelService.TrimTitle(input);
        Assert.Equal(expected, result);
    }

    // [2026-09-19][Antigravity] ทดสอบ ParseImportExcelAsync เมื่อมีช่องว่างเดี่ยว สองช่องว่าง หรือหลายช่องว่างที่หัว-ท้ายชื่อ
    [Fact]
    public async Task ParseImportExcelAsync_TrimsTitleLeadingAndTrailingSpaces_AndDetectsDuplicatesAndMatchesExisting()
    {
        var dbName = Guid.NewGuid().ToString();
        var dbFactory = CreateInMemoryDb(dbName);

        // เพิ่ม Emission Source ลงใน DB เพื่อทดสอบ existing matching
        using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.EmissionSources.Add(new TbEmissionSource
            {
                Id = 100,
                Title = "ไฟฟ้า grid mix",
                GroupId = 2,
                UnitId = 1,
                SourceType = eSourceType.Default,
                IsActive = true,
                IsDeleted = false
            });
            await db.SaveChangesAsync();
        }

        var service = new EmissionSourceExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Import");
        ws.Cell(1, 1).Value = "Title";
        ws.Cell(1, 2).Value = "Group";
        ws.Cell(1, 3).Value = "Unit";
        ws.Cell(1, 4).Value = "EF";

        // แถว 2: มีช่องว่าง 2 ช่องหัวและท้าย "  ไฟฟ้า grid mix  " -> ต้อง trim และจับคู่ existing ได้
        ws.Cell(2, 1).Value = "  ไฟฟ้า grid mix  ";
        ws.Cell(2, 2).Value = "Electricity, grid mix";
        ws.Cell(2, 3).Value = "kWh";
        ws.Cell(2, 4).Value = 0.475;

        // แถว 3: มีช่องว่าง 3 ช่องหัวและท้าย "   น้ำมันดีเซล   " -> ต้อง trim เป็น "น้ำมันดีเซล"
        ws.Cell(3, 1).Value = "   น้ำมันดีเซล   ";
        ws.Cell(3, 2).Value = "Stationary Combustion";
        ws.Cell(3, 3).Value = "kg";
        ws.Cell(3, 4).Value = 2.7;

        // แถว 4: ช่องว่าง 1 ช่อง " น้ำมันดีเซล " -> ต้อง trim และตรวจพบว่า duplicate กับแถว 3
        ws.Cell(4, 1).Value = " น้ำมันดีเซล ";
        ws.Cell(4, 2).Value = "Stationary Combustion";
        ws.Cell(4, 3).Value = "kg";
        ws.Cell(4, 4).Value = 2.7;

        // แถว 5: มีเฉพาะช่องว่าง "   " -> ต้องแจ้งเตือนว่า Title is required
        ws.Cell(5, 1).Value = "   ";
        ws.Cell(5, 2).Value = "Stationary Combustion";
        ws.Cell(5, 3).Value = "kg";
        ws.Cell(5, 4).Value = 1.0;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Equal(4, items.Count);

        // Row 2: "  ไฟฟ้า grid mix  " -> Title = "ไฟฟ้า grid mix", IsExistingMatched = true
        Assert.Equal("ไฟฟ้า grid mix", items[0].Title);
        Assert.True(items[0].IsExistingMatched);
        Assert.Equal(100, items[0].MatchedExistingSourceId);
        Assert.True(items[0].IsValid);

        // Row 3: "   น้ำมันดีเซล   " -> Title = "น้ำมันดีเซล", IsValid = true
        Assert.Equal("น้ำมันดีเซล", items[1].Title);
        Assert.False(items[1].IsDuplicated);
        Assert.True(items[1].IsValid);

        // Row 4: " น้ำมันดีเซล " -> Title = "น้ำมันดีเซล", IsDuplicated = true
        Assert.Equal("น้ำมันดีเซล", items[2].Title);
        Assert.True(items[2].IsDuplicated);
        Assert.False(items[2].IsValid);
        Assert.Contains(items[2].ValidationErrors, e => e.Contains("Duplicated title"));

        // Row 5: "   " -> Title = "", IsValid = false ("Title is required.")
        Assert.Equal(string.Empty, items[3].Title);
        Assert.False(items[3].IsValid);
        Assert.Contains(items[3].ValidationErrors, e => e.Contains("Title is required"));
    }
}

