using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using TU.ACAMP.Domain.Helper;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;

namespace TU.ACAMP.Tests;

public class Phase3DataIntegrityTests
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

    #region 4.1 [GHG-08] Negative Quantity Rejection Tests
    [Fact]
    public async Task GHG08_CreateAsync_NegativeQuantity_ThrowsInvalidOperationException()
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var service = new ActivityEventService(dbFactory, authProvider, NullLogger<ActivityEventService>.Instance);

        var ev = new TbActivityEvent
        {
            SubscriptionId = 1,
            TopicId = 101,
            TimeStamp = new DateTime(2026, 8, 1),
            Quantity = -50.0m, // Invalid negative quantity
            Remark = "Negative Test"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(ev));
        Assert.Contains("Quantity ต้องไม่ติดลบ", ex.Message);
    }

    [Fact]
    public async Task GHG08_UpdateAsync_NegativeQuantity_ThrowsInvalidOperationException()
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var service = new ActivityEventService(dbFactory, authProvider, NullLogger<ActivityEventService>.Instance);

        // Seed an event first
        var context = await dbFactory.CreateDbContextAsync();
        var ev = new TbActivityEvent
        {
            SubscriptionId = 1,
            TopicId = 101,
            TimeStamp = new DateTime(2026, 8, 1),
            Quantity = 100m,
            Remark = "Valid Event"
        };
        context.Events.Add(ev);
        await context.SaveChangesAsync();

        // Update to negative quantity
        ev.Quantity = -25m;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(ev));
        Assert.Contains("Quantity ต้องไม่ติดลบ", ex.Message);
    }

    [Fact]
    public void GHG08_TbActivityEvent_ValidationAttribute_RejectsNegativeQuantity()
    {
        var ev = new TbActivityEvent
        {
            Quantity = -10.5m
        };

        var context = new ValidationContext(ev) { MemberName = nameof(TbActivityEvent.Quantity) };
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateProperty(ev.Quantity, context, results);

        Assert.False(isValid);
        Assert.Contains(results, r => r.ErrorMessage != null && r.ErrorMessage.Contains("Quantity ต้องไม่ติดลบ"));
    }

    [Fact]
    public void GHG08_ImportFromExcel_NegativeQuantity_ProducesRowError()
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var service = new ActivityEventService(dbFactory, authProvider, NullLogger<ActivityEventService>.Instance);

        // Create in-memory Excel workbook with negative quantity
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Events");
        ws.Cell(1, 1).Value = "Remark";
        ws.Cell(1, 2).Value = "Time (yyyy-MM)";
        ws.Cell(1, 3).Value = "Quantity";

        ws.Cell(2, 1).Value = "Row 1 Valid";
        ws.Cell(2, 2).Value = "2026-01";
        ws.Cell(2, 3).Value = 100;

        ws.Cell(3, 1).Value = "Row 2 Negative";
        ws.Cell(3, 2).Value = "2026-02";
        ws.Cell(3, 3).Value = -50;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var (imported, errors, warnings) = service.ImportFromExcel(ms, topicId: 1, subscriptionId: 1);

        Assert.Single(imported);
        Assert.Equal(100m, imported[0].Quantity);
        Assert.Contains(errors, e => e.Contains("Row 3: Quantity ต้องไม่ติดลบ"));
    }
    #endregion

    #region 4.2 [GHG-16] Validation & Parsing Tests
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(50.0, true)]
    [InlineData(100.0, true)]
    [InlineData(-0.1, false)]
    [InlineData(100.1, false)]
    public void GHG16_Percent0To100Attribute_ValidatesCorrectRange(double value, bool expectedValid)
    {
        var attr = new Percent0To100Attribute();
        var isValid = attr.IsValid(value);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void GHG16_ImportFromExcel_DuplicateMonth_SkipsByDefault()
    {
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var service = new ActivityEventService(dbFactory, authProvider, NullLogger<ActivityEventService>.Instance);

        // Existing events in DB
        var existingEvents = new List<TbActivityEvent>
        {
            new TbActivityEvent { Id = 1, TopicId = 10, TimeStamp = new DateTime(2026, 1, 1), Quantity = 200m }
        };

        // Excel has same month 2026-01 and new month 2026-02
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Events");
        ws.Cell(1, 1).Value = "Remark";
        ws.Cell(1, 2).Value = "Time";
        ws.Cell(1, 3).Value = "Quantity";

        ws.Cell(2, 1).Value = "Jan Duplicate";
        ws.Cell(2, 2).Value = "2026-01";
        ws.Cell(2, 3).Value = 500;

        ws.Cell(3, 1).Value = "Feb New";
        ws.Cell(3, 2).Value = "2026-02";
        ws.Cell(3, 3).Value = 300;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var (imported, errors, warnings) = service.ImportFromExcel(ms, topicId: 10, subscriptionId: 1, existingEvents, ActivityEventService.DuplicateHandlingMode.Skip);

        Assert.Single(imported); // Only Feb imported
        Assert.Equal(new DateTime(2026, 2, 1), imported[0].TimeStamp);
        Assert.Contains(warnings, w => w.Contains("ข้ามรายการเดือน 2026-01"));
    }
    #endregion

    #region 4.3 [GHG-12] EF Expiry Tests
    [Fact]
    public void GHG12_IsExpiredAsOf_ReportingPeriodEnd_EvaluatesAccurately()
    {
        var source = new TbEmissionSource
        {
            Title = "Electricity Grid 2024",
            DateExpire = new DateTime(2025, 6, 30) // Expired June 2025
        };

        // For reporting year 2024 (ends Dec 31, 2024): Not expired
        Assert.False(source.IsExpiredAsOf(new DateTime(2024, 12, 31)));

        // For reporting year 2025 (ends Dec 31, 2025): Expired
        Assert.True(source.IsExpiredAsOf(new DateTime(2025, 12, 31)));
    }
    #endregion

    #region 4.4 [GHG-11] Construction Materials Mapping Tests
    [Fact]
    public void GHG11_ConstructionMaterials_0322_MapsTo_PurchasedGoodsGroup7()
    {
        var source = new TbEmissionSource
        {
            Title = "Ready-mixed Concrete",
            Group = new TbEmissionGroup
            {
                Code = "0322",
                Title = "Construction Materials",
                TgoScopeNo = eTgoScopeNo.Scope3
            }
        };

        var allGroups = new List<TbActivityGroup>
        {
            new TbActivityGroup { Id = 7, Code = "0301", Title = "S3-C01 Purchased Goods", TgoScopeNo = eTgoScopeNo.Scope3 },
            new TbActivityGroup { Id = 11, Code = "0305", Title = "S3-C05 Waste", TgoScopeNo = eTgoScopeNo.Scope3 }
        };

        var matchedGroupId = TbEmissionMapping.GetActivityGroupIdForSource(source, allGroups);

        // Construction materials (0322) must fall through to group 7 (Purchased goods), NOT group 11 (Waste)
        Assert.Equal(7, matchedGroupId);
    }

    [Fact]
    public void GHG11_WasteGroups_0316_and_0324_and_0329_MapTo_WasteGroup11()
    {
        var allGroups = new List<TbActivityGroup>
        {
            new TbActivityGroup { Id = 7, Code = "0301", Title = "S3-C01 Purchased Goods", TgoScopeNo = eTgoScopeNo.Scope3 },
            new TbActivityGroup { Id = 11, Code = "0305", Title = "S3-C05 Waste", TgoScopeNo = eTgoScopeNo.Scope3 }
        };

        var source1 = new TbEmissionSource
        {
            Title = "Municipal Solid Waste",
            Group = new TbEmissionGroup { Code = "0316", TgoScopeNo = eTgoScopeNo.Scope3 }
        };

        var source2 = new TbEmissionSource
        {
            Title = "Waste to Landfill",
            Group = new TbEmissionGroup { Code = "0324", TgoScopeNo = eTgoScopeNo.Scope3 }
        };

        var source3 = new TbEmissionSource
        {
            Title = "Waste Management",
            Group = new TbEmissionGroup { Code = "0329", TgoScopeNo = eTgoScopeNo.Scope3 }
        };

        Assert.Equal(11, TbEmissionMapping.GetActivityGroupIdForSource(source1, allGroups));
        Assert.Equal(11, TbEmissionMapping.GetActivityGroupIdForSource(source2, allGroups));
        Assert.Equal(11, TbEmissionMapping.GetActivityGroupIdForSource(source3, allGroups));
    }
    #endregion

    #region 4.5 [GHG-14] Tenant Isolation in CfoReportService
    [Fact]
    public async Task GHG14_GenerateReportAsync_TargetIdZero_ReturnsEmptyReportFailClosed()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var mockEnv = new Mock<IWebHostEnvironment>();
        var reportService = new CfoReportService(dbFactory, mockEnv.Object, NullLogger<CfoReportService>.Instance);

        var report = await reportService.GenerateReportAsync(eTopicLevel.Corporate, targetId: 0, year: 2026, allowedSubscriptionIds: new HashSet<int> { 1 });

        Assert.Equal(0m, report.TotalGrossEmissions_tCO2e);
        Assert.Empty(report.AuditLines);
    }

    [Fact]
    public async Task GHG14_GenerateReportAsync_OtherTenantCorporate_ReturnsEmptyReportFailClosed()
    {
        var (dbFactory, _) = CreateInMemoryContextFactory(Guid.NewGuid().ToString());
        var mockEnv = new Mock<IWebHostEnvironment>();
        var reportService = new CfoReportService(dbFactory, mockEnv.Object, NullLogger<CfoReportService>.Instance);

        // Seed Tenant 2 corporate
        var context = await dbFactory.CreateDbContextAsync();
        var corpTenant2 = new TbCorporate
        {
            Id = 99,
            SubscriptionId = 2, // Tenant 2
            Code = "CORP2",
            Title = "Tenant 2 Corporate"
        };
        context.Corporates.Add(corpTenant2);
        await context.SaveChangesAsync();

        // User belongs only to Tenant 1
        var allowedSubsUser1 = new HashSet<int> { 1 };

        var report = await reportService.GenerateReportAsync(eTopicLevel.Corporate, targetId: 99, year: 2026, allowedSubscriptionIds: allowedSubsUser1);

        // Must return empty report without data from Tenant 2
        Assert.Empty(report.OrganizationTitle);
        Assert.Equal(0m, report.TotalGrossEmissions_tCO2e);
        Assert.Empty(report.AuditLines);
    }
    #endregion
}
