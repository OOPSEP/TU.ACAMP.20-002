// [2026-09-26][Claude] Service ของ Net-Zero Pathway Dashboard (D2): ยอดปล่อยหลายปี + เป้าหมายเส้นตรง + เส้นคาดการณ์
//   ใช้กฎคำนวณชุดเดียวกับ ExecutiveDashboardService2 (EmissionCalculator, Scope จาก ActivityGroup/Source Group, ไม่รวม Separate)
//   โดยไม่แก้ ExecutiveDashboardService2
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class NetZeroDashboardService : BaseService
{
    #region class fields
    private readonly IMemoryCache _memoryCache;
    private readonly ExecutiveDashboardService2 _executiveService;
    private readonly ILogger<NetZeroDashboardService> _logger;
    #endregion

    #region class constructor
    public NetZeroDashboardService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        IMemoryCache memoryCache,
        ExecutiveDashboardService2 executiveService,
        ILogger<NetZeroDashboardService> logger) : base(dbFactory, authStateProvider)
    {
        _memoryCache = memoryCache;
        _executiveService = executiveService;
        _logger = logger;
    }
    #endregion

    #region main data method
    public async Task<NetZeroDashboardResult> GetDashboardDataAsync(
        HashSet<int> allowedSubscriptionIds,
        eTopicLevel? level,
        int? targetId,
        int baseYear,
        int targetYear,
        decimal targetReductionPercent,
        int fiscalYearStartMonth = 1,
        bool bypassCache = false)
    {
        var fym = fiscalYearStartMonth is >= 1 and <= 12 ? fiscalYearStartMonth : 1;
        var result = new NetZeroDashboardResult
        {
            LastUpdatedAt = AppTimeZone.LocalNow,
            BaseYear = baseYear,
            TargetYear = targetYear,
            TargetReductionPercent = targetReductionPercent
        };

        // Fail-closed: ไม่มี Subscription ที่อนุญาต → คืนค่าว่าง
        if (allowedSubscriptionIds == null || !allowedSubscriptionIds.Any())
        {
            return result;
        }

        var subKey = string.Join(",", allowedSubscriptionIds.OrderBy(x => x));
        var cacheKey = $"NzDash_{subKey}_{level}_{targetId}_{baseYear}_{targetYear}_{targetReductionPercent}_{fym}";
        if (!bypassCache && _memoryCache.TryGetValue(cacheKey, out NetZeroDashboardResult? cached) && cached != null)
        {
            return cached;
        }

        // 1. ตัวเลือกองค์กร (ใช้ของ Executive เพื่อให้ขอบเขตสิทธิ์ตรงกัน)
        var (corporates, companies, facilities) = await _executiveService.GetFilterOptionsAsync(allowedSubscriptionIds);
        result.Corporates = corporates;
        result.Companies = companies;
        result.Facilities = facilities;

        using var db = await CreateContextAsync();

        // 2. Topic ในขอบเขตองค์กรที่เลือก (fail-closed ถ้าเลือกองค์กรที่ไม่มีสิทธิ์)
        HashSet<int>? qualifyingTopicIds = null;
        if (level.HasValue && targetId.HasValue && targetId.Value > 0)
        {
            var tId = targetId.Value;
            List<int> companyIds = new();
            List<int> facilityIds = new();

            if (level.Value == eTopicLevel.Corporate)
            {
                if (!corporates.Any(c => c.Id == tId)) return result;
                companyIds = companies.Where(c => c.CorporateId == tId).Select(c => c.Id).ToList();
                facilityIds = facilities.Where(f => companyIds.Contains(f.CompanyId)).Select(f => f.Id).ToList();
            }
            else if (level.Value == eTopicLevel.Company)
            {
                if (!companies.Any(c => c.Id == tId)) return result;
                facilityIds = facilities.Where(f => f.CompanyId == tId).Select(f => f.Id).ToList();
            }
            else if (level.Value == eTopicLevel.Facility)
            {
                if (!facilities.Any(f => f.Id == tId)) return result;
            }

            var lvl = level.Value;
            var ids = await db.Topics
                .AsNoTracking()
                .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId) &&
                            ((t.Level == lvl && t.TargetId == tId) ||
                             (lvl == eTopicLevel.Corporate && t.Level == eTopicLevel.Company && companyIds.Contains(t.TargetId)) ||
                             (lvl != eTopicLevel.Facility && t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId))))
                .Select(t => t.Id)
                .ToListAsync();
            qualifyingTopicIds = ids.ToHashSet();
            if (!qualifyingTopicIds.Any()) return result;
        }

        var topicsQuery = db.Topics
            .AsNoTracking()
            .Where(t => !t.IsDeleted && allowedSubscriptionIds.Contains(t.SubscriptionId));
        if (qualifyingTopicIds != null)
        {
            topicsQuery = topicsQuery.Where(t => qualifyingTopicIds.Contains(t.Id));
        }

        var topics = await topicsQuery
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Source)
                    .ThenInclude(s => s!.Group)
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.ActivityGroup)
            .ToListAsync();
        if (!topics.Any()) return result;

        var topicDict = topics.ToDictionary(t => t.Id);
        var topicIds = topicDict.Keys.ToHashSet();

        // 3. รวมยอดฝั่ง DB ต่อ (Topic, ปี, เดือน) ครั้งเดียวทุกปี
        var aggs = await db.Events
            .AsNoTracking()
            .Where(e => !e.IsDeleted
                     && allowedSubscriptionIds.Contains(e.SubscriptionId)
                     && topicIds.Contains(e.TopicId)
                     && e.TimeStamp.Year >= 2000)
            .GroupBy(e => new { e.TopicId, Year = e.TimeStamp.Year, Month = e.TimeStamp.Month })
            .Select(g => new { g.Key.TopicId, g.Key.Year, g.Key.Month, TotalQuantity = g.Sum(e => e.Quantity) })
            .ToListAsync();

        // 4. คำนวณยอดรายปีบัญชีแยก Scope (kg)
        var yearly = new SortedDictionary<int, decimal[]>(); // [s1, s2, s3]
        foreach (var agg in aggs)
        {
            if (!topicDict.TryGetValue(agg.TopicId, out var topic) || topic.Childs == null) continue;
            var fy = ExecutiveDashboardService2.ToFiscalPeriod(agg.Year, agg.Month, fym).FiscalYear;
            if (!yearly.TryGetValue(fy, out var bucket))
            {
                bucket = new decimal[3];
                yearly[fy] = bucket;
            }

            foreach (var child in topic.Childs)
            {
                var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                if (scope == eTgoScopeNo.SeparateReport) continue;
                var kg = EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                if (scope == eTgoScopeNo.Scope1) bucket[0] += kg;
                else if (scope == eTgoScopeNo.Scope2) bucket[1] += kg;
                else if (scope == eTgoScopeNo.Scope3) bucket[2] += kg;
            }
        }

        var now = AppTimeZone.LocalNow;
        var currentFy = ExecutiveDashboardService2.ToFiscalPeriod(now.Year, now.Month, fym).FiscalYear;
        string Label(int y) => (fym == 1 ? y.ToString() : $"FY{y}") + (y >= currentFy ? " (YTD)" : string.Empty);

        var dataYears = yearly.Where(kv => kv.Value.Sum() > 0m).Select(kv => kv.Key).ToList();
        if (!dataYears.Any())
        {
            _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(3));
            return result;
        }

        // 5. รายการรายปีต่อเนื่อง ตั้งแต่ปีแรก (หรือปีฐาน) ถึงปีล่าสุดที่มีข้อมูล
        var firstYear = Math.Min(dataYears.Min(), baseYear);
        var lastDataYear = dataYears.Max();
        for (int y = firstYear; y <= lastDataYear; y++)
        {
            yearly.TryGetValue(y, out var b);
            b ??= new decimal[3];
            result.YearlyItems.Add(new NzYearlyItem
            {
                Year = y,
                YearLabel = Label(y),
                IsYearToDate = y >= currentFy,
                Scope1Ton = b[0] / 1000m,
                Scope2Ton = b[1] / 1000m,
                Scope3Ton = b[2] / 1000m
            });
        }

        // 6. KPI — ใช้ปีล่าสุดที่ครบปี ถ้าไม่มีจึงใช้ปีปัจจุบัน (ติด YTD)
        var baseItem = result.YearlyItems.FirstOrDefault(i => i.Year == baseYear);
        result.BaseYearTon = baseItem?.GrossTon ?? 0m;
        result.BaseYearHasData = result.BaseYearTon > 0m;

        var completeWithData = result.YearlyItems.Where(i => !i.IsYearToDate && i.GrossTon > 0m).ToList();
        var latest = completeWithData.LastOrDefault() ?? result.YearlyItems.LastOrDefault(i => i.GrossTon > 0m);
        if (latest != null)
        {
            result.LatestYear = latest.Year;
            result.LatestIsYearToDate = latest.IsYearToDate;
            result.LatestTon = latest.GrossTon;
        }

        if (result.BaseYearHasData)
        {
            result.TargetYearTon = TargetForYear(result.BaseYearTon, baseYear, targetYear, targetReductionPercent, targetYear);
            if (latest != null)
            {
                result.ReductionAchievedPercent = (result.BaseYearTon - latest.GrossTon) / result.BaseYearTon * 100m;
                result.LatestTargetTon = TargetForYear(result.BaseYearTon, baseYear, targetYear, targetReductionPercent, latest.Year);
                result.RequiredReductionPercentLatest = (1m - result.LatestTargetTon.Value / result.BaseYearTon) * 100m;
                result.GapToTargetTon = latest.GrossTon - result.TargetYearTon.Value;
                result.YearsLeft = Math.Max(0, targetYear - latest.Year);
                result.RequiredAnnualReductionPercent = RequiredAnnualReductionRate(latest.GrossTon, result.TargetYearTon.Value, result.YearsLeft.Value);
            }
        }

        // 7. Series ของกราฟ (ปีเรียงจากน้อยไปมาก เพื่อให้แกน Category ต่อกันถูกลำดับ)
        result.ActualSeries = result.YearlyItems
            .Select(i => new NzChartPoint { YearLabel = i.YearLabel, Value = (double)i.GrossTon })
            .ToList();

        if (result.BaseYearHasData)
        {
            var lastTargetYear = Math.Max(targetYear, lastDataYear);
            for (int y = baseYear; y <= lastTargetYear; y++)
            {
                result.TargetSeries.Add(new NzChartPoint
                {
                    YearLabel = Label(y),
                    Value = (double)TargetForYear(result.BaseYearTon, baseYear, targetYear, targetReductionPercent, y)
                });
            }
        }

        // เส้นคาดการณ์: เส้นตรง (least squares) จากปีที่ครบปีและมีข้อมูล ตั้งแต่ปีฐาน (ถ้าปีฐานมีข้อมูล)
        var fitPoints = completeWithData
            .Where(i => !result.BaseYearHasData || i.Year >= baseYear)
            .Select(i => (i.Year, i.GrossTon))
            .ToList();
        var fit = LinearFit(fitPoints);
        if (fit.HasValue && targetYear > fitPoints.Last().Year)
        {
            for (int y = fitPoints.Last().Year; y <= targetYear; y++)
            {
                var v = Math.Max(0m, fit.Value.Intercept + fit.Value.Slope * y);
                result.ProjectionSeries.Add(new NzChartPoint { YearLabel = Label(y), Value = (double)v });
            }
            result.ProjectedTargetYearTon = Math.Max(0m, fit.Value.Intercept + fit.Value.Slope * targetYear);
        }

        // 8. [2026-09-26][Claude] Carbon Neutral — เครดิตที่ต้องใช้ต่อปี = ยอด Gross ปีนั้น
        //    ปีที่ครบปีแล้ว = ค่าจริง; ปีหลังจากนั้นถึงปีเป้าหมาย = ค่าคาดการณ์ (ถ้ามีเส้นคาดการณ์)
        var lastCompleteYear = completeWithData.Any() ? completeWithData.Last().Year : (int?)null;
        foreach (var item in result.YearlyItems.Where(i => lastCompleteYear.HasValue && i.Year <= lastCompleteYear.Value))
        {
            result.CreditYearItems.Add(new NzCreditYearItem { Year = item.Year, YearLabel = item.YearLabel, ActualTon = (double)item.GrossTon });
        }
        if (fit.HasValue && lastCompleteYear.HasValue)
        {
            for (int y = lastCompleteYear.Value + 1; y <= targetYear; y++)
            {
                var v = Math.Max(0m, fit.Value.Intercept + fit.Value.Slope * y);
                result.CreditYearItems.Add(new NzCreditYearItem { Year = y, YearLabel = Label(y), ProjectedTon = (double)v });
            }
        }

        // 9. [2026-09-26][Claude] Net-Zero (SBTi) — ในปีเป้าหมาย ส่วนที่เหลือ (ไม่เกินระดับเป้า) ต้องหักล้างด้วยเครดิตแบบดูดกลับ
        //    ส่วนที่คาดการณ์เกินเป้า = ต้องลดเพิ่ม ใช้เครดิตแทนไม่ได้
        if (result.BaseYearHasData && result.TargetYearTon.HasValue)
        {
            var projectedAtTarget = result.ProjectedTargetYearTon;
            result.ResidualRemovalsTon = projectedAtTarget.HasValue
                ? Math.Min(projectedAtTarget.Value, result.TargetYearTon.Value)
                : result.TargetYearTon.Value;
            if (projectedAtTarget.HasValue)
            {
                result.AdditionalReductionGapTon = Math.Max(0m, projectedAtTarget.Value - result.TargetYearTon.Value);
            }

            if (fit.HasValue && lastCompleteYear.HasValue)
            {
                for (int y = lastCompleteYear.Value + 1; y <= targetYear; y++)
                {
                    var projected = Math.Max(0m, fit.Value.Intercept + fit.Value.Slope * y);
                    if (projected > TargetForYear(result.BaseYearTon, baseYear, targetYear, targetReductionPercent, y))
                    {
                        result.FirstYearAboveTarget = y;
                        break;
                    }
                }
            }
        }

        _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(3));
        return result;
    }
    #endregion

    #region pure calculation helpers (unit tested)
    /// <summary>เป้าหมายปี Y = ยอดปีฐาน × (1 − %ลด × min(1, (Y − ปีฐาน) ÷ (ปีเป้าหมาย − ปีฐาน))) — สูตรเดียวกับ Executive Dashboard</summary>
    public static decimal TargetForYear(decimal baseYearTon, int baseYear, int targetYear, decimal targetReductionPercent, int year)
    {
        decimal elapsed = Math.Max(0, year - baseYear);
        decimal total = Math.Max(1, targetYear - baseYear);
        var ratio = 1m - (targetReductionPercent / 100m) * Math.Min(1m, elapsed / total);
        return baseYearTon * ratio;
    }

    /// <summary>อัตราลดแบบทบต้นต่อปี (%) ที่ต้องทำจากยอดล่าสุดให้ถึงเป้าหมายใน yearsLeft ปี; ถึงเป้าแล้ว = 0; คำนวณไม่ได้ = null</summary>
    public static decimal? RequiredAnnualReductionRate(decimal latestTon, decimal targetTon, int yearsLeft)
    {
        if (latestTon <= 0m || targetTon <= 0m || yearsLeft <= 0) return null;
        if (latestTon <= targetTon) return 0m;
        var rate = 1d - Math.Pow((double)(targetTon / latestTon), 1d / yearsLeft);
        return (decimal)(rate * 100d);
    }

    /// <summary>เส้นตรง least squares; ต้องมีอย่างน้อย 2 ปีที่ต่างกัน</summary>
    public static (decimal Slope, decimal Intercept)? LinearFit(IReadOnlyList<(int Year, decimal Value)> points)
    {
        if (points == null || points.Count < 2) return null;
        decimal meanX = points.Average(p => (decimal)p.Year);
        decimal meanY = points.Average(p => p.Value);
        decimal sxx = points.Sum(p => ((decimal)p.Year - meanX) * ((decimal)p.Year - meanX));
        if (sxx == 0m) return null;
        decimal sxy = points.Sum(p => ((decimal)p.Year - meanX) * (p.Value - meanY));
        var slope = sxy / sxx;
        return (slope, meanY - slope * meanX);
    }
    #endregion
}
