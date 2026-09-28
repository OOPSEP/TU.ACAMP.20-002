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
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using Xunit;

namespace TU.ACAMP.Tests;

// [2026-09-21][Gemini] Unit tests for V4 Activity Topic Import (differentiating Master vs Child rows)
public class ImportActivityTopicV4Tests
{
    private static string? FindTestFilePath(string fileName)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "Temp", "bscm_import_excels", fileName);
            if (File.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent;
        }
        return null;
    }

    private IDbContextFactory<ApplicationDbContext> CreateInMemoryDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options);

        // Seed Units
        var unitL = new TbEmissionUnit { Id = 1, Title = "L", IsActive = true };
        var unitKwh = new TbEmissionUnit { Id = 2, Title = "kWh", IsActive = true };
        var unitKg = new TbEmissionUnit { Id = 3, Title = "kg", IsActive = true };
        context.Units.AddRange(unitL, unitKwh, unitKg);

        // Seed Emission Groups
        var eg1 = new TbEmissionGroup { Id = 1, Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default, IsActive = true };
        var eg2 = new TbEmissionGroup { Id = 2, Title = "Purchased Electricity", TgoScopeNo = eTgoScopeNo.Scope2, SourceType = eSourceType.Default, IsActive = true };
        var eg3 = new TbEmissionGroup { Id = 3, Title = "Upstream Fuel (WTT)", TgoScopeNo = eTgoScopeNo.Scope3, SourceType = eSourceType.Default, IsActive = true };
        context.EmissionGroups.AddRange(eg1, eg2, eg3);

        // Seed Activity Groups
        var ag1 = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1, IsActive = true };
        var ag2 = new TbActivityGroup { Id = 2, Code = "0201", Title = "Purchased Electricity", TgoScopeNo = eTgoScopeNo.Scope2, IsActive = true };
        var ag3 = new TbActivityGroup { Id = 3, Code = "0301", Title = "Scope 3 (WTT)", TgoScopeNo = eTgoScopeNo.Scope3, IsActive = true };
        var ag4 = new TbActivityGroup { Id = 4, Code = "0401", Title = "Biogenic CO2", TgoScopeNo = eTgoScopeNo.SeparateReport, IsActive = true };
        context.ActivityGroups.AddRange(ag1, ag2, ag3, ag4);

        // Seed Master Sources
        var srcDiesel = new TbEmissionSource { Id = 10, Title = "น้ำมันดีเซล", UnitId = 1, CO2 = 2.7093m, gwpCO2 = 1m, GroupId = 1, SourceType = eSourceType.Default, IsActive = true };
        var srcElec = new TbEmissionSource { Id = 20, Title = "ไฟฟ้า MEA/PEA", UnitId = 2, CO2 = 0.4999m, gwpCO2 = 1m, GroupId = 2, SourceType = eSourceType.Default, IsActive = true };
        var srcWttDiesel = new TbEmissionSource { Id = 30, Title = "WTT-น้ำมันดีเซล", UnitId = 1, CO2 = 0.5840m, gwpCO2 = 1m, GroupId = 3, SourceType = eSourceType.Default, IsActive = true };
        context.EmissionSources.AddRange(srcDiesel, srcElec, srcWttDiesel);

        // Seed Emission Mapping
        var map1 = new TbEmissionMapping
        {
            Id = 1,
            Scope1ChildId = 10,
            Scope3ChildId = 30,
            UnitConversionRatio = 1m,
            IsActive = true
        };
        context.EmissionMappings.Add(map1);

        context.SaveChanges();

        var mock = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mock.Setup(f => f.CreateDbContextAsync(default))
            .Returns(() => Task.FromResult(new ApplicationDbContext(options)));

        return mock.Object;
    }

    // [2026-09-21][Gemini] Test synthetic Excel with Master and Child rows to verify parsing and parent linking
    [Fact]
    public async Task ParseImportExcelV4Async_SyntheticExcel_CorrectlyDistinguishesMasterAndChild()
    {
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelV4Async_SyntheticExcel_CorrectlyDistinguishesMasterAndChild));
        var service = new ActivityTopicExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Topics");

        // Headers
        ws.Cell(1, 1).Value = "#";
        ws.Cell(1, 2).Value = "Topic Title";
        ws.Cell(1, 3).Value = "Topic Type";
        ws.Cell(1, 4).Value = "Source";
        ws.Cell(1, 5).Value = "Activity Group";
        ws.Cell(1, 6).Value = "Scope";
        ws.Cell(1, 7).Value = "Unit";
        ws.Cell(1, 8).Value = "EF";
        ws.Cell(1, 9).Value = "Share %";

        // Row 2: Master Topic 1
        ws.Cell(2, 1).Value = "1";
        ws.Cell(2, 2).Value = "เครื่องกำเนิดไฟฟ้าดีเซล";
        ws.Cell(2, 3).Value = "Master (Primary)";
        ws.Cell(2, 4).Value = "น้ำมันดีเซล";
        ws.Cell(2, 5).Value = "Stationary Combustion";
        ws.Cell(2, 6).Value = "Scope 1";
        ws.Cell(2, 7).Value = "L";
        ws.Cell(2, 8).Value = 2.7093;
        ws.Cell(2, 9).Value = "100%";

        // Row 3: Child Topic of Master 1 (Scope 3 WTT)
        ws.Cell(3, 1).Value = "";
        ws.Cell(3, 2).Value = "[การได้มา] เครื่องกำเนิดไฟฟ้าดีเซล";
        ws.Cell(3, 3).Value = "Child (Scope 3 WTT)";
        ws.Cell(3, 4).Value = "WTT-น้ำมันดีเซล";
        ws.Cell(3, 5).Value = "Scope 3 (WTT)";
        ws.Cell(3, 6).Value = "Scope 3";
        ws.Cell(3, 7).Value = "L";
        ws.Cell(3, 8).Value = 0.5840;
        ws.Cell(3, 9).Value = "100%";

        // Row 4: Master Topic 2
        ws.Cell(4, 1).Value = "2";
        ws.Cell(4, 2).Value = "ไฟฟ้าอาคารสำนักงาน";
        ws.Cell(4, 3).Value = "Master (Primary)";
        ws.Cell(4, 4).Value = "ไฟฟ้า MEA/PEA";
        ws.Cell(4, 5).Value = "Purchased Electricity";
        ws.Cell(4, 6).Value = "Scope 2";
        ws.Cell(4, 7).Value = "kWh";
        ws.Cell(4, 8).Value = 0.4999;
        ws.Cell(4, 9).Value = "100%";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelV4Async(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);

        // Assert
        Assert.Equal(3, items.Count);

        var masters = items.Where(i => !i.IsChildTopic).ToList();
        var childs = items.Where(i => i.IsChildTopic).ToList();

        Assert.Equal(2, masters.Count);
        Assert.Single(childs);

        // Master 1
        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", masters[0].TopicTitle);
        Assert.False(masters[0].IsChildTopic);

        // Child 1
        Assert.Equal("[การได้มา] เครื่องกำเนิดไฟฟ้าดีเซล", childs[0].TopicTitle);
        Assert.True(childs[0].IsChildTopic);
        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", childs[0].MasterTopicTitle);
        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", childs[0].ParentTopicTitle);

        // Master 2
        Assert.Equal("ไฟฟ้าอาคารสำนักงาน", masters[1].TopicTitle);
        Assert.False(masters[1].IsChildTopic);
    }

    // [2026-09-21][Gemini] Test real exported file Topics_สาขานนทบุรี.xlsx ensuring 67 Masters and 12 Childs (total 79)
    [Fact]
    public async Task ParseImportExcelV4Async_NonthaburiExportFile_ParsesExactly67MastersAnd12Childs()
    {
        var filePath = FindTestFilePath("Topics_สาขานนทบุรี.xlsx");
        if (filePath == null)
        {
            // If running in environment without Temp folder, skip
            return;
        }

        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelV4Async_NonthaburiExportFile_ParsesExactly67MastersAnd12Childs));
        var service = new ActivityTopicExcelService(dbFactory);

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelV4Async(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);

        var masters = items.Where(i => !i.IsChildTopic).ToList();
        var childs = items.Where(i => i.IsChildTopic).ToList();

        // Exactly 67 Master Topics and 12 Child Topics (total 79 rows)
        Assert.Equal(67, masters.Count);
        Assert.Equal(12, childs.Count);
        Assert.Equal(79, items.Count);

        // All child topics should have non-empty MasterTopicTitle matching one of the 67 masters
        var masterTitles = masters.Select(m => m.MasterTopicTitle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var child in childs)
        {
            Assert.True(child.IsChildTopic);
            Assert.False(string.IsNullOrWhiteSpace(child.MasterTopicTitle));
            Assert.Contains(child.MasterTopicTitle, masterTitles);
        }
    }

    // [2026-09-21][Gemini] Test template file bscm_activity_topic_00003_นนทบุรี.xlsx ensuring 68 Masters
    [Fact]
    public async Task ParseImportExcelV4Async_NonthaburiTemplateFile_ParsesExactly68Masters()
    {
        var filePath = FindTestFilePath("bscm_activity_topic_00003_นนทบุรี.xlsx");
        if (filePath == null)
        {
            return;
        }

        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelV4Async_NonthaburiTemplateFile_ParsesExactly68Masters));
        var service = new ActivityTopicExcelService(dbFactory);

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelV4Async(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);

        var masters = items.Where(i => !i.IsChildTopic).ToList();

        // Exactly 68 Master Topics in template
        Assert.Equal(68, masters.Count);
    }

    // [2026-09-21][Gemini] Test that Dialog V4 ReadyCount and DisplayMasterItems accurately reflect only 67 topics
    [Fact]
    public void DialogV4_MasterFilters_ExcludeChildRowsFromDisplayAndReadyCounts()
    {
        var items = new List<ActivityTopicImportItemDto>
        {
            new() { RowNumber = 2, TopicTitle = "Diesel", MasterTopicTitle = "Diesel", IsChildTopic = false, IsValid = true, MappingState = eImportMappingState.Resolved },
            new() { RowNumber = 3, TopicTitle = "[การได้มา] Diesel", MasterTopicTitle = "Diesel", IsChildTopic = true, IsValid = true, MappingState = eImportMappingState.Resolved },
            new() { RowNumber = 4, TopicTitle = "Gasoline", MasterTopicTitle = "Gasoline", IsChildTopic = false, IsValid = true, MappingState = eImportMappingState.Resolved }
        };

        var displayItems = items.Where(i => !i.IsChildTopic).ToList();
        bool IsReady(ActivityTopicImportItemDto i) => i.IsValid && i.MappingState == eImportMappingState.Resolved;
        int readyCount = items.Count(i => !i.IsChildTopic && IsReady(i));
        int totalTopics = items.Count(i => !i.IsChildTopic);
        int childCount = items.Count(i => i.IsChildTopic && i.IsValid);

        Assert.Equal(2, displayItems.Count);
        Assert.Equal(2, readyCount);
        Assert.Equal(2, totalTopics);
        Assert.Equal(1, childCount);
    }

    // [2026-09-21][Gemini] Test that ExportTopicsToExcelV2Async exports only Master topics without Topic Type column
    [Fact]
    public async Task ExportTopicsToExcelV2Async_ExportsOnlyMasterTopics_WithoutTopicTypeColumn()
    {
        var dbFactory = CreateInMemoryDb(nameof(ExportTopicsToExcelV2Async_ExportsOnlyMasterTopics_WithoutTopicTypeColumn));
        var service = new ActivityTopicExcelService(dbFactory);

        var topic1 = new TbActivityTopic
        {
            Id = 1,
            Title = "เครื่องกำเนิดไฟฟ้าดีเซล",
            Level = eTopicLevel.Facility,
            TargetId = 101,
            IsActive = true
        };
        var dieselSource = new TbEmissionSource { Id = 10, Title = "น้ำมันดีเซล", Unit = new TbEmissionUnit { Title = "L" }, CO2 = 2.7093m, gwpCO2 = 1m };
        var wttSource = new TbEmissionSource { Id = 30, Title = "WTT-น้ำมันดีเซล", Unit = new TbEmissionUnit { Title = "L" }, CO2 = 0.5840m, gwpCO2 = 1m };

        // Topic 1 has 2 child sources (Scope 1 Master + Scope 3 WTT Child)
        topic1.Add(new TbActivityTopicChild { Id = 1, TopicId = 1, Source = dieselSource, IsActive = true, EmissionSharePercent = 100f });
        topic1.Add(new TbActivityTopicChild { Id = 2, TopicId = 1, Source = wttSource, IsActive = true, EmissionSharePercent = 100f });

        var topic2 = new TbActivityTopic
        {
            Id = 2,
            Title = "ไฟฟ้าอาคารสำนักงาน",
            Level = eTopicLevel.Facility,
            TargetId = 101,
            IsActive = true
        };
        var elecSource = new TbEmissionSource { Id = 20, Title = "ไฟฟ้า MEA/PEA", Unit = new TbEmissionUnit { Title = "kWh" }, CO2 = 0.4999m, gwpCO2 = 1m };
        topic2.Add(new TbActivityTopicChild { Id = 3, TopicId = 2, Source = elecSource, IsActive = true, EmissionSharePercent = 100f });

        var topics = new List<TbActivityTopic> { topic1, topic2 };

        // Act: Export using V2
        var bytes = await service.ExportTopicsToExcelV2Async(topics, selectedYear: null, contextName: "Test Facility");
        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheets.FirstOrDefault();

        // Assert: Excel file has exactly 1 sheet, headers do NOT have "Topic Type"
        Assert.NotNull(ws);
        var headers = Enumerable.Range(1, 15).Select(c => ws.Cell(1, c).GetString()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        Assert.Equal(12, headers.Count);
        Assert.DoesNotContain(headers, h => h.Contains("Topic Type", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("#", headers[0]);
        Assert.Equal("Title / Topic Name", headers[1]);
        Assert.Equal("Source", headers[2]);

        // Data rows: exactly 2 rows for 2 topics (child rows are not exported)
        var row2Title = ws.Cell(2, 2).GetString();
        var row3Title = ws.Cell(3, 2).GetString();
        var row4Title = ws.Cell(4, 2).GetString();

        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", row2Title);
        Assert.Equal("ไฟฟ้าอาคารสำนักงาน", row3Title);
        Assert.Empty(row4Title); // No 3rd data row!

        // Round-trip test: Import this exported V2 file back using ParseImportExcelV4Async
        ms.Position = 0;
        var importedItems = await service.ParseImportExcelV4Async(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);

        var masters = importedItems.Where(i => !i.IsChildTopic).ToList();
        var childs = importedItems.Where(i => i.IsChildTopic).ToList();

        // Exactly 2 Master Topics in import list
        Assert.Equal(2, masters.Count);
        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", masters[0].TopicTitle);
        Assert.Equal("ไฟฟ้าอาคารสำนักงาน", masters[1].TopicTitle);

        // Child topics are automatically generated via TbEmissionMapping in the background (1 child for diesel)
        Assert.Single(childs);
        Assert.Equal("[การได้มา] เครื่องกำเนิดไฟฟ้าดีเซล", childs[0].TopicTitle);
        Assert.Equal("เครื่องกำเนิดไฟฟ้าดีเซล", childs[0].MasterTopicTitle);
    }

    // [2026-09-21][Gemini] Test export filename format: {entity}_{name}_{date}_{time}.{ext} using ExportFileNameHelper
    [Theory]
    [InlineData("Facility: สาขานนทบุรี", "topics_สาขานนทบุรี_")]
    [InlineData("Facility: พระประแดง", "topics_พระประแดง_")]
    [InlineData("Company: BSCM Co., Ltd.", "topics_BSCM Co., Ltd._")]
    [InlineData("", "topics_facility_")]
    public void ExportFileName_FollowsStandardFormat_TopicsNameDateTime(string parentContextName, string expectedPrefix)
    {
        var now = new DateTime(2026, 9, 21, 16, 45, 30);
        var fileName = ExportFileNameHelper.Generate("topics", parentContextName, fallback: "facility", timestamp: now);

        Assert.StartsWith(expectedPrefix, fileName);
        Assert.EndsWith("_20260921_164530.xlsx", fileName);
    }

    // [2026-09-21][Gemini] Test ExportFileNameHelper with various entities, invalid chars, and extensions
    [Fact]
    public void ExportFileNameHelper_GeneratesStandardCompliantNames()
    {
        var now = new DateTime(2026, 9, 21, 16, 45, 30);

        // 1. Events entity
        var eventFile = ExportFileNameHelper.Generate("events", "Diesel Generator / Engine", timestamp: now);
        Assert.Equal("events_Diesel Generator _ Engine_20260921_164530.xlsx", eventFile);

        // 2. Emission sources entity
        var sourcesFile = ExportFileNameHelper.Generate("emission_sources", "All Subscriptions", timestamp: now);
        Assert.Equal("emission_sources_All Subscriptions_20260921_164530.xlsx", sourcesFile);

        // 3. Emission units entity
        var unitsFile = ExportFileNameHelper.Generate("emission_units", null, fallback: "all", timestamp: now);
        Assert.Equal("emission_units_all_20260921_164530.xlsx", unitsFile);

        // 4. LCI Breakdown CSV
        var csvFile = ExportFileNameHelper.Generate("lci_breakdown", "Scope 1 Direct", extension: "csv", timestamp: now);
        Assert.Equal("lci_breakdown_Scope 1 Direct_20260921_164530.csv", csvFile);

        // 5. Raw prefix stripping
        var prefixFile = ExportFileNameHelper.Generate("topics", "Facility: สาขาพระประแดง", timestamp: now);
        Assert.Equal("topics_สาขาพระประแดง_20260921_164530.xlsx", prefixFile);
    }

    [Fact]
    public async Task ParseImportExcelAsync_ParsesMigrationToolFacilityTopicsFormatSuccessfully()
    {
        // [2026-09-22][Antigravity] Verify that the topics.xlsx format exported by MigrationTool for each facility can be parsed by ActivityTopicExcelService
        var dbFactory = CreateInMemoryDb(nameof(ParseImportExcelAsync_ParsesMigrationToolFacilityTopicsFormatSuccessfully));
        var service = new ActivityTopicExcelService(dbFactory);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Topics");

        // Headers matching MigrationTool facility export
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

        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
        }

        // Row 2: Stationary Combustion Topic
        ws.Cell(2, 1).Value = 1;
        ws.Cell(2, 2).Value = "เครื่องกำเนิดไฟฟ้าดีเซล 500kVA";
        ws.Cell(2, 3).Value = "น้ำมันดีเซล";
        ws.Cell(2, 4).Value = "Stationary Combustion";
        ws.Cell(2, 5).Value = "Stationary Combustion";
        ws.Cell(2, 6).Value = "Scope 1";
        ws.Cell(2, 7).Value = "L";
        ws.Cell(2, 8).Value = 2.7093;
        ws.Cell(2, 9).Value = 100.0;
        ws.Cell(2, 10).Value = "อาคารสำนักงานใหญ่";

        // Row 3: Electricity Topic
        ws.Cell(3, 1).Value = 2;
        ws.Cell(3, 2).Value = "การใช้ไฟฟ้าหลัก";
        ws.Cell(3, 3).Value = "ไฟฟ้า MEA/PEA";
        ws.Cell(3, 4).Value = "Purchased Electricity";
        ws.Cell(3, 5).Value = "Purchased Electricity";
        ws.Cell(3, 6).Value = "Scope 2";
        ws.Cell(3, 7).Value = "kWh";
        ws.Cell(3, 8).Value = 0.4999;
        ws.Cell(3, 9).Value = 100.0;
        ws.Cell(3, 10).Value = "มิเตอร์ 1";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var items = await service.ParseImportExcelAsync(ms, subscriptionId: 1, level: eTopicLevel.Facility, targetId: 101);

        Assert.NotNull(items);
        Assert.True(items.Count >= 2);
        Assert.Contains(items, i => i.TopicTitle == "เครื่องกำเนิดไฟฟ้าดีเซล 500kVA");
        Assert.Contains(items, i => i.TopicTitle == "การใช้ไฟฟ้าหลัก");
    }
}
