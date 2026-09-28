using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using Xunit;

namespace TU.ACAMP.Tests;

public class ImportActivityTopicV3Tests
{
    [Fact]
    public void MasterFilter_ExcludesChildTopics_FromDisplay()
    {
        // Arrange
        var items = new List<ActivityTopicImportItemDto>
        {
            new() { RowNumber = 1, TopicTitle = "Diesel Generator", MasterTopicTitle = "Diesel Generator", IsChildTopic = false, IsValid = true },
            new() { RowNumber = 1, TopicTitle = "[การได้มา] Diesel Generator", MasterTopicTitle = "Diesel Generator", IsChildTopic = true, IsValid = true },
            new() { RowNumber = 1, TopicTitle = "[Biogenic] Diesel Generator", MasterTopicTitle = "Diesel Generator", IsChildTopic = true, IsValid = true },
            new() { RowNumber = 2, TopicTitle = "Electricity Office", MasterTopicTitle = "Electricity Office", IsChildTopic = false, IsValid = true },
            new() { RowNumber = 3, TopicTitle = "LPG Kitchen", MasterTopicTitle = "LPG Kitchen", IsChildTopic = false, IsValid = true },
            new() { RowNumber = 3, TopicTitle = "[การได้มา] LPG Kitchen", MasterTopicTitle = "LPG Kitchen", IsChildTopic = true, IsValid = true }
        };

        // Act - DisplayMasterItems filter used in ImportActivityTopicDialogV3
        var displayItems = items.Where(i => !i.IsChildTopic).ToList();

        // Assert - Exactly 3 master topics displayed in the grid, child topics completely hidden
        Assert.Equal(3, displayItems.Count);
        Assert.DoesNotContain(displayItems, i => i.IsChildTopic);
        Assert.Equal("Diesel Generator", displayItems[0].TopicTitle);
        Assert.Equal("Electricity Office", displayItems[1].TopicTitle);
        Assert.Equal("LPG Kitchen", displayItems[2].TopicTitle);
    }

    [Fact]
    public void ReadyCount_CountsOnlyMasterTopics()
    {
        // Arrange
        var items = new List<ActivityTopicImportItemDto>
        {
            // Master 1: Ready
            new() { TopicTitle = "M1", MasterTopicTitle = "M1", IsChildTopic = false, IsValid = true, MappingState = eImportMappingState.Resolved },
            // Child of M1: Ready
            new() { TopicTitle = "[การได้มา] M1", MasterTopicTitle = "M1", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved },
            new() { TopicTitle = "[Biogenic] M1", MasterTopicTitle = "M1", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved },

            // Master 2: NeedsMapping (not ready)
            new() { TopicTitle = "M2", MasterTopicTitle = "M2", IsChildTopic = false, IsValid = false, MappingState = eImportMappingState.NeedsMapping },

            // Master 3: Ready
            new() { TopicTitle = "M3", MasterTopicTitle = "M3", IsChildTopic = false, IsValid = true, MappingState = eImportMappingState.Resolved }
        };

        // Act - V3 ReadyCount logic
        bool IsReady(ActivityTopicImportItemDto i) => i.IsValid && i.MappingState == eImportMappingState.Resolved;
        int readyCount = items.Count(i => !i.IsChildTopic && IsReady(i));
        int totalTopics = items.Count(i => !i.IsChildTopic);
        int autoMappedChilds = items.Count(i => i.IsChildTopic && i.IsValid);
        int needsMapping = items.Count(i => !i.IsChildTopic && i.MappingState == eImportMappingState.NeedsMapping);

        // Assert
        Assert.Equal(2, readyCount); // Only M1 and M3
        Assert.Equal(3, totalTopics); // M1, M2, M3
        Assert.Equal(2, autoMappedChilds); // 2 childs for M1
        Assert.Equal(1, needsMapping); // M2 needs mapping
    }

    [Fact]
    public void GetChildItemsForMaster_AssociatesCorrectlyByMasterTitle()
    {
        // Arrange
        var items = new List<ActivityTopicImportItemDto>
        {
            new() { TopicTitle = "Diesel", MasterTopicTitle = "Diesel", IsChildTopic = false, IsValid = true },
            new() { TopicTitle = "[การได้มา] Diesel", MasterTopicTitle = "Diesel", IsChildTopic = true, IsValid = true, ActivityGroupTitle = "Scope 3", MatchedSourceName = "Diesel WTT" },
            new() { TopicTitle = "[Biogenic] Diesel", MasterTopicTitle = "Diesel", IsChildTopic = true, IsValid = true, ActivityGroupTitle = "Separate", MatchedSourceName = "Bio CO2" },
            new() { TopicTitle = "Petrol", MasterTopicTitle = "Petrol", IsChildTopic = false, IsValid = true },
            new() { TopicTitle = "[การได้มา] Petrol", MasterTopicTitle = "Petrol", IsChildTopic = true, IsValid = true, ActivityGroupTitle = "Scope 3", MatchedSourceName = "Petrol WTT" }
        };

        var dieselMaster = items.First(i => i.TopicTitle == "Diesel");
        var petrolMaster = items.First(i => i.TopicTitle == "Petrol");

        // Act - V3 GetChildItemsForMaster logic
        List<ActivityTopicImportItemDto> GetChildItems(ActivityTopicImportItemDto master) =>
            items.Where(i => i.IsChildTopic && i.IsValid && string.Equals(i.MasterTopicTitle, master.MasterTopicTitle, StringComparison.OrdinalIgnoreCase)).ToList();

        var dieselChilds = GetChildItems(dieselMaster);
        var petrolChilds = GetChildItems(petrolMaster);

        // Assert
        Assert.Equal(2, dieselChilds.Count);
        Assert.All(dieselChilds, c => Assert.Equal("Diesel", c.MasterTopicTitle));

        Assert.Single(petrolChilds);
        Assert.Equal("Petrol", petrolChilds[0].MasterTopicTitle);
    }

    [Fact]
    public void ImportCollection_OnlyIncludesChildsOfReadyMasters()
    {
        // Arrange
        var items = new List<ActivityTopicImportItemDto>
        {
            // Ready Master A with 2 childs
            new() { TopicTitle = "A", MasterTopicTitle = "A", IsChildTopic = false, IsValid = true, MappingState = eImportMappingState.Resolved, ActivityGroupId = 1, EmissionSourceId = 10 },
            new() { TopicTitle = "[การได้มา] A", MasterTopicTitle = "A", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved, ActivityGroupId = 2, EmissionSourceId = 20 },
            new() { TopicTitle = "[Biogenic] A", MasterTopicTitle = "A", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved, ActivityGroupId = 3, EmissionSourceId = 30 },

            // Unready Master B (NeedsMapping) with 1 child
            new() { TopicTitle = "B", MasterTopicTitle = "B", IsChildTopic = false, IsValid = false, MappingState = eImportMappingState.NeedsMapping },
            new() { TopicTitle = "[การได้มา] B", MasterTopicTitle = "B", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved, ActivityGroupId = 2, EmissionSourceId = 21 }
        };

        bool IsReady(ActivityTopicImportItemDto i) => i.IsValid && i.MappingState == eImportMappingState.Resolved;

        // Act - V3 ExecuteImportAsync item grouping logic
        var readyMasters = items.Where(i => !i.IsChildTopic && IsReady(i)).ToList();
        var importedChildEntries = new List<ActivityTopicImportItemDto>();

        foreach (var master in readyMasters)
        {
            importedChildEntries.Add(master);
            var autoChilds = items.Where(i => i.IsChildTopic && i.IsValid && string.Equals(i.MasterTopicTitle, master.MasterTopicTitle, StringComparison.OrdinalIgnoreCase));
            importedChildEntries.AddRange(autoChilds);
        }

        // Assert
        Assert.Single(readyMasters);
        Assert.Equal("A", readyMasters[0].TopicTitle);

        // Total entities imported for Master A: 1 master + 2 childs = 3
        Assert.Equal(3, importedChildEntries.Count);
        Assert.All(importedChildEntries, entry => Assert.Equal("A", entry.MasterTopicTitle));

        // Master B and its child were completely excluded because Master B is not Ready
        Assert.DoesNotContain(importedChildEntries, entry => entry.MasterTopicTitle == "B");
    }

    // [2026-09-15][Antigravity] ทดสอบว่า GenerateTemplateExcelAsync สร้างคำอธิบายสีเทาและเว้นระยะห่าง 2 แถวถูกต้อง
    [Fact]
    public async Task GenerateTemplateExcelAsync_IncludesGrayDescriptions_SpacedByTwoRows()
    {
        // Arrange
        var service = new ActivityTopicExcelService(null!);

        // Act
        var bytes = await service.GenerateTemplateExcelAsync();
        using var ms = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = workbook.Worksheets.FirstOrDefault();

        // Assert
        Assert.NotNull(ws);
        Assert.Equal("Import Activity Topics", ws.Name);

        // Rows 2..4 are sample rows
        Assert.False(ws.Cell(2, 2).IsEmpty());
        Assert.False(ws.Cell(3, 2).IsEmpty());
        Assert.False(ws.Cell(4, 2).IsEmpty());

        // Rows 5..6 are empty (2 empty rows gap)
        Assert.True(ws.Row(5).IsEmpty());
        Assert.True(ws.Row(6).IsEmpty());

        // Row 7 contains the description in gray
        var cell7 = ws.Cell(7, 1);
        Assert.False(cell7.IsEmpty());
        Assert.StartsWith("คำอธิบาย", cell7.GetString());
        Assert.True(cell7.Style.Font.Italic);
        Assert.Equal(ClosedXML.Excel.XLColor.Gray, cell7.Style.Font.FontColor);

        // Row 8 contains note 1 in gray
        var cell8 = ws.Cell(8, 1);
        Assert.False(cell8.IsEmpty());
        Assert.StartsWith("หมายเหตุ 1", cell8.GetString());
        Assert.True(cell8.Style.Font.Italic);
        Assert.Equal(ClosedXML.Excel.XLColor.Gray, cell8.Style.Font.FontColor);

        // Row 9 contains note 2 in gray
        var cell9 = ws.Cell(9, 1);
        Assert.False(cell9.IsEmpty());
        Assert.StartsWith("หมายเหตุ 2", cell9.GetString());
        Assert.True(cell9.Style.Font.Italic);
        Assert.Equal(ClosedXML.Excel.XLColor.Gray, cell9.Style.Font.FontColor);
    }

    [Theory]
    [InlineData("Topic A", "Topic A")]
    [InlineData(" Topic A ", "Topic A")]
    [InlineData("  Topic A  ", "Topic A")]
    [InlineData("   Topic A   ", "Topic A")]
    [InlineData("    Topic A    ", "Topic A")]
    [InlineData(" \t Topic A \r\n ", "Topic A")]
    [InlineData("\u00A0Topic A\u00A0", "Topic A")]
    [InlineData("  \u00A0Topic A\u00A0  ", "Topic A")]
    [InlineData("\u200BTopic A\uFEFF", "Topic A")]
    [InlineData("\u3000Topic A\u3000", "Topic A")]
    [InlineData("  Topic A / Sub 1  ", "Topic A / Sub 1")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TrimTitle_RemovesSingleMultipleAndSpecialWhitespaceAtHeadAndTail(string? input, string expected)
    {
        // [2026-09-19][Antigravity] ทดสอบการตัดช่องว่างหัวและท้ายทุกรูปแบบของชื่อ Activity Topic Title
        var actual = ActivityTopicExcelService.TrimTitle(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ParseImportExcelAsync_TrimsTopicTitle_AndMatchesExistingInDatabase()
    {
        // [2026-09-19][Antigravity] จำลองการนำเข้าไฟล์ Excel ของ Topic ที่มี Title ใส่ช่องว่างหัวท้ายหลายรูปแบบ
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: nameof(ParseImportExcelAsync_TrimsTopicTitle_AndMatchesExistingInDatabase))
            .Options;

        var context = new ApplicationDbContext(options);
        context.EmissionGroups.Add(new TbEmissionGroup
        {
            Id = 1,
            Title = "Electricity",
            SourceType = eSourceType.Default,
            IsActive = true,
            IsDeleted = false
        });
        context.Topics.Add(new TbActivityTopic
        {
            Id = 10,
            SubscriptionId = 1,
            Level = eTopicLevel.Facility,
            TargetId = 101,
            EmissionGroupId = 1,
            Title = "ไฟฟ้า",
            IsActive = true,
            IsDeleted = false
        });
        context.SaveChanges();

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .Returns(() => Task.FromResult(new ApplicationDbContext(options)));

        var service = new ActivityTopicExcelService(mockFactory.Object);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Topics");

        ws.Cell(1, 1).Value = "Title";
        ws.Cell(1, 2).Value = "Source";
        ws.Cell(1, 3).Value = "Unit";
        ws.Cell(1, 4).Value = "EF";

        // Row 2: "  ไฟฟ้า  " (มีช่องว่าง 2 ช่องหัวท้าย) -> ใน DB มี "ไฟฟ้า" -> ต้อง trim แล้ว match กับ ID 10
        ws.Cell(2, 1).Value = "  ไฟฟ้า  ";
        ws.Cell(2, 2).Value = "ไฟฟ้า";
        ws.Cell(2, 3).Value = "kWh";
        ws.Cell(2, 4).Value = 0.5;

        // Row 3: "   น้ำมันดีเซล   " (ช่องว่าง 3 ช่องหัวท้าย) -> ต้อง trim เป็น "น้ำมันดีเซล"
        ws.Cell(3, 1).Value = "   น้ำมันดีเซล   ";
        ws.Cell(3, 2).Value = "ดีเซล";
        ws.Cell(3, 3).Value = "L";
        ws.Cell(3, 4).Value = 2.7;

        // Row 4: "   " (ช่องว่างล้วน) -> ต้อง trim จนว่าง และ ValidationErrors ต้องมี "Topic Title is required."
        ws.Cell(4, 1).Value = "   ";
        ws.Cell(4, 2).Value = "ดีเซล";
        ws.Cell(4, 3).Value = "L";
        ws.Cell(4, 4).Value = 2.7;

        // Row 5: "\u00A0ก๊าซหุงต้ม\u00A0\u00A0" (NBSP) -> ต้อง trim เป็น "ก๊าซหุงต้ม"
        ws.Cell(5, 1).Value = "\u00A0ก๊าซหุงต้ม\u00A0\u00A0";
        ws.Cell(5, 2).Value = "LPG";
        ws.Cell(5, 3).Value = "kg";
        ws.Cell(5, 4).Value = 3.0;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);
        var masterItems = items.Where(i => !i.IsChildTopic).ToList();
        Assert.Equal(4, masterItems.Count);

        // Row 2: "  ไฟฟ้า  " -> MasterTopicTitle = "ไฟฟ้า", IsExistingMasterMatched = true, MatchedMasterTopicId = 10
        Assert.Equal("ไฟฟ้า", masterItems[0].MasterTopicTitle);
        Assert.True(masterItems[0].IsExistingMasterMatched);
        Assert.Equal(10, masterItems[0].MatchedMasterTopicId);

        // Row 3: "   น้ำมันดีเซล   " -> MasterTopicTitle = "น้ำมันดีเซล"
        Assert.Equal("น้ำมันดีเซล", masterItems[1].MasterTopicTitle);
        Assert.False(masterItems[1].IsExistingMasterMatched);

        // Row 4: "   " -> MasterTopicTitle = "", IsValid = false, ValidationErrors contains "Topic Title is required."
        Assert.Equal(string.Empty, masterItems[2].MasterTopicTitle);
        Assert.False(masterItems[2].IsValid);
        Assert.Contains(masterItems[2].ValidationErrors, e => e.Contains("Topic Title is required"));

        // Row 5: "\u00A0ก๊าซหุงต้ม\u00A0\u00A0" -> MasterTopicTitle = "ก๊าซหุงต้ม"
        Assert.Equal("ก๊าซหุงต้ม", masterItems[3].MasterTopicTitle);
    }

    [Fact]
    public async Task ExportTopicsToExcelAsync_CustomSource_DisplaysRealSourceNameAndEF_NotMasked()
    {
        // [2026-09-19][Antigravity] ทดสอบว่าเมื่อ Export Activity Topic ที่มี Custom Emission Source จะต้องแสดงชื่อ Source จริงและ EF จริง ไม่ mask เป็น [Custom EF - System Internal]
        var service = new ActivityTopicExcelService(null!);

        var topic = new TbActivityTopic
        {
            Id = 1,
            Title = "Solar Rooftop Generation",
            Level = eTopicLevel.Facility,
            TargetId = 101,
            IsActive = true,
            IsDeleted = false
        };

        var customSource = new TbEmissionSource
        {
            Id = 99,
            Title = "Solar Rooftop Clean Energy",
            SourceType = eSourceType.Custom,
            SubscriptionId = 1,
            CO2 = 0.0521m,
            gwpCO2 = 1m,
            Unit = new TbEmissionUnit { Title = "kWh" }
        };

        var child = new TbActivityTopicChild
        {
            Id = 1,
            TopicId = 1,
            Topic = topic,
            Source = customSource,
            IsActive = true,
            IsDeleted = false,
            UnitConversionRatio = 1m,
            EmissionSharePercent = 100f
        };
        topic.Add(child);

        var bytes = await service.ExportTopicsToExcelAsync(new List<TbActivityTopic> { topic }, selectedYear: null, contextName: "Test Facility");
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.FirstOrDefault();

        Assert.NotNull(ws);
        // Column 4 is "Source"
        var exportedSourceName = ws.Cell(2, 4).GetString();
        Assert.Equal("Solar Rooftop Clean Energy", exportedSourceName);
        Assert.DoesNotContain("[Custom EF - System Internal]", exportedSourceName);

        // Column 10 is "EF"
        var exportedEf = ws.Cell(2, 10).GetDouble();
        Assert.Equal(0.0521, exportedEf, 4);
    }
}
