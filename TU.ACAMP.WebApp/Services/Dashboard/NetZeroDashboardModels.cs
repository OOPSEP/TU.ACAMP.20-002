// [2026-09-26][Claude] DTO ของ Net-Zero Pathway Dashboard (D2) — แยกจาก DTO ของ Executive Dashboard
using System;
using System.Collections.Generic;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.WebApp.Services.Dashboard;

/// <summary>ยอดปล่อยรายปี (Gross = Scope 1+2+3 ไม่รวม Separate/Biogenic)</summary>
public class NzYearlyItem
{
    public int Year { get; set; }
    public string YearLabel { get; set; } = string.Empty;
    public bool IsYearToDate { get; set; }
    public decimal Scope1Ton { get; set; }
    public decimal Scope2Ton { get; set; }
    public decimal Scope3Ton { get; set; }
    public decimal GrossTon => Scope1Ton + Scope2Ton + Scope3Ton;
    // ค่า double สำหรับผูกกับ RadzenChart
    public double Scope1Value => (double)Scope1Ton;
    public double Scope2Value => (double)Scope2Ton;
    public double Scope3Value => (double)Scope3Ton;
}

/// <summary>[2026-09-26][Claude] ปริมาณเครดิตที่ต้องใช้ต่อปีเพื่ออ้าง Carbon Neutral (= ยอด Gross ของปีนั้น) แยกค่าจริง/ค่าคาดการณ์</summary>
public class NzCreditYearItem
{
    public int Year { get; set; }
    public string YearLabel { get; set; } = string.Empty;
    public double ActualTon { get; set; }
    public double ProjectedTon { get; set; }
}

/// <summary>จุดข้อมูลของกราฟ (Category = ปี)</summary>
public class NzChartPoint
{
    public string YearLabel { get; set; } = string.Empty;
    public double Value { get; set; }
}

public class NetZeroDashboardResult
{
    public DateTime LastUpdatedAt { get; set; } = AppTimeZone.LocalNow;
    public List<TbCorporate> Corporates { get; set; } = new();
    public List<TbCompany> Companies { get; set; } = new();
    public List<TbFacility> Facilities { get; set; } = new();

    public int BaseYear { get; set; }
    public int TargetYear { get; set; }
    public decimal TargetReductionPercent { get; set; }

    public List<NzYearlyItem> YearlyItems { get; set; } = new();

    // KPI
    public bool BaseYearHasData { get; set; }
    public decimal BaseYearTon { get; set; }
    public int? LatestYear { get; set; }
    public bool LatestIsYearToDate { get; set; }
    public decimal LatestTon { get; set; }
    public decimal? ReductionAchievedPercent { get; set; }
    public decimal? RequiredReductionPercentLatest { get; set; }
    public decimal? LatestTargetTon { get; set; }
    public decimal? TargetYearTon { get; set; }
    public decimal? GapToTargetTon { get; set; }
    public int? YearsLeft { get; set; }
    public decimal? RequiredAnnualReductionPercent { get; set; }

    // Chart series
    public List<NzChartPoint> ActualSeries { get; set; } = new();
    public List<NzChartPoint> TargetSeries { get; set; } = new();
    public List<NzChartPoint> ProjectionSeries { get; set; } = new();
    public decimal? ProjectedTargetYearTon { get; set; }

    // [2026-09-26][Claude] Carbon Neutral: เครดิตที่ต้องใช้ต่อปี (ค่าจริงถึงปีที่ครบปีล่าสุด, ต่อจากนั้นเป็นค่าคาดการณ์)
    public List<NzCreditYearItem> CreditYearItems { get; set; } = new();

    // [2026-09-26][Claude] Net-Zero (SBTi): ส่วนที่เหลือที่ต้องใช้เครดิตแบบดูดกลับ (removal) ในปีเป้าหมาย
    public const decimal SbtiMinLongTermReductionPercent = 90m;
    public bool IsSbtiLongTermAligned => TargetReductionPercent >= SbtiMinLongTermReductionPercent;
    public decimal? ResidualRemovalsTon { get; set; }
    public decimal? AdditionalReductionGapTon { get; set; }
    public int? FirstYearAboveTarget { get; set; }
}
