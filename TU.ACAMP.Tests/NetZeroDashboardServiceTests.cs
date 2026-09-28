using System.Collections.Generic;
using TU.ACAMP.WebApp.Services.Dashboard;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>[2026-09-26][Claude] ทดสอบสูตรของ Net-Zero Pathway Dashboard (เป้าหมายเส้นตรง, อัตราลดทบต้น, เส้นคาดการณ์)</summary>
public class NetZeroDashboardServiceTests
{
    [Theory]
    [InlineData(2023, 1000)]  // ปีฐาน = 100%
    [InlineData(2026, 785.7142857142857142857142857)] // 3/7 ของทาง ลด 50% → 1 - 0.5*3/7
    [InlineData(2030, 500)]   // ปีเป้าหมาย = ลดครบ 50%
    [InlineData(2032, 500)]   // เลยปีเป้าหมาย คงที่
    [InlineData(2020, 1000)]  // ก่อนปีฐาน = 100%
    public void TargetForYear_LinearPathway(int year, double expected)
    {
        var v = NetZeroDashboardService.TargetForYear(1000m, 2023, 2030, 50m, year);
        Assert.Equal((decimal)expected, v, 6);
    }

    [Fact]
    public void RequiredAnnualReductionRate_Compound()
    {
        // 1000 → 500 ใน 1 ปี = 50%/ปี; ใน 2 ปี = 1 - sqrt(0.5) ≈ 29.29%
        Assert.Equal(50m, NetZeroDashboardService.RequiredAnnualReductionRate(1000m, 500m, 1)!.Value, 6);
        Assert.Equal(29.289322m, NetZeroDashboardService.RequiredAnnualReductionRate(1000m, 500m, 2)!.Value, 5);
    }

    [Fact]
    public void RequiredAnnualReductionRate_AlreadyAchieved_ReturnsZero_And_InvalidInput_ReturnsNull()
    {
        Assert.Equal(0m, NetZeroDashboardService.RequiredAnnualReductionRate(400m, 500m, 3));
        Assert.Null(NetZeroDashboardService.RequiredAnnualReductionRate(0m, 500m, 3));
        Assert.Null(NetZeroDashboardService.RequiredAnnualReductionRate(1000m, 500m, 0));
    }

    [Fact]
    public void LinearFit_ExactLine()
    {
        var fit = NetZeroDashboardService.LinearFit(new List<(int, decimal)> { (2023, 1000m), (2024, 900m), (2025, 800m) });
        Assert.NotNull(fit);
        Assert.Equal(-100m, fit!.Value.Slope, 6);
        Assert.Equal(700m, fit.Value.Intercept + fit.Value.Slope * 2026, 6);
    }

    [Fact]
    public void LinearFit_LessThanTwoPoints_ReturnsNull()
    {
        Assert.Null(NetZeroDashboardService.LinearFit(new List<(int, decimal)> { (2023, 1000m) }));
    }
}
