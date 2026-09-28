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

public class EmissionGroupImportTests
{
    private IDbContextFactory<ApplicationDbContext> CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options);
        // [2026-09-19][Antigravity] Mock default emission groups
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
            TitleTh = "การซื้อกระแสไฟฟ้า",
            Code = "0201",
            TgoScopeNo = eTgoScopeNo.Scope2,
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.SaveChanges();

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .Returns(() => Task.FromResult(new ApplicationDbContext(options)));

        return mockFactory.Object;
    }

    [Theory]
    [InlineData("Group A", "Group A")]
    [InlineData(" Group A ", "Group A")]
    [InlineData("  Group A  ", "Group A")]
    [InlineData("   Group A   ", "Group A")]
    [InlineData("    Group A    ", "Group A")]
    [InlineData(" \t Group A \r\n ", "Group A")]
    [InlineData("\u00A0Group A\u00A0", "Group A")]
    [InlineData("  \u00A0Group A\u00A0  ", "Group A")]
    [InlineData("\u200BGroup A\uFEFF", "Group A")]
    [InlineData("\u3000Group A\u3000", "Group A")]
    [InlineData("  Group A / Scope 1  ", "Group A / Scope 1")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TrimTitle_RemovesSingleMultipleAndSpecialWhitespaceAtHeadAndTail(string? input, string expected)
    {
        // [2026-09-19][Antigravity] ทดสอบการตัดช่องว่างหัวและท้ายทุกรูปแบบของชื่อ Emission Group Title
        var actual = EmissionGroupExcelService.TrimTitle(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ParseImportExcelAsync_TrimsTitleLeadingAndTrailingSpaces_AndDetectsDuplicatesAndMatchesExisting()
    {
        // [2026-09-19][Antigravity] จำลองการนำเข้าไฟล์ Excel กลุ่มการปล่อย ที่มี Title ใส่ช่องว่างหัวท้าย 1 ช่อง, 2 ช่อง, 3 ช่อง, ช่องว่างล้วน
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_TrimsTitleLeadingAndTrailingSpaces_AndDetectsDuplicatesAndMatchesExisting));
        var service = new EmissionGroupExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Groups");

        // Headers
        ws.Cell(1, 1).Value = "Title";
        ws.Cell(1, 2).Value = "Scope";

        // Row 2: "  Stationary Combustion  " (ช่องว่าง 2 ช่องหัวท้าย) Scope 1 -> ใน DB มี "Stationary Combustion" อยู่แล้ว -> ต้อง trim และพบว่า already exists
        ws.Cell(2, 1).Value = "  Stationary Combustion  ";
        ws.Cell(2, 2).Value = "1";

        // Row 3: "   Industrial Processes   " (ช่องว่าง 3 ช่องหัวท้าย) Scope 1 -> ต้อง trim เป็น "Industrial Processes" และ Valid
        ws.Cell(3, 1).Value = "   Industrial Processes   ";
        ws.Cell(3, 2).Value = "1";

        // Row 4: " Industrial Processes " (ช่องว่าง 1 ช่อง) Scope 1 -> ต้อง trim และตรวจพบว่า duplicate ซ้ำกับแถว 3 ในไฟล์เดียวกัน
        ws.Cell(4, 1).Value = " Industrial Processes ";
        ws.Cell(4, 2).Value = "1";

        // Row 5: "   " (ช่องว่างล้วน) Scope 1 -> ต้อง trim จนว่าง และแจ้งเตือนว่า Title is required
        ws.Cell(5, 1).Value = "   ";
        ws.Cell(5, 2).Value = "1";

        // Row 6: "\u00A0Transportation\u00A0\u00A0" (NBSP ช่องว่างพิเศษ) Scope 1 -> ต้อง trim เป็น "Transportation" และ Valid
        ws.Cell(6, 1).Value = "\u00A0Transportation\u00A0\u00A0";
        ws.Cell(6, 2).Value = "Scope 1";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Equal(5, items.Count);

        // Row 2: "  Stationary Combustion  " -> Title = "Stationary Combustion", IsDuplicated = true (Already exists in library)
        Assert.Equal("Stationary Combustion", items[0].Title);
        Assert.True(items[0].IsDuplicated);
        Assert.False(items[0].IsValid);
        Assert.Contains(items[0].ValidationErrors, e => e.Contains("already exists in the library"));

        // Row 3: "   Industrial Processes   " -> Title = "Industrial Processes", IsValid = true
        Assert.Equal("Industrial Processes", items[1].Title);
        Assert.False(items[1].IsDuplicated);
        Assert.True(items[1].IsValid);

        // Row 4: " Industrial Processes " -> Title = "Industrial Processes", IsDuplicated = true (Duplicated within file)
        Assert.Equal("Industrial Processes", items[2].Title);
        Assert.True(items[2].IsDuplicated);
        Assert.False(items[2].IsValid);
        Assert.Contains(items[2].ValidationErrors, e => e.Contains("Duplicated title within the same scope"));

        // Row 5: "   " -> Title = "", IsValid = false ("Title is required.")
        Assert.Equal(string.Empty, items[3].Title);
        Assert.False(items[3].IsValid);
        Assert.Contains(items[3].ValidationErrors, e => e.Contains("Title is required"));

        // Row 6: "\u00A0Transportation\u00A0\u00A0" -> Title = "Transportation", IsValid = true
        Assert.Equal("Transportation", items[4].Title);
        Assert.False(items[4].IsDuplicated);
        Assert.True(items[4].IsValid);
    }

    [Fact]
    public async Task GenerateTemplateExcelAsync_DoesNotIncludeHintRowOrCodeColumn()
    {
        // [2026-09-20][Antigravity] ตรวจสอบว่าเทมเพลตที่สร้างขึ้นไม่มีแถวคำอธิบาย/หมายเหตุท้ายตาราง และไม่มีคอลัมน์ Code
        var dbFactory = CreateInMemoryDb(nameof(GenerateTemplateExcelAsync_DoesNotIncludeHintRowOrCodeColumn));
        var service = new EmissionGroupExcelService(dbFactory);

        var bytes = await service.GenerateTemplateExcelAsync();
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.FirstOrDefault();
        Assert.NotNull(ws);

        // Header: #, Title*, Scope*
        Assert.Equal("#", ws.Cell(1, 1).GetString().Trim());
        Assert.Equal("Title*", ws.Cell(1, 2).GetString().Trim());
        Assert.Equal("Scope*", ws.Cell(1, 3).GetString().Trim());

        // ตรวจสอบว่ามีข้อมูลเพียง 5 แถว (หัวตาราง 1 แถว + ข้อมูลตัวอย่าง 4 แถว)
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        Assert.Equal(5, lastRow);
    }

    [Fact]
    public async Task ParseImportExcelAsync_SkipsHintAndRemarkRows_NoExtraInvalidRow()
    {
        // [2026-09-20][Antigravity] ตรวจสอบว่าเมื่อไฟล์ Excel มีแถวหมายเหตุ เช่น "หมายเหตุ: รหัสกลุ่ม (Code)..." จะต้องถูกข้าม ไม่ปรากฏเป็น Invalid Row
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_SkipsHintAndRemarkRows_NoExtraInvalidRow));
        var service = new EmissionGroupExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Emission Groups");

        ws.Cell(1, 1).Value = "Title";
        ws.Cell(1, 2).Value = "Scope";

        // Rows 2-5: 4 valid groups
        ws.Cell(2, 1).Value = "BSCM Scope 1";
        ws.Cell(2, 2).Value = "Scope 1";

        ws.Cell(3, 1).Value = "BSCM Scope 2";
        ws.Cell(3, 2).Value = "Scope 2";

        ws.Cell(4, 1).Value = "BSCM Scope 3";
        ws.Cell(4, 2).Value = "Scope 3";

        ws.Cell(5, 1).Value = "BSCM Separate";
        ws.Cell(5, 2).Value = "Separate Report";

        // Row 7: Hint row from legacy template
        ws.Cell(7, 1).Value = "หมายเหตุ: รหัสกลุ่ม (Code) ระบบสร้างให้อัตโนมัติตาม Scope | ช่อง Scope รับค่า 1-4, Scope 1-3, Separate Report หรือ ขอบเขต 1-3";
        ws.Cell(7, 2).Value = "";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);

        // ต้องมีเฉพาะ 4 แถวข้อมูล และทุกแถวต้อง Valid ไม่มีแถวที่ 5 (row 7) ติดเข้ามา
        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.True(i.IsValid));
    }

    [Fact]
    public async Task ParseImportExcelAsync_ParsesMigrationToolExportedFormatSuccessfully()
    {
        // [2026-09-22][Antigravity] Verify that the emission_groups.xlsx format exported by MigrationTool can be parsed by EmissionGroupExcelService
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_ParsesMigrationToolExportedFormatSuccessfully));
        var service = new EmissionGroupExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Emission Groups");

        // Headers matching MigrationTool export: #, Title*, Scope*
        ws.Cell(1, 1).Value = "#";
        ws.Cell(1, 2).Value = "Title*";
        ws.Cell(1, 3).Value = "Scope*";

        // Rows
        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = "Custom Boiler Group";
        ws.Cell(2, 3).Value = "Scope 1";

        ws.Cell(3, 1).Value = 2;
        ws.Cell(3, 2).Value = "Special Purchased Electricity";
        ws.Cell(3, 3).Value = "Scope 2";

        ws.Cell(4, 1).Value = 3;
        ws.Cell(4, 2).Value = "Business Travel Group";
        ws.Cell(4, 3).Value = "Scope 3";

        ws.Cell(5, 1).Value = 4;
        ws.Cell(5, 2).Value = "Biomass Co-product";
        ws.Cell(5, 3).Value = "Separate Report";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);

        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.True(i.IsValid));
        Assert.Equal("Custom Boiler Group", items[0].Title);
        Assert.Equal(eTgoScopeNo.Scope1, items[0].Scope);
        Assert.Equal("Special Purchased Electricity", items[1].Title);
        Assert.Equal(eTgoScopeNo.Scope2, items[1].Scope);
        Assert.Equal("Business Travel Group", items[2].Title);
        Assert.Equal(eTgoScopeNo.Scope3, items[2].Scope);
        Assert.Equal("Biomass Co-product", items[3].Title);
        Assert.Equal(eTgoScopeNo.SeparateReport, items[3].Scope);
    }
}
