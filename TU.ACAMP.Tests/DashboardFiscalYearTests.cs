using TU.ACAMP.WebApp.Services.Dashboard;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>[2026-09-26][Claude] ทดสอบการแปลงเดือนปฏิทินเป็นปีบัญชีของ Executive Dashboard V2 (ปีบัญชีตั้งชื่อตามปีที่เริ่ม)</summary>
public class DashboardFiscalYearTests
{
    [Theory]
    [InlineData(2026, 1, 1, 2026, 1)]   // ปีปฏิทิน: ม.ค. = เดือนที่ 1
    [InlineData(2026, 12, 1, 2026, 12)] // ปีปฏิทิน: ธ.ค. = เดือนที่ 12
    [InlineData(2026, 4, 4, 2026, 1)]   // เริ่ม เม.ย.: เม.ย. 2026 = FY2026 เดือนที่ 1
    [InlineData(2027, 3, 4, 2026, 12)]  // เริ่ม เม.ย.: มี.ค. 2027 = FY2026 เดือนที่ 12
    [InlineData(2026, 3, 4, 2025, 12)]  // เริ่ม เม.ย.: มี.ค. 2026 = FY2025 เดือนที่ 12
    [InlineData(2026, 10, 10, 2026, 1)] // เริ่ม ต.ค.
    [InlineData(2027, 9, 10, 2026, 12)]
    public void ToFiscalPeriod_MapsCalendarMonthToFiscalYearAndPosition(int year, int month, int startMonth, int expectedFy, int expectedPos)
    {
        var (fy, pos) = ExecutiveDashboardService2.ToFiscalPeriod(year, month, startMonth);

        Assert.Equal(expectedFy, fy);
        Assert.Equal(expectedPos, pos);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void ToFiscalPeriod_InvalidStartMonth_FallsBackToCalendarYear(int startMonth)
    {
        var (fy, pos) = ExecutiveDashboardService2.ToFiscalPeriod(2026, 5, startMonth);

        Assert.Equal(2026, fy);
        Assert.Equal(5, pos);
    }

    [Theory]
    [InlineData(2026, 1, "Jan 2026 - Dec 2026")]
    [InlineData(2026, 4, "Apr 2026 - Mar 2027")]
    public void FormatFiscalPeriod_ReturnsStartAndEndMonth(int fiscalYear, int startMonth, string expected)
    {
        Assert.Equal(expected, ExecutiveDashboardService2.FormatFiscalPeriod(fiscalYear, startMonth));
    }
}
