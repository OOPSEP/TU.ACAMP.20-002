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

public class EmissionUnitImportTests
{
    private IDbContextFactory<ApplicationDbContext> CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options);
        // [2026-09-19][Antigravity] Mock default emission units
        context.Units.Add(new TbEmissionUnit
        {
            Id = 1,
            Title = "kg",
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.Units.Add(new TbEmissionUnit
        {
            Id = 2,
            Title = "Liter",
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
    [InlineData("kg", "kg")]
    [InlineData(" kg ", "kg")]
    [InlineData("  kg  ", "kg")]
    [InlineData("   kg   ", "kg")]
    [InlineData("    kg    ", "kg")]
    [InlineData(" \t kg \r\n ", "kg")]
    [InlineData("\u00A0kg\u00A0", "kg")]
    [InlineData("  \u00A0kg\u00A0  ", "kg")]
    [InlineData("\u200Bkg\uFEFF", "kg")]
    [InlineData("\u3000kg\u3000", "kg")]
    [InlineData("  kg / day  ", "kg / day")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TrimTitle_RemovesSingleMultipleAndSpecialWhitespaceAtHeadAndTail(string? input, string expected)
    {
        // [2026-09-19][Antigravity] ทดสอบการตัดช่องว่างหัวและท้ายทุกรูปแบบของชื่อ Title
        var actual = EmissionUnitExcelService.TrimTitle(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ParseImportExcelAsync_TrimsTitleLeadingAndTrailingSpaces_AndDetectsDuplicatesAndMatchesExisting()
    {
        // [2026-09-19][Antigravity] จำลองการนำเข้าไฟล์ Excel ที่มี Title ใส่ช่องว่างหัวท้าย 1 ช่อง, 2 ช่อง, 3 ช่อง, ช่องว่างล้วน
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_TrimsTitleLeadingAndTrailingSpaces_AndDetectsDuplicatesAndMatchesExisting));
        var service = new EmissionUnitExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Units");

        // Headers
        ws.Cell(1, 1).Value = "Title";
        ws.Cell(1, 2).Value = "Remark";

        // Row 2: "  kg  " (ช่องว่าง 2 ช่องหัวท้าย) -> ใน DB มี "kg" อยู่แล้ว -> ต้อง trim และพบว่า already exists
        ws.Cell(2, 1).Value = "  kg  ";
        ws.Cell(2, 2).Value = "Kilogram";

        // Row 3: "   kWh   " (ช่องว่าง 3 ช่องหัวท้าย) -> ต้อง trim เป็น "kWh" และ Valid
        ws.Cell(3, 1).Value = "   kWh   ";
        ws.Cell(3, 2).Value = "Kilowatt-hour";

        // Row 4: " kWh " (ช่องว่าง 1 ช่อง) -> ต้อง trim และตรวจพบว่า duplicate ซ้ำกับแถว 3 ในไฟล์เดียวกัน
        ws.Cell(4, 1).Value = " kWh ";
        ws.Cell(4, 2).Value = "Kilowatt-hour duplicated";

        // Row 5: "   " (ช่องว่างล้วน) -> ต้อง trim จนว่าง และแจ้งเตือนว่า Title is required
        ws.Cell(5, 1).Value = "   ";
        ws.Cell(5, 2).Value = "Empty unit";

        // Row 6: "\u00A0MJ\u00A0\u00A0" (NBSP ช่องว่างพิเศษ) -> ต้อง trim เป็น "MJ" และ Valid
        ws.Cell(6, 1).Value = "\u00A0MJ\u00A0\u00A0";
        ws.Cell(6, 2).Value = "Megajoule";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Equal(5, items.Count);

        // Row 2: "  kg  " -> Title = "kg", IsDuplicated = true (Already exists in library)
        Assert.Equal("kg", items[0].Title);
        Assert.True(items[0].IsDuplicated);
        Assert.False(items[0].IsValid);
        Assert.Contains(items[0].ValidationErrors, e => e.Contains("already exists in the library"));

        // Row 3: "   kWh   " -> Title = "kWh", IsValid = true
        Assert.Equal("kWh", items[1].Title);
        Assert.False(items[1].IsDuplicated);
        Assert.True(items[1].IsValid);

        // Row 4: " kWh " -> Title = "kWh", IsDuplicated = true (Duplicated within file)
        Assert.Equal("kWh", items[2].Title);
        Assert.True(items[2].IsDuplicated);
        Assert.False(items[2].IsValid);
        Assert.Contains(items[2].ValidationErrors, e => e.Contains("Duplicated title within this file"));

        // Row 5: "   " -> Title = "", IsValid = false ("Title is required.")
        Assert.Equal(string.Empty, items[3].Title);
        Assert.False(items[3].IsValid);
        Assert.Contains(items[3].ValidationErrors, e => e.Contains("Title is required"));

        // Row 6: "\u00A0MJ\u00A0\u00A0" -> Title = "MJ", IsValid = true
        Assert.Equal("MJ", items[4].Title);
        Assert.False(items[4].IsDuplicated);
        Assert.True(items[4].IsValid);
    }

    [Fact]
    public async Task ParseImportExcelAsync_ParsesMigrationToolExportedFormatSuccessfully()
    {
        // [2026-09-22][Antigravity] ทดสอบไฟล์ Excel ที่ Export มาจาก MigrationTool รูปแบบ (#, Title*, Remark)
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_ParsesMigrationToolExportedFormatSuccessfully));
        var service = new EmissionUnitExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Emission Units");

        // Headers exported by MigrationTool
        ws.Cell(1, 1).Value = "#";
        ws.Cell(1, 2).Value = "Title*";
        ws.Cell(1, 3).Value = "Remark";

        // Sample units exported from V16
        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = "ตลับ";
        ws.Cell(2, 3).Value = "ตลับหมึก";

        ws.Cell(3, 1).Value = 2;
        ws.Cell(3, 2).Value = "กล่อง";
        ws.Cell(3, 3).Value = "กล่องกระดาษ";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms);
        Assert.Equal(2, items.Count);

        Assert.Equal("ตลับ", items[0].Title);
        Assert.Equal("ตลับหมึก", items[0].Remark);
        Assert.True(items[0].IsValid);

        Assert.Equal("กล่อง", items[1].Title);
        Assert.Equal("กล่องกระดาษ", items[1].Remark);
        Assert.True(items[1].IsValid);
    }
}

