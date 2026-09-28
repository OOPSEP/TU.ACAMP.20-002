using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Emissions;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;

namespace TU.ACAMP.Tests;

public class Phase4IsoComplianceTests
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

    #region 1. [GHG-07] IPCC GWP Metric Version & CO2 Validation Tests
    [Fact]
    public void GHG07_Enum_eGwpVersion_ValuesAreValid()
    {
        Assert.Equal(4, (int)eGwpVersion.AR4);
        Assert.Equal(5, (int)eGwpVersion.AR5);
        Assert.Equal(6, (int)eGwpVersion.AR6);
    }

    [Fact]
    public void GHG07_TbEmissionSource_DefaultGwpVersion_IsAR5()
    {
        var source = new TbEmissionSource();
        Assert.Equal(eGwpVersion.AR5, source.GwpVersion);
    }

    [Fact]
    public void GHG07_TbEmissionSource_Duplicate_PreservesGwpVersion()
    {
        var source = new TbEmissionSource
        {
            Title = "Solar Power Source",
            GwpVersion = eGwpVersion.AR6,
            gwpCO2 = 1.0m,
            gwpCH4 = 27.2m,
            gwpN2O = 273m
        };

        var duplicated = TbEmissionSource.Duplicate(source, 1);
        Assert.Equal(eGwpVersion.AR6, duplicated.GwpVersion);
        Assert.Equal(1.0m, duplicated.gwpCO2);
        Assert.Equal(27.2m, duplicated.gwpCH4);
        Assert.Equal(273m, duplicated.gwpN2O);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GHG07_EmissionSourceService_CreateAsync_gwpCO2ZeroOrNegative_ThrowsInvalidOperationException(decimal invalidGwpCO2)
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var service = new EmissionSourceService(dbFactory, authProvider, NullLogger<EmissionSourceService>.Instance);

        var source = new TbEmissionSource
        {
            SubscriptionId = 1,
            Title = "Invalid GWP CO2 Source",
            GroupId = 1,
            UnitId = 1,
            gwpCO2 = invalidGwpCO2
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(source));
        Assert.Contains("gwpCO2", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GHG07_EmissionSourceService_UpdateAsync_gwpCO2ZeroOrNegative_ThrowsInvalidOperationException(decimal invalidGwpCO2)
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var context = await dbFactory.CreateDbContextAsync();

        var existing = new TbEmissionSource
        {
            SubscriptionId = 1,
            Title = "Valid Source",
            SourceType = eSourceType.Custom,
            GroupId = 1,
            UnitId = 1,
            gwpCO2 = 1m,
            IsActive = true
        };
        context.EmissionSources.Add(existing);
        await context.SaveChangesAsync();

        var service = new EmissionSourceService(dbFactory, authProvider, NullLogger<EmissionSourceService>.Instance);

        existing.gwpCO2 = invalidGwpCO2;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(existing));
        Assert.Contains("gwpCO2", ex.Message);
    }

    [Fact]
    public async Task GHG07_CfoReportService_NonAR5Gwp_GeneratesPreFlightWarningAndExportGuardWarning()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var context = await dbFactory.CreateDbContextAsync();

        // 1. Seed Company & Corporate
        var corp = new TbCorporate { Id = 1, SubscriptionId = 1, Title = "Test Corp", Code = "CORP01", IsActive = true };
        context.Corporates.Add(corp);

        // 2. Seed Units
        var unitL = new TbEmissionUnit { Id = 1, Title = "L", IsActive = true };
        context.Units.Add(unitL);

        // 3. Seed Emission Group & Source with AR6 GWP Version
        var group = new TbEmissionGroup { Id = 1, TgoScopeNo = eTgoScopeNo.Scope1, Title = "Stationary", Code = "S1", IsActive = true };
        context.EmissionGroups.Add(group);

        var actGroup = new TbActivityGroup { Id = 1, Code = "0101", Title = "Stationary Combustion", TgoScopeNo = eTgoScopeNo.Scope1, IsActive = true };
        context.ActivityGroups.Add(actGroup);

        var sourceAr6 = new TbEmissionSource
        {
            Id = 1,
            Title = "Diesel AR6",
            GroupId = 1,
            UnitId = 1,
            GwpVersion = eGwpVersion.AR6,
            CO2 = 2.7m,
            gwpCO2 = 1m,
            gwpCH4 = 27.2m,
            gwpN2O = 273m,
            IsActive = true
        };
        context.EmissionSources.Add(sourceAr6);

        // 4. Seed Topic & Child & Event
        var child = new TbActivityTopicChild
        {
            Id = 1,
            TopicId = 1,
            ActivityGroupId = 1,
            SourceId = 1,
            Source = sourceAr6,
            ActivityUnitId = 1,
            ActivityUnit = unitL,
            EmissionSharePercent = 100f,
            UnitConversionRatio = 1m,
            IsActive = true
        };

        var ev = new TbActivityEvent
        {
            Id = 1,
            SubscriptionId = 1,
            TopicId = 1,
            Quantity = 1000m,
            TimeStamp = new DateTime(2026, 6, 1),
            IsActive = true
        };

        var topic = new TbActivityTopic
        {
            Id = 1,
            SubscriptionId = 1,
            Level = eTopicLevel.Corporate,
            TargetId = 1,
            Title = "Generator Diesel",
            EmissionGroupId = 1,
            IsActive = true
        };
        topic.Add(child);
        topic.Add(ev);
        context.Topics.Add(topic);
        context.TopicChilds.Add(child);
        context.Events.Add(ev);
        await context.SaveChangesAsync();

        // 5. Generate Report
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppContext.BaseDirectory);
        mockEnv.Setup(e => e.WebRootPath).Returns(AppContext.BaseDirectory);

        var reportService = new CfoReportService(dbFactory, mockEnv.Object, NullLogger<CfoReportService>.Instance);
        var report = await reportService.GenerateReportAsync(eTopicLevel.Corporate, 1, 2026, null);

        // Assert GHG-07 pre-flight warning
        Assert.Contains(report.PreFlightWarnings, w => w.Contains("[GHG-07 GWP Warning]") && w.Contains("AR6"));
        Assert.Equal(eGwpVersion.AR6, report.GwpVersion);

        // Assert ExportToExcel guard warning
        reportService.ExportToExcel(report);
        Assert.Contains(report.ExportVerificationMessages, m => m.Contains("[GHG-07 GWP Guard Warning]"));
    }
    #endregion

    #region 2. [GHG-10] Consolidation Approach Tests
    [Fact]
    public void GHG10_ConsolidationApproach_DefaultIsOperationalControl()
    {
        var report = new CfoReportService.CfoAuditReportDto();
        Assert.Equal("Operational Control", report.ConsolidationApproach);
    }
    #endregion

    #region 3. [GHG-13] Separate Activity Unit vs EF Unit Tests
    [Fact]
    public void GHG13_TbActivityTopicChild_HasActivityUnitNavigation()
    {
        var unit = new TbEmissionUnit { Id = 5, Title = "kWh" };
        var child = new TbActivityTopicChild
        {
            ActivityUnitId = 5,
            ActivityUnit = unit
        };

        Assert.Equal(5, child.ActivityUnitId);
        Assert.NotNull(child.ActivityUnit);
        Assert.Equal("kWh", child.ActivityUnit.Title);
    }

    [Fact]
    public async Task GHG13_CfoReportService_ActivityUnitMismatchWithRatio1_GeneratesWarning()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var context = await dbFactory.CreateDbContextAsync();

        var corp = new TbCorporate { Id = 2, SubscriptionId = 1, Title = "Test Corp 2", Code = "CORP02", IsActive = true };
        context.Corporates.Add(corp);

        var unitKg = new TbEmissionUnit { Id = 10, Title = "kg", IsActive = true };
        var unitTon = new TbEmissionUnit { Id = 11, Title = "ton", IsActive = true };
        context.Units.AddRange(unitKg, unitTon);

        var group = new TbEmissionGroup { Id = 2, TgoScopeNo = eTgoScopeNo.Scope1, Title = "Material", Code = "S1-M", IsActive = true };
        context.EmissionGroups.Add(group);

        var actGroup2 = new TbActivityGroup { Id = 2, Code = "0102", Title = "Material Group", TgoScopeNo = eTgoScopeNo.Scope1, IsActive = true };
        context.ActivityGroups.Add(actGroup2);

        var source = new TbEmissionSource
        {
            Id = 10,
            Title = "LPG EF per kg",
            GroupId = 2,
            UnitId = 10, // EF Unit is kg
            Unit = unitKg,
            CO2 = 3.0m,
            gwpCO2 = 1m,
            IsActive = true
        };
        context.EmissionSources.Add(source);

        // Activity Unit is ton (Id=11), EF Unit is kg (Id=10), but Ratio is 1 (mismatched!)
        var child = new TbActivityTopicChild
        {
            Id = 2,
            TopicId = 2,
            ActivityGroupId = 2,
            SourceId = 10,
            Source = source,
            ActivityUnitId = 11, // Activity Unit is ton
            ActivityUnit = unitTon,
            EmissionSharePercent = 100f,
            UnitConversionRatio = 1m, // Ratio is 1 => Mismatch warning expected!
            IsActive = true
        };

        var ev = new TbActivityEvent
        {
            Id = 2,
            SubscriptionId = 1,
            TopicId = 2,
            Quantity = 50m,
            TimeStamp = new DateTime(2026, 7, 1),
            IsActive = true
        };

        var topic = new TbActivityTopic
        {
            Id = 2,
            SubscriptionId = 1,
            Level = eTopicLevel.Corporate,
            TargetId = 2,
            Title = "LPG Purchase",
            EmissionGroupId = 2,
            IsActive = true
        };
        topic.Add(child);
        topic.Add(ev);
        context.Topics.Add(topic);
        context.TopicChilds.Add(child);
        context.Events.Add(ev);
        await context.SaveChangesAsync();

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppContext.BaseDirectory);
        mockEnv.Setup(e => e.WebRootPath).Returns(AppContext.BaseDirectory);

        var reportService = new CfoReportService(dbFactory, mockEnv.Object, NullLogger<CfoReportService>.Instance);
        var report = await reportService.GenerateReportAsync(eTopicLevel.Corporate, 2, 2026, null);

        // Assert GHG-13 mismatch warning
        Assert.Contains(report.PreFlightWarnings, w => w.Contains("[GHG-13 Unit Warning]") && w.Contains("LPG Purchase"));
    }

    [Fact]
    public async Task GHG13_CfoReportService_IndependentActivityAndEfUnitNames_ResolvesCorrectly()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var context = await dbFactory.CreateDbContextAsync();

        var corp = new TbCorporate { Id = 3, SubscriptionId = 1, Title = "Test Corp 3", Code = "CORP03", IsActive = true };
        context.Corporates.Add(corp);

        var unitTon = new TbEmissionUnit { Id = 20, Title = "ton", IsActive = true };
        var unitKg = new TbEmissionUnit { Id = 21, Title = "kg", IsActive = true };
        context.Units.AddRange(unitTon, unitKg);

        var group = new TbEmissionGroup { Id = 3, TgoScopeNo = eTgoScopeNo.Scope1, Title = "Combustion", Code = "S1-C", IsActive = true };
        context.EmissionGroups.Add(group);

        var actGroup3 = new TbActivityGroup { Id = 3, Code = "0103", Title = "Combustion Group", TgoScopeNo = eTgoScopeNo.Scope1, IsActive = true };
        context.ActivityGroups.Add(actGroup3);

        var source = new TbEmissionSource
        {
            Id = 20,
            Title = "Coal EF per kg",
            GroupId = 3,
            UnitId = 21, // EF unit = kg
            Unit = unitKg,
            CO2 = 2.5m,
            gwpCO2 = 1m,
            IsActive = true
        };
        context.EmissionSources.Add(source);

        var child = new TbActivityTopicChild
        {
            Id = 3,
            TopicId = 3,
            ActivityGroupId = 3,
            SourceId = 20,
            Source = source,
            ActivityUnitId = 20, // Activity unit = ton
            ActivityUnit = unitTon,
            EmissionSharePercent = 100f,
            UnitConversionRatio = 1000m, // 1 ton = 1000 kg
            IsActive = true
        };

        var ev = new TbActivityEvent
        {
            Id = 3,
            SubscriptionId = 1,
            TopicId = 3,
            Quantity = 2m, // 2 tons
            TimeStamp = new DateTime(2026, 5, 1),
            IsActive = true
        };

        var topic = new TbActivityTopic
        {
            Id = 3,
            SubscriptionId = 1,
            Level = eTopicLevel.Corporate,
            TargetId = 3,
            Title = "Coal Usage",
            EmissionGroupId = 3,
            IsActive = true
        };
        topic.Add(child);
        topic.Add(ev);
        context.Topics.Add(topic);
        context.TopicChilds.Add(child);
        context.Events.Add(ev);
        await context.SaveChangesAsync();

        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.ContentRootPath).Returns(AppContext.BaseDirectory);
        mockEnv.Setup(e => e.WebRootPath).Returns(AppContext.BaseDirectory);

        var reportService = new CfoReportService(dbFactory, mockEnv.Object, NullLogger<CfoReportService>.Instance);
        var report = await reportService.GenerateReportAsync(eTopicLevel.Corporate, 3, 2026, null);

        var line = Assert.Single(report.AuditLines);
        Assert.Equal("ton", line.ActivityUnitName);
        Assert.Equal("kg", line.EfUnitName);
        Assert.Equal(1000m, line.Ratio);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(2000m, line.ConvertedQuantity);
        Assert.Equal(5000m, line.KgCO2e); // 2000 kg * 2.5 kgCO2/kg = 5000 kgCO2e = 5 tCO2e
        Assert.Equal(5.0m, line.Total_tCO2e);
    }
    #endregion
}
