using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

/// <summary>
/// Thread-safe atomic rate limiter and monthly quota manager for API ingestion.
///
/// ขอบเขตที่ต้องรู้: ตัวนับเก็บอยู่ในหน่วยความจำของ process นี้เท่านั้น (in-process)
/// ถ้า deploy หลาย instance แต่ละ instance จะนับแยกกัน โควตาจริงที่ client ได้
/// จึงเท่ากับ limit x จำนวน instance หากต้องการ limit ระดับคลัสเตอร์จริง
/// ต้องย้ายตัวนับไปไว้ที่ store ส่วนกลาง (เช่น Redis) ซึ่งอยู่นอกขอบเขตของคลาสนี้
/// </summary>
public class PlanRateLimiterService
{
    private class RateCounter
    {
        public long TimestampWindow;
        public long Count;
    }

    private static readonly ConcurrentDictionary<string, RateCounter> RateMap = new();
    private static long _lastSweptWindow = -1;

    public class RateLimitCheckResult
    {
        public bool IsAllowed { get; set; }
        public bool IsQuotaExceeded { get; set; }
        public int RetryAfterSeconds { get; set; } = 60;
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Atomically checks and consumes rate limit counter for an API key / subscription.
    /// </summary>
    public RateLimitCheckResult CheckRateLimit(string keyPrefix, int limitPerMin)
    {
        #region hide [2026-08-24]
        if (limitPerMin <= 0) limitPerMin = 60;

        var currentWindow = AppTimeZone.UtcNow.Ticks / TimeSpan.TicksPerMinute; // 1-minute window

        // [2026-09-13][Claude] ล้างตัวนับที่หมดอายุทิ้งเมื่อขึ้นนาทีใหม่
        //     ของเดิมไม่เคยลบ key ออกจาก RateMap เลย ทุก KeyPrefix ที่เคยเรียก API
        //     จะค้างในหน่วยความจำตลอดอายุ process (รั่วช้า ๆ ตามจำนวน API Key ที่เคยใช้งาน)
        PruneStaleCounters(currentWindow);

        var counter = RateMap.GetOrAdd(keyPrefix, _ => new RateCounter { TimestampWindow = currentWindow, Count = 0 });

        lock (counter)
        {
            if (counter.TimestampWindow != currentWindow)
            {
                counter.TimestampWindow = currentWindow;
                counter.Count = 0;
            }

            if (counter.Count >= limitPerMin)
            {
                var secondsLeft = 60 - (AppTimeZone.UtcNow.Second);
                return new RateLimitCheckResult
                {
                    IsAllowed = false,
                    RetryAfterSeconds = secondsLeft > 0 ? secondsLeft : 1,
                    ErrorCode = "rate_limited",
                    ErrorMessage = $"Rate limit of {limitPerMin} requests/minute exceeded. Try again in {secondsLeft} seconds."
                };
            }

            counter.Count++;
            return new RateLimitCheckResult { IsAllowed = true };
        }
        #endregion
    }

    /// <summary>
    /// ลบตัวนับที่เก่ากว่าหน้าต่างปัจจุบันเกิน 1 นาทีออกจาก RateMap
    /// กวาดมากที่สุดนาทีละครั้งเพื่อไม่ให้กระทบ throughput ของ request ปกติ
    /// </summary>
    private static void PruneStaleCounters(long currentWindow)
    {
        if (Interlocked.Exchange(ref _lastSweptWindow, currentWindow) == currentWindow) return;

        foreach (var entry in RateMap)
        {
            if (entry.Value.TimestampWindow < currentWindow - 1)
            {
                RateMap.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// Checks monthly quota for a subscription from tb_event_staging.
    /// </summary>
    public async Task<bool> CheckMonthlyQuotaAsync(ApplicationDbContext db, int subscriptionId, int maxMonthlyEvents)
    {
        #region hide [2026-08-24]
        if (maxMonthlyEvents <= 0) return true;

        var currentPeriodStart = AppTimeZone.ToReportingPeriod(AppTimeZone.UtcNow);
        var currentMonthCount = await db.EventStagings
            .AsNoTracking()
            .CountAsync(s => s.SubscriptionId == subscriptionId &&
                             s.CreatedAt >= currentPeriodStart &&
                             s.Status != "Rejected");

        return currentMonthCount < maxMonthlyEvents;
        #endregion
    }
}
