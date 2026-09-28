using System.Collections.Generic;
using System.Linq;
using TU.ACAMP.WebApp.Services.Activity;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>[2026-09-24][Claude] ทดสอบนิยาม Hotspot แบบ Pareto (รายการที่รวมกันได้ถึง 70%)</summary>
public class HotspotAnalyzerTests
{
    private static Dictionary<string, HotspotRank> Run(params (string Key, decimal Value)[] items) =>
        HotspotAnalyzer.Analyze(items, x => x.Key, x => x.Value);

    [Fact]
    public void Analyze_IncludesItemThatCrossesCutoff()
    {
        // 50 + 15 = 65% (< 70) -> ต้องรวม C (10%) ที่ทำให้ข้าม 70% ด้วย
        var result = Run(("A", 50m), ("B", 15m), ("C", 10m), ("D", 9m), ("E", 8m), ("F", 8m));

        Assert.Equal(new[] { "A", "B", "C" }, result.OrderBy(r => r.Value.Rank).Select(r => r.Key));
        Assert.Equal(75m, result["C"].CumulativePercent);
        Assert.Equal(3, result["C"].Rank);
    }

    [Fact]
    public void Analyze_SingleDominantItem_IsOnlyHotspot()
    {
        var result = Run(("Electricity", 80m), ("LPG", 12m), ("Water", 8m));

        Assert.Single(result);
        Assert.True(result.ContainsKey("Electricity"));
    }

    [Fact]
    public void Analyze_ExactlyReachesCutoff_StopsThere()
    {
        var result = Run(("A", 40m), ("B", 30m), ("C", 30m));

        Assert.Equal(2, result.Count);
        Assert.Equal(70m, result["B"].CumulativePercent);
    }

    [Fact]
    public void Analyze_IgnoresZeroItemsAndEmptyTotal()
    {
        Assert.Empty(Run(("A", 0m), ("B", 0m)));

        var result = Run(("A", 0m), ("B", 10m));
        Assert.Single(result);
        Assert.Equal(100m, result["B"].CumulativePercent);
    }

    [Fact]
    public void Analyze_Seed7Year2025AllScopes_MatchesExpectedHotspots()
    {
        // ยอดปี 2025 (tCO2e) จากหน้า Corporate Level Analytics ของข้อมูลจำลอง DatabaseSeeder7
        var result = Run(
            ("0101", 395.33m), ("0102", 429.73m), ("0103", 0.05m), ("0104", 78.18m), ("0201", 1945.85m),
            ("0301", 31.17m), ("0303", 449.83m), ("0304", 47.52m), ("0305", 90.65m),
            ("0401", 0.03m), ("0402", 2.52m), ("0403", 31.50m));

        // 0201 = 55.6%, + 0303 = 68.4%, + 0102 = 80.6% -> Hotspot 3 กลุ่ม
        Assert.Equal(new[] { "0201", "0303", "0102" }, result.OrderBy(r => r.Value.Rank).Select(r => r.Key));
    }
}
