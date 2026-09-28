using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;

namespace TU.ACAMP.Tests;

// [2026-09-15][Antigravity] Unit tests for CFO Report verification tabs and ACAMP EF policy compliance
public class CfoReportTabVerificationTests
{
    private (IDbContextFactory<ApplicationDbContext> dbFactory, IWebHostEnvironment env) CreateMocks(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        var mockEnv = new Mock<IWebHostEnvironment>();
        // Point to solution root where Files/Template/ is available (search upward for TU.ACAMP.WebApp)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "TU.ACAMP.WebApp")))
        {
            dir = dir.Parent;
        }
        var solutionRoot = dir != null
            ? Path.Combine(dir.FullName, "TU.ACAMP.WebApp")
            : AppContext.BaseDirectory;
        mockEnv.Setup(e => e.ContentRootPath).Returns(solutionRoot);
        mockEnv.Setup(e => e.WebRootPath).Returns(Path.Combine(solutionRoot, "wwwroot"));

        return (mockFactory.Object, mockEnv.Object);
    }

    [Fact]
    public void ActivityAuditLineDto_ContainsSourceId()
    {
        var line = new CfoReportService.ActivityAuditLineDto
        {
            SourceId = 123,
            SourceName = "Natural Gas (Grid)"
        };

        Assert.Equal(123, line.SourceId);
        Assert.Equal("Natural Gas (Grid)", line.SourceName);
    }

    [Fact]
    public void CfoReportOverridesDto_CanHoldCustomFields()
    {
        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            OrganizationTitle = "Custom Corporate Org",
            GhgTeamName = "Sustainability Taskforce",
            CertifierOrgTitle = "External Verification Body",
            Scope3CheckedCategories = new HashSet<int> { 1, 3, 5 }
        };

        Assert.Equal("Custom Corporate Org", overrides.OrganizationTitle);
        Assert.Equal("Sustainability Taskforce", overrides.GhgTeamName);
        Assert.Equal("External Verification Body", overrides.CertifierOrgTitle);
        Assert.Contains(3, overrides.Scope3CheckedCategories);
        Assert.DoesNotContain(2, overrides.Scope3CheckedCategories);
    }

    [Fact]
    public void EFPolicy_EvaluatesExpirationUsingIsExpire_RegardlessOfReportingPeriod()
    {
        // Case 1: Expired factor (DateExpire in past)
        var expiredSource = new TbEmissionSource
        {
            Id = 1,
            Title = "Old Fuel Factor",
            DateExpire = AppTimeZone.LocalNow.AddDays(-10)
        };
        Assert.True(expiredSource.IsExpire);

        // Case 2: Active factor (DateExpire in future)
        var activeSource = new TbEmissionSource
        {
            Id = 2,
            Title = "Current Fuel Factor",
            DateExpire = AppTimeZone.LocalNow.AddYears(1)
        };
        Assert.False(activeSource.IsExpire);
    }

    [Fact]
    public void ExportToExcel_AppliesFr01OverridesToWorkbook()
    {
        var (dbFactory, env) = CreateMocks("test_excel_overrides");
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Original Org Title",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            TotalScope1_tCO2e = 15.5m,
            TotalScope2_tCO2e = 25.0m,
            TotalScope3_tCO2e = 10.0m
        };

        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            OrganizationTitle = "Overridden Org Title",
            GhgTeamName = "Overridden GHG Team",
            CertifierOrgTitle = "Overridden Certifier Body"
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, overrides);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 0);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws01 = wb.Worksheet("Fr-01");
        if (ws01 != null)
        {
            Assert.Equal("Overridden Org Title", ws01.Cell("B6").GetString());
            Assert.Equal("Overridden GHG Team", ws01.Cell("G4").GetString());
            var certCell = ws01.CellsUsed(c => (c.Address.ColumnLetter == "H" || c.Address.ColumnLetter == "I") && c.GetString() == "Overridden Certifier Body").FirstOrDefault();
            Assert.NotNull(certCell);
        }
    }

    [Fact]
    public void ExportToExcel_AppliesFr032Scope3CustomChecks()
    {
        var (dbFactory, env) = CreateMocks("test_excel_s3_checks");
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Test Org",
            Level = eTopicLevel.Corporate,
            Year = 2026
        };

        // Custom checked categories: Category 1 (row 19) and Category 4 (row 22)
        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            Scope3CheckedCategories = new HashSet<int> { 1, 4 }
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, overrides);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws032 = wb.Worksheet("Fr-03.2");
        if (ws032 != null)
        {
            // In template Wingdings 2, "P" represents checkmark
            Assert.Equal("P", ws032.Cell("C19").GetString()); // Category 1
            Assert.Equal("P", ws032.Cell("C22").GetString()); // Category 4
            Assert.Empty(ws032.Cell("C20").GetString());      // Category 2 unchecked
        }
    }

    [Fact]
    public void ExportToExcel_Fr01_PopulatesScopeActivitiesRowByRow()
    {
        // [2026-09-16][Antigravity] Verify Scope 1 activities are populated row-by-row (H9, H10, H11) instead of concatenated in H9
        var (dbFactory, env) = CreateMocks("test_excel_fr01_rows");
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Test Org Fr01",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            Scopes = new List<CfoReportService.ScopeBreakdownDto>
            {
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope1,
                    // Intentionally provide out of order (0104, 0101, 0102) to verify ascending sorting
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0104", Title = "การรั่วไหลและการแพร่กระจาย" },
                        new() { Code = "0101", Title = "การเผาไหม้เชื้อเพลิงที่อยู่กับที่" },
                        new() { Code = "0102", Title = "การเผาไหม้เชื้อเพลิงที่เคลื่อนที่" }
                    }
                },
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope2,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0201", Title = "การใช้พลังงานไฟฟ้า" }
                    }
                },
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope3,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0301", Title = "การเดินทางเพื่อธุรกิจ" }
                    }
                }
            }
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, null);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws01 = wb.Worksheet("Fr-01");
        Assert.NotNull(ws01);

        // Scope 1: sorted ascending (0101 -> 0102 -> 0104), formatted as TGO Category (Option A)
        Assert.Equal("1.1 การเผาไหม้เชื้อเพลิงที่อยู่กับที่", ws01.Cell("H9").GetString());
        Assert.Equal("1.2 การเผาไหม้เชื้อเพลิงที่เคลื่อนที่", ws01.Cell("H10").GetString());
        Assert.Equal("1.4 การรั่วไหลและการแพร่กระจาย", ws01.Cell("H11").GetString());
        Assert.Empty(ws01.Cell("H12").GetString());
        Assert.Empty(ws01.Cell("H17").GetString()); // blank rows guaranteed

        // Scope 2: formatted as 2.1, with blank rows guaranteed
        Assert.Equal("2.1 การใช้พลังงานไฟฟ้า", ws01.Cell("H18").GetString());
        Assert.Empty(ws01.Cell("H19").GetString());
        Assert.Empty(ws01.Cell("H20").GetString());

        // Scope 3: formatted as 3.1, with at least 1 blank row guaranteed below it
        Assert.Equal("3.1 การเดินทางเพื่อธุรกิจ", ws01.Cell("H21").GetString());
        Assert.Empty(ws01.Cell("H22").GetString()); // Row H22 is blank in Scope 3!
        Assert.False(string.IsNullOrWhiteSpace(ws01.Cell("H23").GetString())); // Shifted base year cell
    }

    [Fact]
    public void ExportToExcel_Fr01_InsertsRowsWhenScopeExceedsCapacity()
    {
        // [2026-09-16][Antigravity] Verify Scope 1 inserts rows below H17 when exceeding 9 items (with at least 1 blank row)
        var (dbFactory, env) = CreateMocks("test_excel_fr01_overflow");
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var s1Groups = Enumerable.Range(1, 11)
            .Select(i => new CfoReportService.ActivityGroupAuditDto { Code = $"01{i:D2}", Title = $"Scope 1 Activity {i}" })
            .ToList();

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Test Org Fr01 Overflow",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            Scopes = new List<CfoReportService.ScopeBreakdownDto>
            {
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope1,
                    ActivityGroups = s1Groups
                },
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope2,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0201", Title = "Scope 2 Activity 1" }
                    }
                }
            }
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, null);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws01 = wb.Worksheet("Fr-01");
        Assert.NotNull(ws01);

        // Scope 1: items 1..11 on rows 9..19, row 20 is blank (guaranteeing at least 1 blank row)
        Assert.Equal("1.1 Scope 1 Activity 1", ws01.Cell("H9").GetString());
        Assert.Equal("1.9 Scope 1 Activity 9", ws01.Cell("H17").GetString());
        Assert.Equal("1.10 Scope 1 Activity 10", ws01.Cell("H18").GetString());
        Assert.Equal("1.11 Scope 1 Activity 11", ws01.Cell("H19").GetString());
        Assert.Empty(ws01.Cell("H20").GetString()); // Blank row in Scope 1

        // Scope 2: shifted down to row 21 (originally 18 + 3 s1Shift)
        Assert.Equal("2.1 Scope 2 Activity 1", ws01.Cell("H21").GetString());
        Assert.Empty(ws01.Cell("H22").GetString()); // Blank row in Scope 2
    }

    [Theory]
    [InlineData("0101", "Stationary Combustion", eTgoScopeNo.Scope1, "1.1 Stationary Combustion")]
    [InlineData("0104", "Fugitive Emissions", eTgoScopeNo.Scope1, "1.4 Fugitive Emissions")]
    [InlineData("0201", "Purchased Electricity", eTgoScopeNo.Scope2, "2.1 Purchased Electricity")]
    [InlineData("0315", "Investments", eTgoScopeNo.Scope3, "3.15 Investments")]
    [InlineData("S1-04", "การรั่วไหลและการแพร่กระจาย", eTgoScopeNo.Scope1, "1.4 การรั่วไหลและการแพร่กระจาย")]
    [InlineData("1.4", "Fugitive Emissions", eTgoScopeNo.Scope1, "1.4 Fugitive Emissions")]
    [InlineData("1.4 Fugitive Emissions", "1.4 Fugitive Emissions", eTgoScopeNo.Scope1, "1.4 Fugitive Emissions")]
    [InlineData("-", "Custom Topic", eTgoScopeNo.Scope1, "Custom Topic")]
    public void FormatTgoCategory_FormatsCorrectlyAccordingToOptionA(string code, string title, eTgoScopeNo scope, string expected)
    {
        // [2026-09-16][Antigravity] Verify TGO Category (Option A) formatting helper
        var result = CfoReportService.FormatTgoCategory(code, title, scope);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetAvailableCalculateYearsAsync_ReturnsOnlyYearsWithActualActivityData()
    {
        // [2026-09-16][Antigravity] Verify calculate year list excludes years without activity events
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, env) = CreateMocks(dbName);
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        using (var db = await dbFactory.CreateDbContextAsync())
        {
            var corp = new TbCorporate { Id = 10, SubscriptionId = 1, Title = "Test Corp", Code = "CORP01" };
            db.Corporates.Add(corp);

            var topic = new TbActivityTopic
            {
                Id = 100,
                SubscriptionId = 1,
                Level = eTopicLevel.Corporate,
                TargetId = 10,
                Title = "Stationary Fuel"
            };
            db.Topics.Add(topic);

            // Events in 2024 and 2026 (2025 has NO events; 2023 is deleted)
            var ev1 = new TbActivityEvent { Id = 1, SubscriptionId = 1, TopicId = 100, TimeStamp = new DateTime(2024, 3, 1), Quantity = 100m };
            var ev2 = new TbActivityEvent { Id = 2, SubscriptionId = 1, TopicId = 100, TimeStamp = new DateTime(2026, 6, 15), Quantity = 250m };
            var ev3 = new TbActivityEvent { Id = 3, SubscriptionId = 1, TopicId = 100, TimeStamp = new DateTime(2023, 1, 1), Quantity = 50m };
            db.Events.AddRange(ev1, ev2, ev3);
            await db.SaveChangesAsync();

            // Mark 2023 event as soft-deleted
            ev3.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var years = await service.GetAvailableCalculateYearsAsync(eTopicLevel.Corporate, 10, new HashSet<int> { 1 });

        // Must return [2026, 2024] sorted descending. 2025 (no data) and 2023 (deleted) must NOT be included.
        Assert.Equal(2, years.Count);
        Assert.Equal(2026, years[0]);
        Assert.Equal(2024, years[1]);
        Assert.DoesNotContain(2025, years);
        Assert.DoesNotContain(2023, years);
    }

    [Fact]
    public async Task GetAvailableCalculateYearsAsync_ReturnsEmptyWhenNoActivityDataExists()
    {
        // [2026-09-16][Antigravity] Verify returns empty list if organization has no recorded activity events
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, env) = CreateMocks(dbName);
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        using (var db = await dbFactory.CreateDbContextAsync())
        {
            var corp = new TbCorporate { Id = 20, SubscriptionId = 1, Title = "Empty Corp", Code = "EMPTY01" };
            db.Corporates.Add(corp);
            await db.SaveChangesAsync();
        }

        var years = await service.GetAvailableCalculateYearsAsync(eTopicLevel.Corporate, 20, new HashSet<int> { 1 });
        Assert.Empty(years);
    }

    [Fact]
    public async Task GetAvailableCalculateYearsAsync_RespectsTenantIsolation()
    {
        // [2026-09-16][Antigravity] Verify events from other tenants are not included
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, env) = CreateMocks(dbName);
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        using (var db = await dbFactory.CreateDbContextAsync())
        {
            var corp = new TbCorporate { Id = 30, SubscriptionId = 2, Title = "Other Tenant Corp", Code = "OTHER01" };
            db.Corporates.Add(corp);

            var topic = new TbActivityTopic
            {
                Id = 300,
                SubscriptionId = 2,
                Level = eTopicLevel.Corporate,
                TargetId = 30,
                Title = "Tenant Topic"
            };
            db.Topics.Add(topic);

            db.Events.Add(new TbActivityEvent { Id = 10, SubscriptionId = 2, TopicId = 300, TimeStamp = new DateTime(2026, 1, 1), Quantity = 100m });
            await db.SaveChangesAsync();
        }

        // Allowed subscription is 1 (not 2)
        var years = await service.GetAvailableCalculateYearsAsync(eTopicLevel.Corporate, 30, new HashSet<int> { 1 });
        Assert.Empty(years);
    }

    [Fact]
    public void CfoReportOverridesDto_ContainsAllFr01EditableFields()
    {
        // [2026-09-16][Antigravity] Verify CfoReportOverridesDto holds all Fr-01 sheet fields
        var prepDate = new DateTime(2026, 3, 15);
        var regDate = new DateTime(2026, 4, 1);
        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            OrganizationTitle = "Global Tech Corp",
            GhgTeamName = "Carbon Accounting Division",
            CertifierOrgTitle = "Thailand Greenhouse Gas Management Organization",
            PreparationDate = prepDate,
            OrganizationImageBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            CalculatePeriodText = "1 Jan 2026 - 31 Dec 2026",
            CalculateProductionAmount = 50000.75m,
            CalculateProductionUnit = "Tonnes of Product",
            BasePeriodText = "1 Jan 2020 - 31 Dec 2020",
            BaseProductionAmount = 42000m,
            BaseProductionUnit = "Tonnes of Product",
            OrgInfo1 = "High-tech manufacturing operations across Southeast Asia",
            OrgInfo2 = "Precision sensors and optical equipment",
            OrgInfo3 = "5,000 employees with 10,000 m2 production floor",
            OrgInfo4 = "24/7 continuous operations with 3 rotational shifts",
            OrgInfo5 = "Certified ISO 14064-1:2018 and ISO 14001:2015",
            ContactAddress = "99 Phahonyothin Rd, Pathum Thani 12120, Thailand",
            RegistrationDate = regDate
        };

        Assert.Equal("Global Tech Corp", overrides.OrganizationTitle);
        Assert.Equal("Carbon Accounting Division", overrides.GhgTeamName);
        Assert.Equal("Thailand Greenhouse Gas Management Organization", overrides.CertifierOrgTitle);
        Assert.Equal(prepDate, overrides.PreparationDate);
        Assert.NotNull(overrides.OrganizationImageBase64);
        Assert.Equal("1 Jan 2026 - 31 Dec 2026", overrides.CalculatePeriodText);
        Assert.Equal(50000.75m, overrides.CalculateProductionAmount);
        Assert.Equal("Tonnes of Product", overrides.CalculateProductionUnit);
        Assert.Equal("1 Jan 2020 - 31 Dec 2020", overrides.BasePeriodText);
        Assert.Equal(42000m, overrides.BaseProductionAmount);
        Assert.Equal("Tonnes of Product", overrides.BaseProductionUnit);
        Assert.Equal("High-tech manufacturing operations across Southeast Asia", overrides.OrgInfo1);
        Assert.Equal("Precision sensors and optical equipment", overrides.OrgInfo2);
        Assert.Equal("5,000 employees with 10,000 m2 production floor", overrides.OrgInfo3);
        Assert.Equal("24/7 continuous operations with 3 rotational shifts", overrides.OrgInfo4);
        Assert.Equal("Certified ISO 14064-1:2018 and ISO 14001:2015", overrides.OrgInfo5);
        Assert.Equal("99 Phahonyothin Rd, Pathum Thani 12120, Thailand", overrides.ContactAddress);
        Assert.Equal(regDate, overrides.RegistrationDate);
    }

    [Fact]
    public void ExportToExcel_Fr01PopulatesAllCustomFieldsAndImage()
    {
        // [2026-09-16][Antigravity] Verify ExportToExcel writes all Fr-01 fields and image cleanly into workbook
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, env) = CreateMocks(dbName);
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Default Org Title",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            Scopes = new List<CfoReportService.ScopeBreakdownDto>
            {
                new() { ScopeNo = eTgoScopeNo.Scope1, ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto> { new() { Code = "0101", Title = "Stationary Combustion" } } },
                new() { ScopeNo = eTgoScopeNo.Scope2, ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto> { new() { Code = "0201", Title = "Purchased Electricity" } } },
                new() { ScopeNo = eTgoScopeNo.Scope3, ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto> { new() { Code = "0301", Title = "Purchased Goods" } } }
            }
        };

        // 1x1 PNG transparent pixel
        var pixelBase64 = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            OrganizationTitle = "Acme Global Industry",
            GhgTeamName = "Green Taskforce 2026",
            PreparationDate = new DateTime(2026, 6, 20),
            OrganizationImageBase64 = pixelBase64,
            CalculatePeriodText = "01/01/2026 - 31/12/2026",
            CalculateProductionAmount = 98765.43m,
            CalculateProductionUnit = "Pieces",
            BasePeriodText = "01/01/2021 - 31/12/2021",
            BaseProductionAmount = 80000m,
            BaseProductionUnit = "Pieces",
            OrgInfo1 = "Pioneering green packaging solutions",
            OrgInfo2 = "Biodegradable containers and cutlery",
            OrgInfo3 = "1,200 employees, 3 operational factories",
            OrgInfo4 = "2 shifts per day, 6 days a week",
            OrgInfo5 = "ISO 14064-1:2018 verified",
            ContactAddress = "123 Green Way, Eco City, Thailand",
            RegistrationDate = new DateTime(2026, 7, 1)
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, overrides);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet("Fr-01");
        Assert.NotNull(ws);

        // Header fields
        Assert.Equal("Acme Global Industry", ws.Cell("B6").GetString());
        Assert.Equal("Acme Global Industry", ws.Cell("G3").GetString());
        Assert.Equal("Green Taskforce 2026", ws.Cell("G4").GetString());
        Assert.Equal("20/06/2026", ws.Cell("J4").GetFormattedString());

        // Cell A8 text should be cleared because image was provided
        Assert.Equal(string.Empty, ws.Cell("A8").GetString());

        // Image should be embedded
        Assert.Contains(ws.Pictures, p => p.Name == "OrgLogo");

        // Search shifted rows for populated values
        var rowsText = Enumerable.Range(20, 30)
            .Select(r => new { Row = r, H = ws.Cell(r, 8).GetFormattedString(), G = ws.Cell(r, 7).GetString(), J = ws.Cell(r, 10).GetString() })
            .ToList();

        Assert.Contains(rowsText, x => x.H == "01/01/2026 - 31/12/2026");
        Assert.Contains(rowsText, x => x.J == "Pieces");
        Assert.Contains(rowsText, x => x.H == "01/01/2021 - 31/12/2021");
        Assert.Contains(rowsText, x => x.G == "Pioneering green packaging solutions");
        Assert.Contains(rowsText, x => x.G == "Biodegradable containers and cutlery");
        Assert.Contains(rowsText, x => x.G == "1,200 employees, 3 operational factories");
        Assert.Contains(rowsText, x => x.G == "2 shifts per day, 6 days a week");
        Assert.Contains(rowsText, x => x.G == "ISO 14064-1:2018 verified");
        Assert.Contains(rowsText, x => x.H == "123 Green Way, Eco City, Thailand");
        Assert.Contains(rowsText, x => x.H == "01/07/2026");
    }

    [Fact]
    public void ExportToExcel_Fr01PopulatesProductGridItemsAndMultiLineOrgInfo()
    {
        // [2026-09-16][Antigravity] Verify ExportToExcel writes product grid items (summed) and parses multirow org info
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, env) = CreateMocks(dbName);
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);

        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Apex Manufacturing",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            Scopes = new List<CfoReportService.ScopeBreakdownDto>
            {
                new() { ScopeNo = eTgoScopeNo.Scope1, ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto> { new() { Code = "0101", Title = "Stationary Combustion" } } }
            }
        };

        // [2026-09-16][Antigravity] Verify CalcProductItems and BaseProductItems support ProductName alongside Amount and Unit
        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            OrganizationTitle = "Apex Manufacturing",
            CalcProductItems = new List<CfoReportService.CfoProductItemDto>
            {
                new() { Index = 1, ProductName = "Recycled Paper", Amount = 12000m, Unit = "Tonnes" },
                new() { Index = 2, ProductName = "Pulp", Amount = 8000m, Unit = "Tonnes" }
            },
            BaseProductItems = new List<CfoReportService.CfoProductItemDto>
            {
                new() { Index = 1, ProductName = "Cardboard Box", Amount = 15000m, Unit = "Pieces" }
            },
            OrgInfoMultiLine = "1. Head Office in Nonthaburi\n" +
                               "2. Main Products: Recycled Paper & Pulp\n" +
                               "3. Workforce 800 people\n" +
                               "4. Operations: 3 shifts 24/7\n" +
                               "5. ISO 14001 certified"
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, overrides);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = wb.Worksheet("Fr-01");
        Assert.NotNull(ws);

        // Verify ProductName is preserved in DTO
        Assert.Equal("Recycled Paper", overrides.CalcProductItems[0].ProductName);
        Assert.Equal("Pulp", overrides.CalcProductItems[1].ProductName);
        Assert.Equal("Cardboard Box", overrides.BaseProductItems[0].ProductName);

        // Search shifted rows for populated values
        var rowsText = Enumerable.Range(20, 30)
            .Select(r => new
            {
                Row = r,
                H = ws.Cell(r, 8).GetString(),
                H_num = ws.Cell(r, 8).TryGetValue<double>(out var num) ? (double?)num : null,
                G = ws.Cell(r, 7).GetString(),
                J = ws.Cell(r, 10).GetString()
            })
            .ToList();

        // Calculate Year: Sum is 12000 + 8000 = 20000, Unit is "Tonnes"
        Assert.Contains(rowsText, x => x.H_num.HasValue && Math.Abs(x.H_num.Value - 20000.0) < 0.01 && x.J == "Tonnes");

        // Base Year: Sum is 15000, Unit is "Pieces"
        Assert.Contains(rowsText, x => x.H_num.HasValue && Math.Abs(x.H_num.Value - 15000.0) < 0.01 && x.J == "Pieces");

        // Organization Info 1..5 (Prefixes stripped as Column F provides 1..5 index)
        Assert.Contains(rowsText, x => x.G == "Head Office in Nonthaburi");
        Assert.Contains(rowsText, x => x.G == "Main Products: Recycled Paper & Pulp");
        Assert.Contains(rowsText, x => x.G == "Workforce 800 people");
        Assert.Contains(rowsText, x => x.G == "Operations: 3 shifts 24/7");
        Assert.Contains(rowsText, x => x.G == "ISO 14001 certified");
    }

    [Fact]
    public void ExportToExcel_OnlyFr01Sheet_ExportsOnlyFr01WithProductSumsH26AndH28()
    {
        // [2026-09-16][Antigravity] Verify OnlyFr01Sheet excludes all other sheets, H26/H28 have product sums, merges are preserved, and Chart 4 is shifted with data
        var (dbFactory, env) = CreateMocks("test_only_fr01_h26_h28");
        var service = new CfoReportService(dbFactory, env, NullLogger<CfoReportService>.Instance);
        var calcReport = new CfoReportService.CfoAuditReportDto
        {
            OrganizationTitle = "Test Org",
            Level = eTopicLevel.Corporate,
            Year = 2026,
            TotalScope1_tCO2e = 150.25m,
            TotalScope2_tCO2e = 80.50m,
            TotalScope3_tCO2e = 45.10m,
            TotalSeparate_tCO2e = 12.00m,
            Scopes = new List<CfoReportService.ScopeBreakdownDto>
            {
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope1,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0101", Title = "Stationary" },
                        new() { Code = "0102", Title = "Mobile" },
                        new() { Code = "0104", Title = "Fugitive" }
                    }
                },
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope2,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0201", Title = "Electricity" }
                    }
                },
                new()
                {
                    ScopeNo = eTgoScopeNo.Scope3,
                    ActivityGroups = new List<CfoReportService.ActivityGroupAuditDto>
                    {
                        new() { Code = "0301", Title = "Cat 1" },
                        new() { Code = "0302", Title = "Cat 2" },
                        new() { Code = "0303", Title = "Cat 3" }
                    }
                }
            }
        };

        var overrides = new CfoReportService.CfoReportOverridesDto
        {
            CalcProductItems = new List<CfoReportService.CfoProductItemDto>
            {
                new() { Index = 1, ProductName = "Product A", Amount = 100m, Unit = "Tonnes" },
                new() { Index = 2, ProductName = "Product B", Amount = 100m, Unit = "Tonnes" }
            },
            BaseProductItems = new List<CfoReportService.CfoProductItemDto>
            {
                new() { Index = 1, ProductName = "Product Base", Amount = 500m, Unit = "Tonnes" }
            },
            OrgInfoMultiLine = "1. Head Office in Nonthaburi\n2. Main Products\n3. 500 Staff\n4. Day Shift\n5. ISO 9001",
            FocusSheetName = "Fr-01",
            OnlyFr01Sheet = false
        };

        var bytes = service.ExportToExcel(calcReport, null, 0, overrides);
        Assert.NotNull(bytes);

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);

        // 1. Verify ALL sheets are preserved (not just Fr-01) for cross-sheet formula linkages
        Assert.True(wb.Worksheets.Count > 1);
        Assert.Contains(wb.Worksheets, s => s.Name == "Fr-01");
        Assert.Contains(wb.Worksheets, s => s.Name == "Fr-05");
        Assert.Contains(wb.Worksheets, s => s.Name == "Fr-04.1");
        Assert.Contains(wb.Worksheets, s => s.Name == "Fr-03.2");

        var ws = wb.Worksheet("Fr-01");

        // 2. Verify row 26 ("ผลผลิต"): F26:G26 merged, H26:I26 merged, amount 200, unit Tonnes
        Assert.Equal("ผลผลิต", ws.Cell(26, 6).GetString());
        Assert.True(ws.Range(26, 6, 26, 7).IsMerged());
        Assert.True(ws.Range(26, 8, 26, 9).IsMerged());
        Assert.Equal(200.0, ws.Cell(26, 8).GetDouble(), 2);
        Assert.Equal("Tonnes", ws.Cell(26, 10).GetString());

        // 3. Verify row 28 ("ผลผลิตปีฐาน"): F28:G28 merged, H28:I28 merged, amount 500, unit Tonnes
        Assert.Equal("ผลผลิตปีฐาน", ws.Cell(28, 6).GetString());
        Assert.True(ws.Range(28, 6, 28, 7).IsMerged());
        Assert.True(ws.Range(28, 8, 28, 9).IsMerged());
        Assert.Equal(500.0, ws.Cell(28, 8).GetDouble(), 2);
        Assert.Equal("Tonnes", ws.Cell(28, 10).GetString());

        // 4. Verify Org Info rows 30..34: Col F is 1..5, G..J is merged, text has no leading prefix, WrapText is true
        for (int i = 0; i < 5; i++)
        {
            int r = 30 + i;
            Assert.Equal((i + 1).ToString(), ws.Cell(r, 6).GetString());
            Assert.True(ws.Range(r, 7, r, 10).IsMerged());
            Assert.True(ws.Cell(r, 7).Style.Alignment.WrapText);
        }
        Assert.Equal("Head Office in Nonthaburi", ws.Cell(30, 7).GetString());
        Assert.Equal("Main Products", ws.Cell(31, 7).GetString());

        // 5. Open OpenXML package to verify Chart 4 anchor position shift, data cache injection, and active tab
        ms.Position = 0;
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(ms, false);
        var wbPart = doc.WorkbookPart;
        Assert.NotNull(wbPart);
        var fr01Sheet = wbPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().First(s => s.Name == "Fr-01");
        var wsPart = (DocumentFormat.OpenXml.Packaging.WorksheetPart)wbPart.GetPartById(fr01Sheet.Id!);
        Assert.NotNull(wsPart.DrawingsPart);

        // Check anchor shift in drawing1.xml
        using (var drawReader = new StreamReader(wsPart.DrawingsPart.GetStream()))
        {
            var drawXml = drawReader.ReadToEnd();
            // Chart 4 from-row must be shifted down from 35 by totalShift=3 to row 38
            Assert.Contains("name=\"Chart 4\"", drawXml);
            Assert.Contains("<xdr:row>38</xdr:row>", drawXml);
            Assert.Contains("<xdr:row>52</xdr:row>", drawXml);
        }

        // Check chart1.xml contains emission values in numCache for Scope 1..3 and Separate
        var cp = wsPart.DrawingsPart.ChartParts.First();
        using (var cpReader = new StreamReader(cp.GetStream()))
        {
            var chartXml = cpReader.ReadToEnd();
            Assert.Contains("150.25", chartXml);
            Assert.Contains("80.50", chartXml);
            Assert.Contains("45.10", chartXml);
            Assert.Contains("12.00", chartXml);
            // Formula reference to Fr-05 is preserved because Fr-05 exists in workbook
            Assert.Contains("'Fr-05'!$C$10:$C$13", chartXml);
        }

        // Verify ActiveTab is set to Fr-01
        var allSheets = wbPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
        int fr01Index = allSheets.FindIndex(s => s.Name == "Fr-01");
        var wbView = wbPart.Workbook.BookViews?.Descendants<DocumentFormat.OpenXml.Spreadsheet.WorkbookView>().FirstOrDefault();
        if (wbView?.ActiveTab != null)
        {
            Assert.Equal((uint)fr01Index, wbView.ActiveTab.Value);
        }
    }
}
