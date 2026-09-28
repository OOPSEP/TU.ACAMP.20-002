using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class ExecutiveDashboardService : BaseService
{
    #region class constructor
    public ExecutiveDashboardService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider) : base(dbFactory, authStateProvider)
    {
    }
    #endregion

    #region class methods
    public async Task<ExecutiveDashboardResult> GetDashboardDataAsync(
        HashSet<int> allowedSubscriptionIds,
        int selectedYear,
        int? selectedCorporateId,
        int baseYear,
        int targetYear,
        decimal targetReductionPercent,
        decimal alertThresholdPercent = 80.0m)
    {
        #region hide [2026-08-22]
        var result = new ExecutiveDashboardResult();

        // Security check: If user has no allowed subscriptions, return empty state
        if (allowedSubscriptionIds == null || !allowedSubscriptionIds.Any())
        {
            return result;
        }

        using var db = await CreateContextAsync();

        // 1. Fetch Corporates for Filter Dropdown (Filtered by Tenant)
        result.Corporates = await db.Corporates
            .AsNoTracking()
            .Where(c => !c.IsDeleted && allowedSubscriptionIds.Contains(c.SubscriptionId))
            .OrderBy(c => c.Title)
            .ToListAsync();

        var allowedCorporateIds = result.Corporates.Select(c => c.Id).ToHashSet();

        // 2. Fetch Available Event Years dynamically
        var availableYears = await db.Events
            .AsNoTracking()
            .Where(e => !e.IsDeleted && allowedSubscriptionIds.Contains(e.SubscriptionId))
            .Select(e => e.TimeStamp.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        if (!availableYears.Any())
        {
            var currentY = TU.ACAMP.Domain.Tools.AppTimeZone.LocalNow.Year;
            availableYears = new List<int> { currentY, currentY - 1, currentY - 2, currentY - 3 };
        }
        result.AvailableYears = availableYears;

        // 3. Resolve Corporate Topic Scope (Fail-Closed)
        HashSet<int>? matchingTopicIds = null;
        Dictionary<int, string> facilityDict = new();

        var allowedCompanyIds = await db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && allowedCorporateIds.Contains(c.CorporateId))
            .Select(c => c.Id)
            .ToListAsync();

        var facilities = await db.Facilities
            .AsNoTracking()
            .Where(f => !f.IsDeleted && allowedCompanyIds.Contains(f.CompanyId))
            .ToListAsync();

        facilityDict = facilities.ToDictionary(f => f.Id, f => f.Title);

        if (selectedCorporateId.HasValue && selectedCorporateId.Value > 0)
        {
            var corpCompanyIds = await db.Companies
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.CorporateId == selectedCorporateId.Value && allowedCorporateIds.Contains(c.CorporateId))
                .Select(c => c.Id)
                .ToListAsync();

            var corpFacilityIds = facilities
                .Where(f => corpCompanyIds.Contains(f.CompanyId))
                .Select(f => f.Id)
                .ToList();

            var qualifyingTopics = await db.Topics
                .AsNoTracking()
                .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId) &&
                            ((t.Level == eTopicLevel.Corporate && t.TargetId == selectedCorporateId.Value) ||
                             (t.Level == eTopicLevel.Company && corpCompanyIds.Contains(t.TargetId)) ||
                             (t.Level == eTopicLevel.Facility && corpFacilityIds.Contains(t.TargetId))))
                .Select(t => t.Id)
                .ToListAsync();

            matchingTopicIds = qualifyingTopics.ToHashSet();

            // Fail-Closed: Selected Corporate has no qualifying topics -> return zeroed result
            if (!matchingTopicIds.Any())
            {
                return result;
            }
        }

        // 4. Fetch Events in Single Query with Date Range & Split Query
        // 4. Fetch Events in Single Query with Date Range & Split Query (Reporting period as Unspecified Business Date)
        var minYear = Math.Min(baseYear, selectedYear - 1);
        var fromDate = new DateTime(minYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var toDate = new DateTime(selectedYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var eventsQuery = db.Events
            .AsNoTracking()
            .AsSplitQuery()
            .Include(e => e.Topic)
                .ThenInclude(t => t!.Childs)
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
            .Include(e => e.Topic)
                .ThenInclude(t => t!.Childs)
                    .ThenInclude(c => c.ActivityGroup)
            .Where(e => !e.IsDeleted
                     && allowedSubscriptionIds.Contains(e.SubscriptionId)
                     && e.TimeStamp >= fromDate
                     && e.TimeStamp < toDate);

        if (matchingTopicIds != null)
        {
            eventsQuery = eventsQuery.Where(e => matchingTopicIds.Contains(e.TopicId));
        }

        var allEvents = await eventsQuery.ToListAsync();

        // 5. Partition Events by Year in Memory
        var currentYearEvents = allEvents.Where(e => e.TimeStamp.Year == selectedYear).ToList();
        var baseYearEvents = allEvents.Where(e => e.TimeStamp.Year == baseYear).ToList();
        var priorYearEvents = allEvents.Where(e => e.TimeStamp.Year == (selectedYear - 1)).ToList();

        // 6. Compute Prior Year Total Emissions (Gross only: Scope 1-3, excluding Separate Biogenic)
        decimal priorTotalKg = 0m;
        foreach (var ev in priorYearEvents)
        {
            if (ev.Topic?.Childs == null) continue;
            foreach (var c in ev.Topic.Childs)
            {
                var scope = c.ActivityGroup?.TgoScopeNo ?? c.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope != eTgoScopeNo.SeparateReport)
                {
                    priorTotalKg += EmissionCalculator.CalculateKgCO2e(ev.Quantity, c);
                }
            }
        }
        decimal priorTotalTon = priorTotalKg / 1000m;

        // 7. Compute Base Year Monthly Emissions Map (Gross only)
        var baseYearMonthlyMap = new Dictionary<int, decimal>();
        for (int m = 1; m <= 12; m++) baseYearMonthlyMap[m] = 0m;

        decimal baseYearTotalKg = 0m;
        foreach (var ev in baseYearEvents)
        {
            if (ev.Topic?.Childs == null) continue;
            decimal evKg = 0m;
            foreach (var c in ev.Topic.Childs)
            {
                var scope = c.ActivityGroup?.TgoScopeNo ?? c.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope != eTgoScopeNo.SeparateReport)
                {
                    evKg += EmissionCalculator.CalculateKgCO2e(ev.Quantity, c);
                }
            }
            baseYearTotalKg += evKg;
            var m = ev.TimeStamp.Month;
            if (m >= 1 && m <= 12) baseYearMonthlyMap[m] += evKg;
        }
        result.BaseYearHasData = baseYearTotalKg > 0m;

        // 8. Process Current Selected Year Events
        decimal s1Kg = 0m, s2Kg = 0m, s3Kg = 0m, sepKg = 0m;
        var monthlyMap = new Dictionary<int, decimal>();
        for (int m = 1; m <= 12; m++) monthlyMap[m] = 0m;

        var hotspotGroupMap = new Dictionary<string, decimal>();
        var facilityMap = new Dictionary<string, decimal>();

        // For Materiality Table (Grouped by TopicId)
        var topicEmissionsDict = new Dictionary<int, (string TopicTitle, string GroupTitle, HashSet<eTgoScopeNo> Scopes, decimal TotalKg)>();

        foreach (var ev in currentYearEvents)
        {
            if (ev.Topic == null || ev.Topic.Childs == null) continue;

            decimal eventGrossKg = 0m;
            int topicId = ev.Topic.Id;
            string topicTitle = ev.Topic.Title;

            if (!topicEmissionsDict.ContainsKey(topicId))
            {
                topicEmissionsDict[topicId] = (topicTitle, "General", new HashSet<eTgoScopeNo>(), 0m);
            }

            var (tTitle, _, scopes, currentTopicKg) = topicEmissionsDict[topicId];

            foreach (var c in ev.Topic.Childs)
            {
                var childKg = EmissionCalculator.CalculateKgCO2e(ev.Quantity, c);
                if (childKg == 0m && (c == null || !c.IsActive || c.IsDeleted || c.Source == null)) continue;

                var scope = c.ActivityGroup?.TgoScopeNo ?? c.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                // [B-01] ป้ายกำกับ Scope ในตาราง Hotspot ต้องสะท้อนเฉพาะ Gross (Scope 1-3)
                // ไม่นับ SeparateReport เพื่อไม่ให้ Topic ที่มีก๊าซชีวภาพถูกแสดงเป็น "Scope 3" หรือ "Mixed" อย่างผิดพลาด
                if (scope != eTgoScopeNo.SeparateReport) scopes.Add(scope);

                if (scope == eTgoScopeNo.Scope1)
                {
                    s1Kg += childKg;
                    eventGrossKg += childKg;
                }
                else if (scope == eTgoScopeNo.Scope2)
                {
                    s2Kg += childKg;
                    eventGrossKg += childKg;
                }
                else if (scope == eTgoScopeNo.Scope3)
                {
                    s3Kg += childKg;
                    eventGrossKg += childKg;
                }
                else
                {
                    sepKg += childKg;
                }

                if (scope != eTgoScopeNo.SeparateReport)
                {
                    var grpName = c.ActivityGroup?.Title ?? "General";
                    if (!hotspotGroupMap.ContainsKey(grpName)) hotspotGroupMap[grpName] = 0m;
                    hotspotGroupMap[grpName] += childKg;
                }
            }

            topicEmissionsDict[topicId] = (
                topicTitle,
                ev.Topic.Childs.FirstOrDefault()?.ActivityGroup?.Title ?? "General",
                scopes,
                currentTopicKg + eventGrossKg
            );

            var month = ev.TimeStamp.Month;
            if (month >= 1 && month <= 12)
            {
                monthlyMap[month] += eventGrossKg;
            }

            string facName = "General Corporate";
            if (ev.Topic.Level == eTopicLevel.Facility && facilityDict.TryGetValue(ev.Topic.TargetId, out var fTitle))
            {
                facName = fTitle;
            }
            if (!facilityMap.ContainsKey(facName)) facilityMap[facName] = 0m;
            facilityMap[facName] += eventGrossKg;
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

        // YoY Change calculation (null when prior year has 0 emissions -> UI renders N/A)
        if (priorTotalTon > 0m)
        {
            result.YoYChangePercent = ((result.TotalEmissions - priorTotalTon) / priorTotalTon) * 100m;
        }
        else
        {
            result.YoYChangePercent = null;
        }

        // 9. Populate Donut Chart Data
        result.ScopeDistributionData = new List<ScopeChartItem>
        {
            new ScopeChartItem { ScopeName = "Scope 1 (Direct)", Amount = (double)result.Scope1Emissions },
            new ScopeChartItem { ScopeName = "Scope 2 (Electricity)", Amount = (double)result.Scope2Emissions },
            new ScopeChartItem { ScopeName = "Scope 3 (Value Chain)", Amount = (double)result.Scope3Emissions }
        };

        // 10. Compute Monthly Trajectory & Net-Zero Target Line
        decimal elapsedYears = Math.Max(0, selectedYear - baseYear);
        decimal totalTargetYears = Math.Max(1, targetYear - baseYear);
        decimal targetRatioProgress = Math.Min(1.0m, elapsedYears / totalTargetYears);
        decimal currentTargetReductionRatio = 1.0m - ((targetReductionPercent / 100.0m) * targetRatioProgress);

        string[] monthNames = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        result.MonthlyTrendData = new List<MonthlyTrendItem>();
        for (int m = 1; m <= 12; m++)
        {
            decimal actualTon = monthlyMap[m] / 1000m;
            decimal baseTon = baseYearMonthlyMap[m] / 1000m;
            decimal targetTon = baseTon * currentTargetReductionRatio;

            result.MonthlyTrendData.Add(new MonthlyTrendItem
            {
                MonthName = monthNames[m - 1],
                ActualAmount = (double)actualTon,
                BaseYearAmount = (double)baseTon,
                NetZeroTargetAmount = (double)targetTon,
                // [2026-09-25][Gemini] กำหนดสัดส่วน threshold แจ้งเตือนสีส้ม (>80%)
                WarningThresholdRatio = (double)(alertThresholdPercent / 100.0m)
            });
        }

        // 11. Top 5 Hotspots Bar Chart Data
        result.TopHotspotsData = hotspotGroupMap
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => new HotspotChartItem
            {
                GroupName = kv.Key,
                Amount = (double)(kv.Value / 1000m)
            }).ToList();

        // 12. Facility Comparison Data
        result.FacilityComparisonData = facilityMap
            .OrderByDescending(kv => kv.Value)
            .Take(6)
            .Select(kv => new FacilityChartItem
            {
                FacilityName = kv.Key,
                Amount = (double)(kv.Value / 1000m)
            }).ToList();

        // 13. Materiality DataGrid Table (Grouped by TopicId)
        var topTopics = topicEmissionsDict.Values
            .OrderByDescending(t => t.TotalKg)
            .Take(5)
            .ToList();

        result.TopHotspotsTable = new List<HotspotRow>();
        int rank = 1;
        decimal grandTotalKg = result.TotalEmissions * 1000m;

        foreach (var item in topTopics)
        {
            decimal share = grandTotalKg > 0 ? ((item.TotalKg / grandTotalKg) * 100m) : 0m;
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
                // ไม่มี Scope 1-3 เลย = Topic นี้เป็นก๊าซชีวภาพล้วน (ต้องรายงานแยก)
                scopeText = "Separate (Biogenic)";
            }

            result.TopHotspotsTable.Add(new HotspotRow
            {
                Rank = rank++,
                TopicId = 0,
                ActivityTopic = item.TopicTitle,
                ActivityGroup = item.GroupTitle,
                ScopeDisplay = scopeText,
                TotalKg = item.TotalKg,
                SharePercent = share
            });
        }

        return result;
        #endregion
    }
    #endregion
}
