// [2026-09-17][Gemini] Executive Dashboard Service V2 with database aggregation, multi-level hierarchy, memory cache, and Excel export
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Reports;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class ExecutiveDashboardService2 : BaseService
{
    #region class fields
    private readonly IMemoryCache _memoryCache;
    private readonly CfoReportStorageService _cfoReportStorageService;
    private readonly ILogger<ExecutiveDashboardService2> _logger;
    #endregion

    #region class constructor
    public ExecutiveDashboardService2(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        IMemoryCache memoryCache,
        CfoReportStorageService cfoReportStorageService,
        ILogger<ExecutiveDashboardService2> logger) : base(dbFactory, authStateProvider)
    {
        _memoryCache = memoryCache;
        _cfoReportStorageService = cfoReportStorageService;
        _logger = logger;
    }
    #endregion

    #region filter options method
    public async Task<(List<TbCorporate> Corporates, List<TbCompany> Companies, List<TbFacility> Facilities)> GetFilterOptionsAsync(
        HashSet<int> allowedSubscriptionIds)
    {
        if (allowedSubscriptionIds == null || !allowedSubscriptionIds.Any())
        {
            return (new List<TbCorporate>(), new List<TbCompany>(), new List<TbFacility>());
        }

        using var db = await CreateContextAsync();

        var corporates = await db.Corporates
            .AsNoTracking()
            .Where(c => !c.IsDeleted && allowedSubscriptionIds.Contains(c.SubscriptionId))
            .OrderBy(c => c.Title)
            .ToListAsync();

        var corpIds = corporates.Select(c => c.Id).ToHashSet();

        var companies = await db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && corpIds.Contains(c.CorporateId))
            .OrderBy(c => c.Title)
            .ToListAsync();

        var compIds = companies.Select(c => c.Id).ToHashSet();

        var facilities = await db.Facilities
            .AsNoTracking()
            .Where(f => !f.IsDeleted && compIds.Contains(f.CompanyId))
            .OrderBy(f => f.Title)
            .ToListAsync();

        return (corporates, companies, facilities);
    }
    #endregion

    #region main dashboard data method
    public async Task<ExecutiveDashboardResult2> GetDashboardDataAsync(
        HashSet<int> allowedSubscriptionIds,
        int selectedYear,
        eTopicLevel? level,
        int? targetId,
        int baseYear,
        int targetYear,
        decimal targetReductionPercent,
        decimal carbonOffsets = 0m,
        decimal alertThresholdPercent = 80.0m,
        bool bypassCache = false,
        int fiscalYearStartMonth = 1)
    {
        // [2026-09-26][Claude] เดือนเริ่มปีบัญชี (1 = ม.ค. = ปีปฏิทิน) ค่านอกช่วง 1-12 ถือเป็นปีปฏิทิน
        var fym = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : 1;

        var result = new ExecutiveDashboardResult2
        {
            LastUpdatedAt = AppTimeZone.LocalNow,
            CarbonOffsets = carbonOffsets
        };

        // Security check: Fail-closed if no allowed subscriptions
        if (allowedSubscriptionIds == null || !allowedSubscriptionIds.Any())
        {
            return result;
        }

        // Cache resolution (Cache TTL: 3 minutes)
        var subKey = string.Join(",", allowedSubscriptionIds.OrderBy(x => x));
        var cacheKey = $"Dash2_{subKey}_{level}_{targetId}_{selectedYear}_{baseYear}_{targetYear}_{targetReductionPercent}_{carbonOffsets}_{alertThresholdPercent}_{fym}";

        if (!bypassCache && _memoryCache.TryGetValue(cacheKey, out ExecutiveDashboardResult2? cachedResult) && cachedResult != null)
        {
            return cachedResult;
        }

        using var db = await CreateContextAsync();

        // 1. Fetch Filter Metadata
        var (corporates, companies, facilities) = await GetFilterOptionsAsync(allowedSubscriptionIds);
        result.Corporates = corporates;
        result.Companies = companies;
        result.Facilities = facilities;

        var allowedCorporateIds = corporates.Select(c => c.Id).ToHashSet();
        var allowedCompanyIds = companies.Select(c => c.Id).ToHashSet();
        var allowedFacilityIds = facilities.Select(f => f.Id).ToHashSet();
        var facilityDict = facilities.ToDictionary(f => f.Id, f => f.Title);

        // 2. Fetch Available Event Years dynamically
        // [2026-09-26][Claude] ดึงคู่ ปี/เดือน แล้วแปลงเป็นปีบัญชี เพื่อให้ dropdown ปีตรงกับเดือนเริ่มปีที่เลือก
        var eventYearMonths = await db.Events
            .AsNoTracking()
            .Where(e => !e.IsDeleted && allowedSubscriptionIds.Contains(e.SubscriptionId) && e.TimeStamp.Year >= 2000)
            .Select(e => new { e.TimeStamp.Year, e.TimeStamp.Month })
            .Distinct()
            .ToListAsync();

        var availableYears = eventYearMonths
            .Select(x => ToFiscalPeriod(x.Year, x.Month, fym).FiscalYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        if (!availableYears.Any())
        {
            var currentY = AppTimeZone.LocalNow.Year;
            availableYears = new List<int> { currentY, currentY - 1, currentY - 2, currentY - 3 };
        }
        result.AvailableYears = availableYears;

        // 3. Resolve Organization Scope (Fail-Closed)
        HashSet<int>? qualifyingTopicIds = null;

        if (level.HasValue && targetId.HasValue && targetId.Value > 0)
        {
            var tId = targetId.Value;
            var lvl = level.Value;

            if (lvl == eTopicLevel.Corporate)
            {
                if (!allowedCorporateIds.Contains(tId))
                {
                    _logger.LogWarning("Access denied for Corporate ID {TargetId} under allowed subscriptions", tId);
                    return result; // Fail-closed
                }

                var corpCompanyIds = companies.Where(c => c.CorporateId == tId).Select(c => c.Id).ToList();
                var corpFacilityIds = facilities.Where(f => corpCompanyIds.Contains(f.CompanyId)).Select(f => f.Id).ToList();

                var topicIds = await db.Topics
                    .AsNoTracking()
                    .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId) &&
                                ((t.Level == eTopicLevel.Corporate && t.TargetId == tId) ||
                                 (t.Level == eTopicLevel.Company && corpCompanyIds.Contains(t.TargetId)) ||
                                 (t.Level == eTopicLevel.Facility && corpFacilityIds.Contains(t.TargetId))))
                    .Select(t => t.Id)
                    .ToListAsync();

                qualifyingTopicIds = topicIds.ToHashSet();
            }
            else if (lvl == eTopicLevel.Company)
            {
                if (!allowedCompanyIds.Contains(tId))
                {
                    _logger.LogWarning("Access denied for Company ID {TargetId} under allowed subscriptions", tId);
                    return result; // Fail-closed
                }

                var compFacilityIds = facilities.Where(f => f.CompanyId == tId).Select(f => f.Id).ToList();

                var topicIds = await db.Topics
                    .AsNoTracking()
                    .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId) &&
                                ((t.Level == eTopicLevel.Company && t.TargetId == tId) ||
                                 (t.Level == eTopicLevel.Facility && compFacilityIds.Contains(t.TargetId))))
                    .Select(t => t.Id)
                    .ToListAsync();

                qualifyingTopicIds = topicIds.ToHashSet();
            }
            else if (lvl == eTopicLevel.Facility)
            {
                if (!allowedFacilityIds.Contains(tId))
                {
                    _logger.LogWarning("Access denied for Facility ID {TargetId} under allowed subscriptions", tId);
                    return result; // Fail-closed
                }

                var topicIds = await db.Topics
                    .AsNoTracking()
                    .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId) &&
                                (t.Level == eTopicLevel.Facility && t.TargetId == tId))
                    .Select(t => t.Id)
                    .ToListAsync();

                qualifyingTopicIds = topicIds.ToHashSet();
            }

            if (qualifyingTopicIds != null && !qualifyingTopicIds.Any())
            {
                // Selected organization has no qualifying topics -> fail-closed with empty metrics
                return result;
            }
        }

        // 4. In-Scope Topics Query (Topics metadata & children)
        var topicsQuery = db.Topics
            .AsNoTracking()
            .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId));

        if (qualifyingTopicIds != null)
        {
            topicsQuery = topicsQuery.Where(t => qualifyingTopicIds.Contains(t.Id));
        }

        var inScopeTopics = await topicsQuery
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Source)
                    .ThenInclude(s => s!.Group)
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.ActivityGroup)
            .ToListAsync();

        var inScopeTopicDict = inScopeTopics.ToDictionary(t => t.Id, t => t);
        var inScopeTopicIds = inScopeTopics.Select(t => t.Id).ToHashSet();

        if (!inScopeTopicIds.Any())
        {
            return result;
        }

        // 5. Database Push-Down Aggregation: GroupBy (TopicId, Year, Month)
        var minYear = Math.Min(baseYear, selectedYear - 1);
        // [2026-09-26][Claude] ช่วงวันที่เริ่มที่เดือนเริ่มปีบัญชี (ปีบัญชี Y = เดือน fym ของปี Y ถึงก่อนเดือน fym ของปี Y+1)
        var fromDate = new DateTime(minYear, fym, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var toDate = new DateTime(selectedYear + 1, fym, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var calendarAggregations = await db.Events
            .AsNoTracking()
            .Where(e => !e.IsDeleted
                     && allowedSubscriptionIds.Contains(e.SubscriptionId)
                     && inScopeTopicIds.Contains(e.TopicId)
                     && e.TimeStamp >= fromDate
                     && e.TimeStamp < toDate)
            .GroupBy(e => new
            {
                e.TopicId,
                Year = e.TimeStamp.Year,
                Month = e.TimeStamp.Month
            })
            .Select(g => new
            {
                TopicId = g.Key.TopicId,
                Year = g.Key.Year,
                Month = g.Key.Month,
                TotalQuantity = g.Sum(e => e.Quantity)
            })
            .ToListAsync();

        // [2026-09-26][Claude] แปลงปี/เดือนปฏิทินเป็น ปีบัญชี / ลำดับเดือนในปีบัญชี (1-12) — โค้ดส่วนถัดไปใช้ Year/Month ชุดนี้
        var eventAggregations = calendarAggregations
            .Select(a =>
            {
                var period = ToFiscalPeriod(a.Year, a.Month, fym);
                return new { a.TopicId, Year = period.FiscalYear, Month = period.Position, a.TotalQuantity };
            })
            .ToList();

        // 6. Partition Database Aggregations by Year
        var currentYearAggs = eventAggregations.Where(a => a.Year == selectedYear).ToList();
        var baseYearAggs = eventAggregations.Where(a => a.Year == baseYear).ToList();
        var priorYearAggs = eventAggregations.Where(a => a.Year == (selectedYear - 1)).ToList();

        // 7. Compute Prior Year Total Emissions (Gross only: Scope 1-3)
        decimal priorTotalKg = 0m;
        foreach (var agg in priorYearAggs)
        {
            if (!inScopeTopicDict.TryGetValue(agg.TopicId, out var topic) || topic.Childs == null) continue;
            foreach (var child in topic.Childs)
            {
                var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope != eTgoScopeNo.SeparateReport)
                {
                    priorTotalKg += EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                }
            }
        }
        decimal priorTotalTon = priorTotalKg / 1000m;

        // 8. Compute Base Year Monthly Emissions Map (Gross only)
        var baseYearMonthlyMap = new Dictionary<int, decimal>();
        for (int m = 1; m <= 12; m++) baseYearMonthlyMap[m] = 0m;

        decimal baseYearTotalKg = 0m;
        foreach (var agg in baseYearAggs)
        {
            if (!inScopeTopicDict.TryGetValue(agg.TopicId, out var topic) || topic.Childs == null) continue;
            decimal aggKg = 0m;
            foreach (var child in topic.Childs)
            {
                var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope != eTgoScopeNo.SeparateReport)
                {
                    aggKg += EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                }
            }
            baseYearTotalKg += aggKg;
            if (agg.Month >= 1 && agg.Month <= 12)
            {
                baseYearMonthlyMap[agg.Month] += aggKg;
            }
        }
        result.BaseYearHasData = baseYearTotalKg > 0m;

        // 9. Process Current Selected Year Emissions
        decimal s1Kg = 0m, s2Kg = 0m, s3Kg = 0m, sepKg = 0m;
        var monthlyMap = new Dictionary<int, decimal>();
        var monthlyS1Map = new Dictionary<int, decimal>();
        var monthlyS2Map = new Dictionary<int, decimal>();
        var monthlyS3Map = new Dictionary<int, decimal>();
        for (int m = 1; m <= 12; m++)
        {
            monthlyMap[m] = 0m;
            monthlyS1Map[m] = 0m;
            monthlyS2Map[m] = 0m;
            monthlyS3Map[m] = 0m;
        }

        var hotspotGroupMap = new Dictionary<string, decimal>();
        var facilityMap = new Dictionary<string, decimal>();

        // [2026-09-25][Antigravity] Scope 3 15 Categories Map (Category 1..15)
        var scope3CategoryKgMap = new Dictionary<int, decimal>();
        for (int c = 1; c <= 15; c++) scope3CategoryKgMap[c] = 0m;

        // Materiality calculations: group by topic
        var topicEmissionsMap = new Dictionary<int, (TbActivityTopic Topic, decimal GrossKg, Dictionary<string, decimal> GroupKgMap, HashSet<eTgoScopeNo> Scopes)>();

        var recordedTopicIdsInCurrentYear = new HashSet<int>();

        foreach (var agg in currentYearAggs)
        {
            if (!inScopeTopicDict.TryGetValue(agg.TopicId, out var topic) || topic.Childs == null) continue;

            recordedTopicIdsInCurrentYear.Add(agg.TopicId);

            if (!topicEmissionsMap.TryGetValue(agg.TopicId, out var topicEntry))
            {
                topicEntry = (topic, 0m, new Dictionary<string, decimal>(), new HashSet<eTgoScopeNo>());
                topicEmissionsMap[agg.TopicId] = topicEntry;
            }

            decimal aggGrossKg = 0m;

            foreach (var child in topic.Childs)
            {
                var childKg = EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                if (childKg == 0m && (child == null || !child.IsActive || child.IsDeleted || child.Source == null)) continue;

                var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope != eTgoScopeNo.SeparateReport)
                {
                    topicEntry.Scopes.Add(scope);
                }

                if (scope == eTgoScopeNo.Scope1)
                {
                    s1Kg += childKg;
                    aggGrossKg += childKg;
                    if (agg.Month >= 1 && agg.Month <= 12) monthlyS1Map[agg.Month] += childKg;
                }
                else if (scope == eTgoScopeNo.Scope2)
                {
                    s2Kg += childKg;
                    aggGrossKg += childKg;
                    if (agg.Month >= 1 && agg.Month <= 12) monthlyS2Map[agg.Month] += childKg;
                }
                else if (scope == eTgoScopeNo.Scope3)
                {
                    s3Kg += childKg;
                    aggGrossKg += childKg;
                    if (agg.Month >= 1 && agg.Month <= 12) monthlyS3Map[agg.Month] += childKg;

                    // [2026-09-25][Antigravity] Map Scope 3 emissions into 15 standard GHG categories
                    int catNo = 0;
                    if (child.ActivityGroup != null)
                    {
                        catNo = CfoReportService.ParseScope3Category(child.ActivityGroup.Code);
                        if (catNo == 0 && CfoReportService.Scope3ActivityGroupIdToFr032RowMap.TryGetValue(child.ActivityGroup.Id, out var r))
                        {
                            catNo = r - 18;
                        }
                    }
                    if (catNo < 1 || catNo > 15)
                    {
                        // [2026-09-25][Antigravity] Fallback to parse Category from Topic Title
                        catNo = CfoReportService.ParseScope3Category(topic.Title);
                    }
                    if (catNo < 1 || catNo > 15)
                    {
                        catNo = 1; // Default to Category 1 (Purchased Goods and Services)
                    }
                    scope3CategoryKgMap[catNo] += childKg;
                }
                else
                {
                    sepKg += childKg;
                }

                if (scope != eTgoScopeNo.SeparateReport)
                {
                    var grpName = child.ActivityGroup?.Title ?? "General";
                    if (!hotspotGroupMap.ContainsKey(grpName)) hotspotGroupMap[grpName] = 0m;
                    hotspotGroupMap[grpName] += childKg;

                    if (!topicEntry.GroupKgMap.ContainsKey(grpName)) topicEntry.GroupKgMap[grpName] = 0m;
                    topicEntry.GroupKgMap[grpName] += childKg;
                }
            }

            topicEntry.GrossKg += aggGrossKg;
            topicEmissionsMap[agg.TopicId] = topicEntry;

            if (agg.Month >= 1 && agg.Month <= 12)
            {
                monthlyMap[agg.Month] += aggGrossKg;
            }

            string facName = "General Corporate";
            if (topic.Level == eTopicLevel.Facility && facilityDict.TryGetValue(topic.TargetId, out var fTitle))
            {
                facName = fTitle;
            }
            if (!facilityMap.ContainsKey(facName)) facilityMap[facName] = 0m;
            facilityMap[facName] += aggGrossKg;
        }

        result.Scope1Emissions = s1Kg / 1000m;
        result.Scope2Emissions = s2Kg / 1000m;
        result.Scope3Emissions = s3Kg / 1000m;
        result.SeparateEmissions = sepKg / 1000m;
        result.TotalEmissions = result.Scope1Emissions + result.Scope2Emissions + result.Scope3Emissions;

        if (result.TotalEmissions > 0m)
        {
            result.Scope1Share = (result.Scope1Emissions / result.TotalEmissions) * 100m;
            result.Scope2Share = (result.Scope2Emissions / result.TotalEmissions) * 100m;
            result.Scope3Share = (result.Scope3Emissions / result.TotalEmissions) * 100m;
        }

        // YoY Change calculation
        if (priorTotalTon > 0m)
        {
            result.YoYChangePercent = ((result.TotalEmissions - priorTotalTon) / priorTotalTon) * 100m;
        }
        else
        {
            result.YoYChangePercent = null;
        }

        // 10. Populate Donut Chart Data
        result.ScopeDistributionData = new List<ScopeChartItem2>
        {
            new ScopeChartItem2 { ScopeName = "Scope 1 (Direct)", Amount = (double)result.Scope1Emissions },
            new ScopeChartItem2 { ScopeName = "Scope 2 (Electricity)", Amount = (double)result.Scope2Emissions },
            new ScopeChartItem2 { ScopeName = "Scope 3 (Value Chain)", Amount = (double)result.Scope3Emissions }
        };

        // 11. Compute Monthly Trajectory & Net-Zero Target Line
        decimal elapsedYears = Math.Max(0, selectedYear - baseYear);
        decimal totalTargetYears = Math.Max(1, targetYear - baseYear);
        decimal targetRatioProgress = Math.Min(1.0m, elapsedYears / totalTargetYears);
        decimal currentTargetReductionRatio = 1.0m - ((targetReductionPercent / 100.0m) * targetRatioProgress);

        string[] monthNames = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        result.MonthlyTrendData = new List<MonthlyTrendItem2>();
        for (int m = 1; m <= 12; m++)
        {
            decimal actualTon = monthlyMap[m] / 1000m;
            decimal baseTon = baseYearMonthlyMap[m] / 1000m;
            decimal targetTon = baseTon * currentTargetReductionRatio;

            result.MonthlyTrendData.Add(new MonthlyTrendItem2
            {
                // [2026-09-26][Claude] ชื่อเดือนเรียงตามปีบัญชี (m = ลำดับเดือนในปีบัญชี)
                MonthName = monthNames[(fym - 1 + m - 1) % 12],
                ActualAmount = (double)actualTon,
                BaseYearAmount = (double)baseTon,
                NetZeroTargetAmount = (double)targetTon,
                // [2026-09-25][Gemini] กำหนดสัดส่วน threshold แจ้งเตือนสีส้ม (>80%)
                WarningThresholdRatio = (double)(alertThresholdPercent / 100.0m),
                Scope1Amount = (double)(monthlyS1Map[m] / 1000m),
                Scope2Amount = (double)(monthlyS2Map[m] / 1000m),
                Scope3Amount = (double)(monthlyS3Map[m] / 1000m)
            });
        }

        // [2026-09-25][Antigravity] Populate 12-Month Sparkline trend arrays for KPI cards
        result.MonthlyTotalSparkline = result.MonthlyTrendData.Select(d => d.ActualAmount).ToList();
        result.MonthlyScope1Sparkline = Enumerable.Range(1, 12).Select(m => (double)(monthlyS1Map[m] / 1000m)).ToList();
        result.MonthlyScope2Sparkline = Enumerable.Range(1, 12).Select(m => (double)(monthlyS2Map[m] / 1000m)).ToList();
        result.MonthlyScope3Sparkline = Enumerable.Range(1, 12).Select(m => (double)(monthlyS3Map[m] / 1000m)).ToList();

        // [2026-09-25][Antigravity] Build Scope 3 15 Categories Breakdown
        result.Scope3CategoriesData = new List<Scope3CategoryItem2>();
        for (int c = 1; c <= 15; c++)
        {
            decimal catTon = scope3CategoryKgMap[c] / 1000m;
            decimal share = result.Scope3Emissions > 0m ? (catTon / result.Scope3Emissions) * 100m : 0m;
            string title = c <= CfoReportService.Fr032Rubric.CategoryTitles.Length
                ? CfoReportService.Fr032Rubric.CategoryTitles[c - 1]
                : $"Category {c}";

            result.Scope3CategoriesData.Add(new Scope3CategoryItem2
            {
                CategoryNo = c,
                CategoryTitle = title,
                EmissionsTon = catTon,
                SharePercent = share
            });
        }

        // 12. Top 5 Hotspots Bar Chart Data
        result.TopHotspotsData = hotspotGroupMap
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new HotspotChartItem2
            {
                GroupName = kv.Key,
                Amount = (double)(kv.Value / 1000m)
            }).ToList();

        // 13. Facility Comparison Data
        result.FacilityComparisonData = facilityMap
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => new FacilityChartItem2
            {
                FacilityName = kv.Key,
                Amount = (double)(kv.Value / 1000m)
            }).ToList();

        // 14. Materiality Table (Ranked by TotalKg)
        result.TopHotspotsTable = new List<HotspotRow2>();
        int rank = 1;
        decimal grandTotalKg = result.TotalEmissions * 1000m;

        var sortedTopics = topicEmissionsMap.Values
            .OrderByDescending(t => t.GrossKg)
            .ToList();

        foreach (var item in sortedTopics)
        {
            decimal share = grandTotalKg > 0 ? ((item.GrossKg / grandTotalKg) * 100m) : 0m;
            string scopeText;
            if (item.Scopes.Count == 1)
            {
                var singleScope = item.Scopes.First();
                scopeText = singleScope == eTgoScopeNo.Scope1 ? "Scope 1" : singleScope == eTgoScopeNo.Scope2 ? "Scope 2" : "Scope 3";
            }
            else if (item.Scopes.Count > 1)
            {
                scopeText = "Mixed";
            }
            else
            {
                scopeText = "Separate (Biogenic)";
            }

            // Determine primary activity group with highest emissions, plus indicator if multiple groups
            string groupDisplay = "General";
            if (item.GroupKgMap.Any())
            {
                var topGroup = item.GroupKgMap.OrderByDescending(kv => kv.Value).First().Key;
                var totalDistinctGroups = item.GroupKgMap.Count;
                groupDisplay = totalDistinctGroups > 1 ? $"{topGroup} (+{totalDistinctGroups - 1} groups)" : topGroup;
            }
            else
            {
                var fallbackGroup = item.Topic.Childs.FirstOrDefault()?.ActivityGroup?.Title;
                if (!string.IsNullOrWhiteSpace(fallbackGroup)) groupDisplay = fallbackGroup;
            }

            result.TopHotspotsTable.Add(new HotspotRow2
            {
                Rank = rank++,
                TopicId = item.Topic.Id,
                TopicKey = item.Topic.Id.ToString(),
                ActivityTopic = item.Topic.Title,
                ActivityGroup = groupDisplay,
                ScopeDisplay = scopeText,
                TotalKg = item.GrossKg,
                SharePercent = share,
                Level = item.Topic.Level,
                TargetId = item.Topic.TargetId
            });
        }

        // 15. Data Quality & Coverage Check
        result.DataQuality = new DataQualitySummary2
        {
            InScopeTopicsCount = inScopeTopics.Count,
            TopicsWithDataCount = recordedTopicIdsInCurrentYear.Count
        };

        // Find topics missing activity data for selected year
        foreach (var topic in inScopeTopics)
        {
            if (!recordedTopicIdsInCurrentYear.Contains(topic.Id))
            {
                string targetTitle = "Corporate";
                if (topic.Level == eTopicLevel.Facility && facilityDict.TryGetValue(topic.TargetId, out var facTitle))
                {
                    targetTitle = facTitle;
                }
                else if (topic.Level == eTopicLevel.Company)
                {
                    targetTitle = companies.FirstOrDefault(c => c.Id == topic.TargetId)?.Title ?? "Company";
                }
                else if (topic.Level == eTopicLevel.Corporate)
                {
                    targetTitle = corporates.FirstOrDefault(c => c.Id == topic.TargetId)?.Title ?? "Corporate";
                }

                var scopeFirst = topic.Childs.FirstOrDefault()?.ActivityGroup?.TgoScopeNo ?? topic.Childs.FirstOrDefault()?.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;

                result.DataQuality.MissingDataTopics.Add(new ProblemTopicItem2
                {
                    TopicId = topic.Id,
                    TopicKey = topic.Id.ToString(),
                    TopicTitle = topic.Title,
                    ScopeDisplay = scopeFirst.ToString(),
                    Level = topic.Level,
                    TargetTitle = targetTitle
                });
            }

            // Find expired EFs
            foreach (var child in topic.Childs)
            {
                if (child.Source != null && child.Source.IsExpire)
                {
                    // Avoid duplicate source logging for the same topic
                    if (!result.DataQuality.ExpiredEfs.Any(e => e.SourceId == child.Source.Id && e.TopicTitle == topic.Title))
                    {
                        result.DataQuality.ExpiredEfs.Add(new ProblemEfItem2
                        {
                            SourceId = child.Source.Id,
                            SourceTitle = child.Source.Title,
                            EF = child.Source.EF,
                            UnitName = child.Source.Unit?.Title ?? string.Empty,
                            ExpiryDate = child.Source.DateExpire,
                            TopicTitle = topic.Title
                        });
                    }
                }
            }
        }

        // 16. Emission Intensity Calculation from CFO Report Snapshots
        // [2026-09-26][Claude] รายงาน CFO คิดตามปีปฏิทิน จึงคำนวณ Intensity เฉพาะเมื่อปีเริ่มเดือนมกราคม (ไม่งั้นผลผลิตกับยอดปล่อยคนละช่วงเวลา)
        if (fym == 1)
        {
            await CalculateEmissionIntensityAsync(result, level, targetId, selectedYear);
        }

        // 17. Save to MemoryCache (3 minutes)
        _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(3));

        return result;
    }
    #endregion

    #region fiscal year helpers
    /// <summary>แปลงปี/เดือนปฏิทินเป็นปีบัญชีและลำดับเดือนในปีบัญชี (1-12) ปีบัญชีตั้งชื่อตามปีที่เริ่ม เช่น เริ่ม เม.ย. → FY2026 = เม.ย. 2026 – มี.ค. 2027</summary>
    public static (int FiscalYear, int Position) ToFiscalPeriod(int year, int month, int fiscalYearStartMonth)
    {
        var fym = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : 1;
        var fiscalYear = month >= fym ? year : year - 1;
        var position = ((month - fym + 12) % 12) + 1;
        return (fiscalYear, position);
    }

    /// <summary>ข้อความช่วงเวลาของปีบัญชี เช่น "Apr 2026 - Mar 2027" (ปีปฏิทินได้ "Jan 2026 - Dec 2026")</summary>
    public static string FormatFiscalPeriod(int fiscalYear, int fiscalYearStartMonth)
    {
        var fym = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : 1;
        var start = new DateTime(fiscalYear, fym, 1);
        var end = start.AddMonths(11);
        return $"{start.ToString("MMM yyyy", CultureInfo.InvariantCulture)} - {end.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
    }
    #endregion

    #region emission intensity helper
    private async Task CalculateEmissionIntensityAsync(
        ExecutiveDashboardResult2 result,
        eTopicLevel? level,
        int? targetId,
        int selectedYear)
    {
        try
        {
            if (level.HasValue && targetId.HasValue && targetId.Value > 0)
            {
                var reports = await _cfoReportStorageService.GetReportsAsync(level.Value, targetId.Value);
                var matchingReport = reports.FirstOrDefault(r => r.CalculateYear == selectedYear);
                if (matchingReport != null)
                {
                    var doc = await _cfoReportStorageService.GetReportAsync(matchingReport.ReportKey, level.Value, targetId.Value);
                    if (doc != null)
                    {
                        if (doc.CalcProductItems != null && doc.CalcProductItems.Any())
                        {
                            var totalAmount = doc.CalcProductItems.Sum(p => p.Amount);
                            if (totalAmount > 0)
                            {
                                result.ProductionAmount = totalAmount;
                                result.ProductionUnit = doc.CalcProductItems.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Unit))?.Unit ?? "Tonnes";
                                if (result.TotalEmissions > 0)
                                {
                                    result.EmissionIntensity = result.TotalEmissions / totalAmount;
                                }
                            }
                        }
                        else if (doc.CalculateProductionAmount.HasValue && doc.CalculateProductionAmount.Value > 0)
                        {
                            result.ProductionAmount = doc.CalculateProductionAmount.Value;
                            result.ProductionUnit = !string.IsNullOrWhiteSpace(doc.CalculateProductionUnit) ? doc.CalculateProductionUnit : "Tonnes";
                            if (result.TotalEmissions > 0)
                            {
                                result.EmissionIntensity = result.TotalEmissions / doc.CalculateProductionAmount.Value;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating emission intensity for Year {Year}", selectedYear);
        }
    }
    #endregion

    #region excel export method
    public byte[] ExportToExcel(ExecutiveDashboardResult2 data, int selectedYear, string organizationTitle, string? periodText = null)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Executive Dashboard");

        // Styling Palette
        var navyColor = XLColor.FromHtml("#0F172A");
        var emeraldColor = XLColor.FromHtml("#10B981");
        var lightGreyColor = XLColor.FromHtml("#F1F5F9");

        // 1. Header Title
        ws.Cell("A1").Value = "CARBON ONE — Executive Carbon Dashboard";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontColor = navyColor;

        // [2026-09-26][Claude] แสดงช่วงเวลาของปีบัญชีเมื่อส่งมา
        var periodPart = string.IsNullOrWhiteSpace(periodText) ? string.Empty : $" ({periodText})";
        ws.Cell("A2").Value = $"Organization: {organizationTitle} | Reporting Year: {selectedYear}{periodPart} | Exported: {AppTimeZone.LocalNow:yyyy-MM-dd HH:mm:ss}";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Cell("A2").Style.Font.FontSize = 10;
        ws.Cell("A2").Style.Font.FontColor = XLColor.DimGray;

        // 2. KPI Summary Section
        ws.Cell("A4").Value = "1. KPI SUMMARY";
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("A4").Style.Font.FontSize = 12;

        ws.Cell("A5").Value = "Metric";
        ws.Cell("B5").Value = "Value (tCO₂e)";
        ws.Cell("C5").Value = "Share (%)";
        ws.Range("A5:C5").Style.Font.Bold = true;
        ws.Range("A5:C5").Style.Fill.BackgroundColor = lightGreyColor;

        ws.Cell("A6").Value = "Gross Footprint (Scope 1+2+3)";
        ws.Cell("B6").Value = (double)data.GrossEmissions;
        ws.Cell("C6").Value = 100.0;

        ws.Cell("A7").Value = "Carbon Offsets & RECs";
        ws.Cell("B7").Value = (double)data.CarbonOffsets;
        ws.Cell("C7").Value = (double)data.OffsetRatioPercent;

        ws.Cell("A8").Value = "Net Footprint (Gross - Offsets)";
        ws.Cell("B8").Value = (double)data.NetEmissions;
        ws.Cell("C8").Value = "-";

        ws.Cell("A9").Value = "Scope 1 (Direct)";
        ws.Cell("B9").Value = (double)data.Scope1Emissions;
        ws.Cell("C9").Value = (double)data.Scope1Share;

        ws.Cell("A10").Value = "Scope 2 (Electricity)";
        ws.Cell("B10").Value = (double)data.Scope2Emissions;
        ws.Cell("C10").Value = (double)data.Scope2Share;

        ws.Cell("A11").Value = "Scope 3 (Value Chain)";
        ws.Cell("B11").Value = (double)data.Scope3Emissions;
        ws.Cell("C11").Value = (double)data.Scope3Share;

        ws.Cell("A12").Value = "Biogenic CO₂ (Separate Report)";
        ws.Cell("B12").Value = (double)data.SeparateEmissions;
        ws.Cell("C12").Value = "-";

        ws.Cell("A13").Value = "YoY Change vs. Prior Year";
        ws.Cell("B13").Value = data.YoYChangePercent.HasValue ? $"{data.YoYChangePercent.Value:F1}%" : "N/A";
        ws.Cell("C13").Value = "-";

        // [2026-09-25][Gemini] ปรับฟอร์แมตตัวเลขภาพรวมเป็น N2
        ws.Cell("A14").Value = "Emission Intensity";
        ws.Cell("B14").Value = data.EmissionIntensity.HasValue ? $"{data.EmissionIntensity.Value:N2} tCO₂e / {data.ProductionUnit}" : "N/A";
        ws.Cell("C14").Value = "-";

        ws.Range("B6:B12").Style.NumberFormat.Format = "#,##0.00";
        ws.Range("C6:C11").Style.NumberFormat.Format = "0.0\"%\"";
        ws.Range("A5:C14").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range("A5:C14").Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        // 3. Monthly Trajectory Section
        ws.Cell("A16").Value = "2. MONTHLY TRAJECTORY & NET-ZERO PATHWAY";
        ws.Cell("A16").Style.Font.Bold = true;
        ws.Cell("A16").Style.Font.FontSize = 12;

        // [2026-09-26][Claude] เดิมมีแค่ Met / Exceeded ทำให้เดือนที่อยู่ช่วงเตือน (> threshold ถึง 100%) หายจาก Excel
        //   → เพิ่มคอลัมน์ Actual (ยอดจริงทั้งเดือน) และ Approaching ให้ครบ 3 สถานะเหมือนกราฟบนหน้าจอ
        var thresholdText = data.MonthlyTrendData.Any()
            ? (data.MonthlyTrendData[0].WarningThresholdRatio * 100).ToString("0.#", CultureInfo.InvariantCulture)
            : "80";
        ws.Cell("A17").Value = "Month";
        ws.Cell("B17").Value = "Base Year (tCO₂e)";
        ws.Cell("C17").Value = "Actual (tCO₂e)";
        ws.Cell("D17").Value = $"Actual Met Target ≤{thresholdText}% (tCO₂e)";
        ws.Cell("E17").Value = $"Actual Approaching >{thresholdText}% (tCO₂e)";
        ws.Cell("F17").Value = "Actual Exceeded >100% (tCO₂e)";
        ws.Cell("G17").Value = "Net-Zero Target (tCO₂e)";
        ws.Range("A17:G17").Style.Font.Bold = true;
        ws.Range("A17:G17").Style.Fill.BackgroundColor = lightGreyColor;

        int row = 18;
        foreach (var m in data.MonthlyTrendData)
        {
            ws.Cell(row, 1).Value = m.MonthName;
            ws.Cell(row, 2).Value = m.BaseYearAmount;
            ws.Cell(row, 3).Value = m.ActualAmount;
            ws.Cell(row, 4).Value = m.ActualMetTargetAmount;
            ws.Cell(row, 5).Value = m.ActualWarningTargetAmount;
            ws.Cell(row, 6).Value = m.ActualExceededTargetAmount;
            // [2026-09-26][Claude] ปีฐานไม่มีข้อมูล → หน้าจอซ่อนเส้นเป้าหมาย ใน Excel ใส่ "-" แทน 0 กันอ่านผิดว่าเป้าหมายเป็นศูนย์
            if (data.BaseYearHasData)
            {
                ws.Cell(row, 7).Value = m.NetZeroTargetAmount;
            }
            else
            {
                ws.Cell(row, 7).Value = "-";
            }
            row++;
        }
        ws.Range($"B18:G{row - 1}").Style.NumberFormat.Format = "#,##0.00";
        ws.Range($"A17:G{row - 1}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range($"A17:G{row - 1}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        // 4. Carbon Hotspots & Facilities
        row += 1;
        ws.Cell(row, 1).Value = "3. TOP CARBON HOTSPOTS";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;

        row++;
        ws.Cell(row, 1).Value = "Activity Group";
        ws.Cell(row, 2).Value = "Emissions (tCO₂e)";
        ws.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor = lightGreyColor;

        var hotspotStartRow = row + 1;
        row++;
        foreach (var h in data.TopHotspotsData)
        {
            ws.Cell(row, 1).Value = h.GroupName;
            ws.Cell(row, 2).Value = h.Amount;
            row++;
        }
        if (data.TopHotspotsData.Any())
        {
            ws.Range($"B{hotspotStartRow}:B{row - 1}").Style.NumberFormat.Format = "#,##0.00";
            ws.Range($"A{hotspotStartRow - 1}:B{row - 1}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range($"A{hotspotStartRow - 1}:B{row - 1}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // 5. Facility Comparison
        row += 1;
        // [2026-09-26][Claude] ระบุว่าเป็น Top 6 ให้ตรงกับข้อมูลจริง (FacilityComparisonData ตัดไว้ 6 อันดับ)
        ws.Cell(row, 1).Value = "4. FACILITY FOOTPRINT BENCHMARK (TOP 6)";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;

        row++;
        ws.Cell(row, 1).Value = "Facility Name";
        ws.Cell(row, 2).Value = "Emissions (tCO₂e)";
        ws.Range(row, 1, row, 2).Style.Font.Bold = true;
        ws.Range(row, 1, row, 2).Style.Fill.BackgroundColor = lightGreyColor;

        var facStartRow = row + 1;
        row++;
        foreach (var f in data.FacilityComparisonData)
        {
            ws.Cell(row, 1).Value = f.FacilityName;
            ws.Cell(row, 2).Value = f.Amount;
            row++;
        }
        if (data.FacilityComparisonData.Any())
        {
            ws.Range($"B{facStartRow}:B{row - 1}").Style.NumberFormat.Format = "#,##0.00";
            ws.Range($"A{facStartRow - 1}:B{row - 1}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range($"A{facStartRow - 1}:B{row - 1}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // 6. Scope 3 15 Categories Breakdown
        row += 1;
        ws.Cell(row, 1).Value = "5. SCOPE 3 VALUE CHAIN BREAKDOWN (15 CATEGORIES)";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;

        row++;
        ws.Cell(row, 1).Value = "Cat #";
        ws.Cell(row, 2).Value = "Scope 3 Category Name";
        ws.Cell(row, 3).Value = "Emissions (tCO₂e)";
        ws.Cell(row, 4).Value = "Share of Scope 3 (%)";
        ws.Cell(row, 5).Value = "Status";
        ws.Range(row, 1, row, 5).Style.Font.Bold = true;
        ws.Range(row, 1, row, 5).Style.Fill.BackgroundColor = lightGreyColor;

        var s3StartRow = row + 1;
        row++;
        foreach (var c in data.Scope3CategoriesData)
        {
            ws.Cell(row, 1).Value = c.CategoryNo;
            ws.Cell(row, 2).Value = c.CategoryTitle;
            ws.Cell(row, 3).Value = (double)c.EmissionsTon;
            ws.Cell(row, 4).Value = (double)c.SharePercent;
            ws.Cell(row, 5).Value = c.HasData ? "Reported" : "No Activity";
            row++;
        }
        if (data.Scope3CategoriesData.Any())
        {
            // [2026-09-25][Gemini] ปรับฟอร์แมตปริมาณการปล่อยเป็น N2 เหมาะกับภาพรวมผู้บริหาร
            ws.Range($"C{s3StartRow}:C{row - 1}").Style.NumberFormat.Format = "#,##0.00";
            ws.Range($"D{s3StartRow}:D{row - 1}").Style.NumberFormat.Format = "0.0\"%\"";
            ws.Range($"A{s3StartRow - 1}:E{row - 1}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range($"A{s3StartRow - 1}:E{row - 1}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // 7. Materiality Table
        row += 1;
        ws.Cell(row, 1).Value = "6. MATERIALITY MATRIX";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 12;

        row++;
        ws.Cell(row, 1).Value = "#";
        ws.Cell(row, 2).Value = "Activity Topic";
        ws.Cell(row, 3).Value = "Activity Group";
        ws.Cell(row, 4).Value = "Scope";
        ws.Cell(row, 5).Value = "Total (tCO₂e)";
        ws.Cell(row, 6).Value = "Share (%)";
        ws.Range(row, 1, row, 6).Style.Font.Bold = true;
        ws.Range(row, 1, row, 6).Style.Fill.BackgroundColor = lightGreyColor;

        var matStartRow = row + 1;
        row++;
        foreach (var m in data.TopHotspotsTable)
        {
            ws.Cell(row, 1).Value = m.Rank;
            ws.Cell(row, 2).Value = m.ActivityTopic;
            ws.Cell(row, 3).Value = m.ActivityGroup;
            ws.Cell(row, 4).Value = m.ScopeDisplay;
            ws.Cell(row, 5).Value = (double)m.TotalTon;
            ws.Cell(row, 6).Value = (double)m.SharePercent;
            row++;
        }
        if (data.TopHotspotsTable.Any())
        {
            // [2026-09-25][Gemini] ปรับฟอร์แมตปริมาณการปล่อยเป็น N2 เหมาะกับภาพรวมผู้บริหาร
            ws.Range($"E{matStartRow}:E{row - 1}").Style.NumberFormat.Format = "#,##0.00";
            ws.Range($"F{matStartRow}:F{row - 1}").Style.NumberFormat.Format = "0.0\"%\"";
            ws.Range($"A{matStartRow - 1}:F{row - 1}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range($"A{matStartRow - 1}:F{row - 1}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        // Auto-fit columns
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
    #endregion
}
