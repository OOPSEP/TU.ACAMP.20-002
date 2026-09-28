using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>
/// [2026-09-24][Claude] ทดสอบ DatabaseSeeder7 (องค์กรจำลอง 1 Corporate / 1 Company / 2 Facilities ครบ 4 ขอบเขต 2023-ปัจจุบัน)
/// ใช้ Master Library ขนาดเล็กที่ Id ตรงกับ tb_emission_source.json เพื่อไม่ต้องพึ่งไฟล์ JSON ตอนรันเทสต์
/// </summary>
public class DatabaseSeeder7Tests
{
    private static ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    #region fixture
    private static async Task SeedLibraryFixtureAsync(ApplicationDbContext context)
    {
        var groups = new (int Id, string Code, eTgoScopeNo Scope)[]
        {
            (1, "0101", eTgoScopeNo.Scope1), (2, "0102", eTgoScopeNo.Scope1), (4, "0104", eTgoScopeNo.Scope1), (5, "0105", eTgoScopeNo.Scope1),
            (7, "0201", eTgoScopeNo.Scope2),
            (8, "0402-BIO", eTgoScopeNo.SeparateReport), (10, "0405-BIO", eTgoScopeNo.SeparateReport), (11, "0401-IPCC", eTgoScopeNo.SeparateReport),
            (14, "0303", eTgoScopeNo.Scope3), (15, "0304", eTgoScopeNo.Scope3), (16, "0305", eTgoScopeNo.Scope3),
            (17, "0306", eTgoScopeNo.Scope3), (28, "0324", eTgoScopeNo.Scope3), (30, "0317", eTgoScopeNo.Scope3)
        };
        foreach (var g in groups)
        {
            context.EmissionGroups.Add(new TbEmissionGroup { Id = g.Id, Code = g.Code, Title = g.Code, TgoScopeNo = g.Scope, IsReadOnly = true, SourceType = eSourceType.Default });
        }

        // ActivityGroup Id 1-24 ตาม tb_activity_group.json (1-4 = S1, 5-6 = S2, 7-21 = S3, 22-24 = S4)
        for (int id = 1; id <= 24; id++)
        {
            var scope = id <= 4 ? eTgoScopeNo.Scope1 : id <= 6 ? eTgoScopeNo.Scope2 : id <= 21 ? eTgoScopeNo.Scope3 : eTgoScopeNo.SeparateReport;
            context.ActivityGroups.Add(new TbActivityGroup { Id = id, Code = $"AG{id:D2}", Title = $"Activity Group {id}", TgoScopeNo = scope });
        }

        var sources = new (int Id, int GroupId, int UnitId, string Title)[]
        {
            (6, 1, 1, "LPG"), (20, 1, 13, "Diesel B7: Report in Scope 1"), (21, 8, 13, "Diesel B7: Report in Biogenic CO2 emissions"),
            (79, 10, 13, "Diesel B7: Report in Biogenic CO2 emissions"), (106, 2, 1, "LPG"), (114, 2, 13, "Diesel B7: Report in Scope 1"),
            (123, 2, 13, "Gasohol E20: Report in Scope 1"), (131, 7, 3, "ไฟฟ้าแบบ grid mix ปี 2022-2024 (CFO Scope2)"), (132, 15, 3, "ไฟฟ้า upstream"),
            (134, 11, 1, "R-22 (HCFC-22)"), (135, 4, 1, "R-32"), (138, 4, 1, "R-134a"), (144, 4, 1, "R-410a"),
            (182, 14, 1, "ก๊าซหุงต้ม"), (186, 14, 1, "น้ำมันดีเซล / น้ำมันโซล่าร์"), (189, 16, 2, "น้ำประปา-การประปานครหลวง"),
            (190, 16, 2, "น้ำประปา-การประปาส่วนภูมิภาค"), (254, 17, 5, "รถตู้บรรทุก 10 ล้อ วิ่งปกติ 100% Loading"),
            (626, 28, 1, "การฝังกลบขยะมูลฝอยชุมชนแบบถูกหลักสุขาภิบาล"), (643, 30, 1, "กระดาษพิมพ์เขียนแบบไม่เคลือบผิว"), (685, 5, 1, "CO2")
        };
        foreach (var s in sources)
        {
            context.EmissionSources.Add(new TbEmissionSource { Id = s.Id, GroupId = s.GroupId, UnitId = s.UnitId, Title = s.Title, CO2 = 1m, gwpCO2 = 1m, SourceType = eSourceType.Default, IsReadOnly = true });
        }

        context.EmissionMappings.AddRange(
            new TbEmissionMapping { Id = 32, Title = "Electricity", Scope2ChildId = 131, Scope3ChildId = 132 },
            new TbEmissionMapping { Id = 38, Title = "LPG", Scope1ChildId = 6, Scope3ChildId = 182 },
            new TbEmissionMapping { Id = 62, Title = "LPG", Scope1ChildId = 106, Scope3ChildId = 182 },
            new TbEmissionMapping { Id = 64, Title = "Diesel B7: Report in Scope 1", Scope1ChildId = 20, Scope3ChildId = 186, BiogenicChildId = 21, BiogenicSharePercent = 7m },
            new TbEmissionMapping { Id = 78, Title = "Diesel B7: Report in Scope 1", Scope1ChildId = 114, Scope3ChildId = 186, BiogenicChildId = 79, BiogenicSharePercent = 7m });

        await context.SaveChangesAsync();
    }

    private static async Task<ApplicationDbContext> CreateSeededContextAsync()
    {
        var context = CreateInMemoryContext(Guid.NewGuid().ToString());
        await SeedLibraryFixtureAsync(context);
        await DatabaseSeeder7.SeedAsync(context, NullLogger.Instance);
        return context;
    }
    #endregion

    [Fact]
    public async Task SeedAsync_CreatesOneCorporateOneCompanyTwoFacilities()
    {
        using var context = await CreateSeededContextAsync();

        var sub = await context.Subscriptions.SingleAsync(s => s.TaxId == DatabaseSeeder7.SubscriptionTaxId);
        var corps = await context.Corporates.Where(c => c.SubscriptionId == sub.Id).ToListAsync();
        Assert.Single(corps);

        var comps = await context.Companies.Where(c => c.CorporateId == corps[0].Id).ToListAsync();
        Assert.Single(comps);

        var facilities = await context.Facilities.Where(f => f.CompanyId == comps[0].Id).ToListAsync();
        Assert.Equal(2, facilities.Count);
        Assert.Contains(facilities, f => f.Code == DatabaseSeeder7.PlantCode);
        Assert.Contains(facilities, f => f.Code == DatabaseSeeder7.DcCode);
    }

    /// <summary>คืน (Level, TargetId) ของทุกเป้าหมาย: Corporate, Company และ Facility ทั้ง 2 แห่ง key ด้วย Code</summary>
    private static async Task<Dictionary<string, (eTopicLevel Level, int TargetId)>> GetTargetsAsync(ApplicationDbContext context)
    {
        var result = new Dictionary<string, (eTopicLevel Level, int TargetId)>();
        var corp = await context.Corporates.SingleAsync(c => c.Code == DatabaseSeeder7.CorporateCode);
        result[corp.Code] = (eTopicLevel.Corporate, corp.Id);
        var comp = await context.Companies.SingleAsync(c => c.Code == DatabaseSeeder7.CompanyCode);
        result[comp.Code] = (eTopicLevel.Company, comp.Id);
        foreach (var fac in await context.Facilities.Where(f => f.CompanyId == comp.Id).ToListAsync())
        {
            result[fac.Code] = (eTopicLevel.Facility, fac.Id);
        }
        return result;
    }

    [Fact]
    public async Task SeedAsync_CorporateCompanyAndEachFacilityCoverAllFourScopes()
    {
        using var context = await CreateSeededContextAsync();

        var activityGroupScopes = await context.ActivityGroups.ToDictionaryAsync(g => g.Id, g => g.TgoScopeNo);
        var targets = await GetTargetsAsync(context);
        Assert.Equal(4, targets.Count);

        foreach (var (level, targetId) in targets.Values)
        {
            var topicIds = await context.Topics
                .Where(t => t.Level == level && t.TargetId == targetId)
                .Select(t => t.Id)
                .ToListAsync();
            Assert.NotEmpty(topicIds);

            var scopes = (await context.TopicChilds.Where(c => topicIds.Contains(c.TopicId)).Select(c => c.ActivityGroupId).ToListAsync())
                .Select(id => activityGroupScopes[id])
                .ToHashSet();

            Assert.Contains(eTgoScopeNo.Scope1, scopes);
            Assert.Contains(eTgoScopeNo.Scope2, scopes);
            Assert.Contains(eTgoScopeNo.Scope3, scopes);
            Assert.Contains(eTgoScopeNo.SeparateReport, scopes);

            // ทุกหัวข้อต้องมีข้อมูลกิจกรรม
            foreach (var topicId in topicIds)
            {
                Assert.True(await context.Events.AnyAsync(e => e.TopicId == topicId), $"Topic {topicId} has no events");
            }
        }
    }

    [Fact]
    public async Task SeedAsync_MonthlyTopicsHaveEveryMonth_SporadicTopicsHaveSomeMonthsEveryYear()
    {
        using var context = await CreateSeededContextAsync();

        var lastMonth = DatabaseSeeder7.GetLastClosedMonth();
        int expectedMonths = (lastMonth.Year - DatabaseSeeder7.FirstYear) * 12 + lastMonth.Month;
        var targets = await GetTargetsAsync(context);

        foreach (var plan in DatabaseSeeder7.BuildPlans())
        {
            var target = targets[plan.TargetCode];
            Assert.Equal(target.Level, plan.Level);
            var topic = await context.Topics.SingleAsync(t => t.Level == target.Level && t.TargetId == target.TargetId && t.Title == plan.Title);
            var events = await context.Events.Where(e => e.TopicId == topic.Id).ToListAsync();

            Assert.All(events, e => Assert.True(e.Quantity > 0m));
            Assert.All(events, e => Assert.Equal(1, e.TimeStamp.Day));
            Assert.Equal(events.Count, events.Select(e => (e.TimeStamp.Year, e.TimeStamp.Month)).Distinct().Count());
            Assert.All(events, e => Assert.True(e.TimeStamp <= lastMonth && e.TimeStamp.Year >= DatabaseSeeder7.FirstYear));

            switch (plan.Pattern)
            {
                case DatabaseSeeder7.eEventPattern.Monthly:
                    Assert.Equal(expectedMonths, events.Count);
                    break;
                case DatabaseSeeder7.eEventPattern.Sporadic:
                    Assert.True(events.Count < expectedMonths, $"Sporadic topic '{plan.Title}' should not have every month");
                    for (int year = DatabaseSeeder7.FirstYear; year <= lastMonth.Year; year++)
                    {
                        Assert.Contains(events, e => e.TimeStamp.Year == year);
                    }
                    break;
                case DatabaseSeeder7.eEventPattern.Annual:
                    Assert.All(events, e => Assert.Equal(plan.AnnualMonth, e.TimeStamp.Month));
                    break;
            }
        }
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateAnything()
    {
        using var context = await CreateSeededContextAsync();

        int subs = await context.Subscriptions.CountAsync();
        int facilities = await context.Facilities.CountAsync();
        int topics = await context.Topics.CountAsync();
        int childs = await context.TopicChilds.CountAsync();
        int events = await context.Events.CountAsync();

        await DatabaseSeeder7.SeedAsync(context, NullLogger.Instance);

        Assert.Equal(subs, await context.Subscriptions.CountAsync());
        Assert.Equal(facilities, await context.Facilities.CountAsync());
        Assert.Equal(topics, await context.Topics.CountAsync());
        Assert.Equal(childs, await context.TopicChilds.CountAsync());
        Assert.Equal(events, await context.Events.CountAsync());
    }

    // [2026-09-24][Claude] EF ของกิ่ง Biogenic (เช่น Diesel B7: Report in Biogenic) รวมสัดส่วน 7% ไว้แล้ว
    // กิ่ง Biogenic ต้องมี EmissionSharePercent = 100 ไม่ใช่ BiogenicSharePercent ของ mapping (กันคูณซ้ำ)
    [Fact]
    public async Task SeedAsync_BiogenicChilds_UseFullShare_NotMappingBlendPercent()
    {
        using var context = await CreateSeededContextAsync();

        var bioChilds = await context.TopicChilds.Where(c => c.SourceId == 21 || c.SourceId == 79).ToListAsync();

        Assert.NotEmpty(bioChilds);
        Assert.All(bioChilds, c => Assert.Equal(TbEmissionMapping.BiogenicChildEmissionSharePercent, c.EmissionSharePercent));
    }

    [Fact]
    public void GenerateQuantities_IsDeterministicForSameSeed()
    {
        var plan = DatabaseSeeder7.BuildPlans().First();
        var lastMonth = new DateTime(2025, 12, 1);

        var first = DatabaseSeeder7.GenerateQuantities(plan, lastMonth);
        var second = DatabaseSeeder7.GenerateQuantities(plan, lastMonth);

        Assert.Equal(36, first.Count);
        Assert.Equal(first, second);
    }
    // [2026-09-26][Claude] สถานการณ์ปีฐาน 2024 / ปีคำนวณ 2025: ทุกเดือนของ 2025 = เดือนเดียวกันของ 2024 x ตัวคูณ (ทุกแผน)
    [Fact]
    public void GenerateQuantities_CalcYearEqualsBaseYearTimesMonthlyFactor()
    {
        var lastMonth = new DateTime(2025, 12, 1);
        foreach (var plan in DatabaseSeeder7.BuildPlans())
        {
            var data = DatabaseSeeder7.GenerateQuantities(plan, lastMonth);
            var baseYear = data.Where(d => d.Period.Year == DatabaseSeeder7.ScenarioBaseYear).ToDictionary(d => d.Period.Month, d => d.Quantity);
            var calcYear = data.Where(d => d.Period.Year == DatabaseSeeder7.ScenarioCalcYear).ToDictionary(d => d.Period.Month, d => d.Quantity);

            Assert.Equal(baseYear.Keys.OrderBy(m => m), calcYear.Keys.OrderBy(m => m));
            foreach (var (month, baseQty) in baseYear)
            {
                var expected = Math.Round((decimal)((double)baseQty * DatabaseSeeder7.ScenarioCalcYearFactors[month - 1]), plan.Decimals, MidpointRounding.AwayFromZero);
                Assert.Equal(expected, calcYear[month]);
            }
        }
    }

    [Fact]
    public async Task SeedAsync_SeedsNetZeroTarget_BaseYear2024()
    {
        using var context = await CreateSeededContextAsync();
        var sub = await context.Subscriptions.SingleAsync(s => s.TaxId == DatabaseSeeder7.SubscriptionTaxId);
        var target = await context.NetZeroTargets.SingleAsync(t => t.SubscriptionId == sub.Id && t.CorporateId == null);

        Assert.Equal(2024, target.BaseYear);
        Assert.Equal(2030, target.TargetYear);
        Assert.Equal(50m, target.TargetReductionPercent);
    }
}
