using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using Xunit;

namespace TU.ACAMP.Tests;

public class ImportActivityTopicV2Tests
{
    private (IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider authProvider) CreateInMemoryContextFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        var mockAuthProvider = new Mock<AuthenticationStateProvider>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Email, "auditor@acamp.com"),
            new Claim(ClaimTypes.Role, "Administrator")
        }, "TestAuth"));

        mockAuthProvider.Setup(a => a.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(user));

        return (mockFactory.Object, mockAuthProvider.Object);
    }

    private MemoryStream CreateTestExcelStream(params (string title, string source, string unit, string ef)[] rows)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Topics");
        ws.Cell(1, 1).Value = "Topic Title";
        ws.Cell(1, 2).Value = "Emission Source";
        ws.Cell(1, 3).Value = "Unit";
        ws.Cell(1, 4).Value = "EF";

        for (int i = 0; i < rows.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = rows[i].title;
            ws.Cell(i + 2, 2).Value = rows[i].source;
            ws.Cell(i + 2, 3).Value = rows[i].unit;
            ws.Cell(i + 2, 4).Value = rows[i].ef;
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Parse_SourceUnknown_SetsNeedsMapping()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        // Seed Unknown Source (Id = -1)
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var unknownSource = new TbEmissionSource
            {
                Id = -1,
                Title = "Unknown",
                GroupId = 1,
                UnitId = 1,
                CO2 = 0m,
                SourceType = eSourceType.Default
            };
            context.EmissionSources.Add(unknownSource);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Boiler Diesel", "Unknown", "liter", "2.7"),
            ("Generator Fuel", "", "liter", "2.7"),
            ("Vehicle Gas", "-1", "liter", "2.7")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Equal(3, result.Count);
        Assert.All(result, item =>
        {
            Assert.Equal(eImportMappingState.NeedsMapping, item.MappingState);
        });
    }

    [Fact]
    public async Task Parse_SourceMatched_StaysResolved()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 10, Title = "liter", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 20, Title = "Fuel Group", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default };
            var source = new TbEmissionSource
            {
                Id = 30,
                Title = "Diesel Oil",
                GroupId = 20,
                UnitId = 10,
                CO2 = 2.7m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);
            context.EmissionSources.Add(source);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Boiler Fuel", "Diesel Oil", "liter", "2.7")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.Resolved, item.MappingState);
        Assert.True(item.MatchedEF > 0);
        Assert.Equal(30, item.EmissionSourceId);
    }

    [Fact]
    public async Task ApplyManualSource_SetsEmissionGroupFromSource()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var excelService = new ActivityTopicExcelService(dbFactory);

        var item = new ActivityTopicImportItemDto
        {
            TopicTitle = "Test Topic",
            ExcelEF = 1.5m,
            ExcelUnit = "kg",
            MappingState = eImportMappingState.NeedsMapping
        };

        var source = new TbEmissionSource
        {
            Id = 101,
            Title = "Manual Source",
            GroupId = 77,
            Group = new TbEmissionGroup { Id = 77, Title = "Source Group Title", TgoScopeNo = eTgoScopeNo.Scope2 },
            UnitId = 5,
            Unit = new TbEmissionUnit { Id = 5, Title = "kg" },
            CO2 = 1.5m
        };

        var activityGroup = new TbActivityGroup
        {
            Id = 33,
            Code = "0301",
            Title = "Scope 3 Activity Group",
            TgoScopeNo = eTgoScopeNo.Scope3
        };

        excelService.ApplyManualSource(item, source, activityGroup);

        Assert.Equal(77, item.EmissionGroupId);
        Assert.Equal("Source Group Title", item.EmissionGroupTitle);
        Assert.Equal(eTgoScopeNo.Scope3, item.Scope); // derived from activityGroup
        Assert.Equal(33, item.ActivityGroupId);
    }

    [Fact]
    public async Task ApplyManualSource_RecalculatesConversionRatio()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var excelService = new ActivityTopicExcelService(dbFactory);

        var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Act", TgoScopeNo = eTgoScopeNo.Scope1 };

        // Case 1: ExcelEF = 2, source.EF = 1 -> ratio = 2, MatchedEF = 2
        var item1 = new ActivityTopicImportItemDto
        {
            ExcelEF = 2m,
            ExcelUnit = "liter"
        };
        var source1 = new TbEmissionSource
        {
            Id = 1,
            Title = "Src1",
            GroupId = 1,
            UnitId = 1,
            CO2 = 1m,
            gwpCO2 = 1m
        };
        excelService.ApplyManualSource(item1, source1, actGroup);
        Assert.Equal(2m, item1.UnitConversionRatio);
        Assert.Equal(2m, item1.MatchedEF);

        // Case 2: ExcelEF = 0, source.EF = 1.5 -> ratio = 1, MatchedEF = 1.5
        var item2 = new ActivityTopicImportItemDto
        {
            ExcelEF = 0m,
            ExcelUnit = "liter"
        };
        var source2 = new TbEmissionSource
        {
            Id = 2,
            Title = "Src2",
            GroupId = 1,
            UnitId = 1,
            CO2 = 1.5m,
            gwpCO2 = 1m
        };
        excelService.ApplyManualSource(item2, source2, actGroup);
        Assert.Equal(1m, item2.UnitConversionRatio);
        Assert.Equal(1.5m, item2.MatchedEF);
    }

    [Fact]
    public async Task ApplyManualSource_FlipsStateToResolved()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var excelService = new ActivityTopicExcelService(dbFactory);

        var item = new ActivityTopicImportItemDto
        {
            TopicTitle = "Needs Map Item",
            ExcelEF = 1m,
            ExcelUnit = "kg",
            MappingState = eImportMappingState.NeedsMapping,
            IsValid = false,
            ValidationErrors = new() { "Source not resolved" }
        };

        var source = new TbEmissionSource
        {
            Id = 10,
            Title = "Resolved Source",
            GroupId = 2,
            UnitId = 1,
            CO2 = 1m
        };
        var actGroup = new TbActivityGroup { Id = 5, Code = "0101", Title = "General", TgoScopeNo = eTgoScopeNo.Scope1 };

        excelService.ApplyManualSource(item, source, actGroup);

        Assert.Equal(eImportMappingState.Resolved, item.MappingState);
        Assert.True(item.IsManuallyMapped);
        Assert.True(item.IsValid);
        Assert.Empty(item.ValidationErrors);
    }

    [Fact]
    public async Task Parse_ShortGenericSourceName_DoesNotAutoMatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 10, Title = "kg", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 20, Title = "Biogenic Group", TgoScopeNo = eTgoScopeNo.SeparateReport, SourceType = eSourceType.Default };
            var source = new TbEmissionSource
            {
                Id = 21,
                Title = "Diesel B7: Report in Biogenic CO2 emissions",
                GroupId = 20,
                UnitId = 10,
                CO2 = 0.115078m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);
            context.EmissionSources.Add(source);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("CO2 จากการเติมน้ำยาถังดับเพลิง", "CO2", "", "0")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.NeedsMapping, item.MappingState);
        Assert.NotEqual(21, item.EmissionSourceId);
    }

    [Fact]
    public async Task Parse_AmbiguousSubstring_FallsBackToNeedsMapping()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 1, Title = "liter", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 1, Title = "Fuel", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default };

            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);

            context.EmissionSources.Add(new TbEmissionSource
            {
                Id = 101,
                Title = "Premium Gasoline 95",
                GroupId = 1,
                UnitId = 1,
                CO2 = 2.1m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            });
            context.EmissionSources.Add(new TbEmissionSource
            {
                Id = 102,
                Title = "Regular Gasoline 91",
                GroupId = 1,
                UnitId = 1,
                CO2 = 2.0m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            });
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Vehicle Fuel", "Gasoline", "", "0")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.NeedsMapping, item.MappingState);
    }

    [Fact]
    public async Task Parse_ExactSourceName_StillResolves()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 1, Title = "liter", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 1, Title = "Fuel", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default };
            var source = new TbEmissionSource
            {
                Id = 55,
                Title = "Diesel B7",
                GroupId = 1,
                UnitId = 1,
                CO2 = 2.7m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);
            context.EmissionSources.Add(source);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Backup Generator", "Diesel B7", "liter", "2.7")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.Resolved, item.MappingState);
        Assert.Equal(55, item.EmissionSourceId);
    }

    [Fact]
    public async Task Parse_EfWithinPlausibleRange_KeepsConversionRatio()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 1, Title = "kg", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 1, Title = "Gas", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default };
            var source = new TbEmissionSource
            {
                Id = 60,
                Title = "Natural Gas",
                GroupId = 1,
                UnitId = 1,
                CO2 = 1m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);
            context.EmissionSources.Add(source);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Gas Turbine", "Natural Gas", "kg", "2")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.Resolved, item.MappingState);
        Assert.Equal(2m, item.UnitConversionRatio);
        Assert.Equal(2m, item.MatchedEF);
    }

    [Fact]
    public async Task Parse_EfOutOfRange_FlagsNeedsMapping()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            var unit = new TbEmissionUnit { Id = 1, Title = "liter", SourceType = eSourceType.Default };
            var group = new TbEmissionGroup { Id = 1, Title = "Fuel", TgoScopeNo = eTgoScopeNo.Scope1, SourceType = eSourceType.Default };
            var source1 = new TbEmissionSource
            {
                Id = 71,
                Title = "Test Fuel OutOfRange",
                GroupId = 1,
                UnitId = 1,
                CO2 = 1m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            var source2 = new TbEmissionSource
            {
                Id = 72,
                Title = "Test Fuel InRange",
                GroupId = 1,
                UnitId = 1,
                CO2 = 0.115078m,
                gwpCO2 = 1m,
                SourceType = eSourceType.Default
            };
            context.ActivityGroups.Add(actGroup);
            context.Units.Add(unit);
            context.EmissionGroups.Add(group);
            context.EmissionSources.Add(source1);
            context.EmissionSources.Add(source2);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Row 1 OutOfRange", "Test Fuel OutOfRange", "liter", "100"),
            ("Row 2 InRange", "Test Fuel InRange", "liter", "1")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Equal(2, result.Count);

        // Row 1: EF 100 vs source EF 1 -> ratio 100 > 10 -> NeedsMapping, UnitConversionRatio = 1
        var item1 = result[0];
        Assert.Equal(eImportMappingState.NeedsMapping, item1.MappingState);
        Assert.Equal(1m, item1.UnitConversionRatio);

        // Row 2: EF 1 vs source EF 0.115078 -> ratio 8.69 <= 10 -> Resolved, UnitConversionRatio = 1 / 0.115078
        var item2 = result[1];
        Assert.Equal(eImportMappingState.Resolved, item2.MappingState);
        Assert.Equal(1m / 0.115078m, item2.UnitConversionRatio);
    }

    [Fact]
    public async Task Parse_UnresolvedSource_IsNeedsMappingNotInvalid()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, _) = CreateInMemoryContextFactory(dbName);
        var excelService = new ActivityTopicExcelService(dbFactory);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1 };
            context.ActivityGroups.Add(actGroup);
            await context.SaveChangesAsync();
        }

        using var stream = CreateTestExcelStream(
            ("Unresolved Topic", "CompletelyNonExistentSource999", "", "0")
        );

        var result = await excelService.ParseImportExcelAsync(stream);

        Assert.Single(result);
        var item = result.First();
        Assert.Equal(eImportMappingState.NeedsMapping, item.MappingState);
        Assert.True(item.IsValid);
    }
}

