// [2026-09-17][Gemini] Independent DTO models for Executive Dashboard V2
using System;
using System.Collections.Generic;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public static class Dashboard2Colors
{
    public const string Total = "#0f766e";
    public const string Scope1 = "#10b981";
    public const string Scope2 = "#3b82f6";
    public const string Scope3 = "#f97316";
    public const string Biogenic = "#8b5cf6";
    public const string Target = "#f59e0b";
    public const string TargetMet = "#10b981";
    // [2026-09-25][Gemini] สีส้มสำหรับค่าเข้าใกล้เส้น limit (>80%) ของ Net-Zero Pathway
    public const string TargetWarning = "#f97316";
    public const string TargetExceeded = "#e11d48";
    public const string BaseYear = "#cbd5e1";

    // [2026-09-26][Claude] Monthly GHG Trajectory: แท่ง Base Year วางทับ "ด้านหน้า" แท่ง Actual แบบโปร่งใส
    // Radzen ไม่มี overlap ในตัว จึงคำนวณสีที่ได้จากการซ้อนสีโปร่ง (alpha blending) ไว้ล่วงหน้า แล้ววาดด้วย stack จำลอง
    public const double BaseYearOverlayAlpha = 0.5;
    /// <summary>ส่วน Base ที่สูงกว่า Actual (Base โปร่งบนพื้นขาว)</summary>
    public static readonly string BaseYearOverlay = Blend(BaseYear, BaseYearOverlayAlpha, "#ffffff");
    /// <summary>ส่วนที่ Base ทับ Actual (Base โปร่งบนสีสถานะ Actual)</summary>
    public static readonly string OverlapTargetMet = Blend(BaseYear, BaseYearOverlayAlpha, TargetMet);
    public static readonly string OverlapTargetWarning = Blend(BaseYear, BaseYearOverlayAlpha, TargetWarning);
    public static readonly string OverlapTargetExceeded = Blend(BaseYear, BaseYearOverlayAlpha, TargetExceeded);

    /// <summary>สีผลลัพธ์ของการวาด front (alpha) ทับ back: out = a*front + (1-a)*back</summary>
    private static string Blend(string frontHex, double alpha, string backHex)
    {
        static (int R, int G, int B) Rgb(string hex) =>
            (Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16));
        var f = Rgb(frontHex);
        var b = Rgb(backHex);
        int Mix(int fc, int bc) => (int)Math.Round(alpha * fc + (1 - alpha) * bc);
        return $"#{Mix(f.R, b.R):x2}{Mix(f.G, b.G):x2}{Mix(f.B, b.B):x2}";
    }
    public const string Hotspot = "#ef4444";
    public const string Facility = "#3b82f6";
}

public class ScopeChartItem2
{
    #region class properties
    public string ScopeName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class MonthlyTrendItem2
{
    #region class properties
    public string MonthName { get; set; } = string.Empty;
    public double ActualAmount { get; set; }
    public double BaseYearAmount { get; set; }
    public double NetZeroTargetAmount { get; set; }
    // [2026-09-25][Gemini] สัดส่วนเกณฑ์แจ้งเตือน (Default: 0.8 = 80%)
    public double WarningThresholdRatio { get; set; } = 0.8;

    // [2026-09-25][Antigravity] Monthly scope amounts for multi-scope trajectory & sparkline analysis
    public double Scope1Amount { get; set; }
    public double Scope2Amount { get; set; }
    public double Scope3Amount { get; set; }

    // [2026-09-25][Gemini] สีเขียว: ปล่อยต่ำกว่าหรือเท่ากับ 80% ของเส้น Net-Zero Limit (Met Target)
    public double ActualMetTargetAmount => ActualAmount > 0 && (NetZeroTargetAmount <= 0 || ActualAmount <= NetZeroTargetAmount * WarningThresholdRatio) ? ActualAmount : 0.0;
    // [2026-09-25][Gemini] สีส้ม: ปล่อยเข้าใกล้เส้น limit (> 80% และ <= 100% ของ Net-Zero Pathway)
    public double ActualWarningTargetAmount => NetZeroTargetAmount > 0 && ActualAmount > NetZeroTargetAmount * WarningThresholdRatio && ActualAmount <= NetZeroTargetAmount ? ActualAmount : 0.0;
    // [2026-09-25][Gemini] สีแดง: ปล่อยเกินกว่า 100% ของเส้น Net-Zero Pathway
    public double ActualExceededTargetAmount => NetZeroTargetAmount > 0 && ActualAmount > NetZeroTargetAmount ? ActualAmount : 0.0;
    // [2026-09-26][Claude] Overlay (Base อยู่ด้านหน้า Actual, เริ่มจาก 0 ทั้งคู่) จำลองด้วย stack 3 ชั้น:
    //   1) Overlap  = min(Base, Actual)   → สี Base โปร่งทับสีสถานะ Actual
    //   2) Excess   = max(Actual - Base,0) → สีสถานะ Actual ล้วน (Actual สูงกว่า Base)
    //   3) Remainder= max(Base - Actual,0) → สี Base โปร่งบนพื้นขาว (Base สูงกว่า Actual)
    // ชั้น 2 กับ 3 มีค่าได้อย่างใดอย่างหนึ่งเท่านั้น → ยอดแท่ง = max(Base, Actual)
    private double OverlapAmount => Math.Min(BaseYearAmount, ActualAmount);
    private double ExcessAmount => Math.Max(ActualAmount - BaseYearAmount, 0.0);

    public double OverlapMetAmount => ActualMetTargetAmount > 0 ? OverlapAmount : 0.0;
    public double OverlapWarningAmount => ActualWarningTargetAmount > 0 ? OverlapAmount : 0.0;
    public double OverlapExceededAmount => ActualExceededTargetAmount > 0 ? OverlapAmount : 0.0;

    public double ExcessMetAmount => ActualMetTargetAmount > 0 ? ExcessAmount : 0.0;
    public double ExcessWarningAmount => ActualWarningTargetAmount > 0 ? ExcessAmount : 0.0;
    public double ExcessExceededAmount => ActualExceededTargetAmount > 0 ? ExcessAmount : 0.0;

    public double BaseYearRemainderAmount => Math.Max(BaseYearAmount - ActualAmount, 0.0);
    #endregion
}

public class HotspotChartItem2
{
    #region class properties
    public string GroupName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class FacilityChartItem2
{
    #region class properties
    public string FacilityName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class HotspotRow2
{
    #region class properties
    public int Rank { get; set; }
    public int TopicId { get; set; }
    public string TopicKey { get; set; } = string.Empty;
    public string ActivityTopic { get; set; } = string.Empty;
    public string ActivityGroup { get; set; } = string.Empty;
    public string ScopeDisplay { get; set; } = string.Empty;
    public decimal TotalKg { get; set; }
    public decimal TotalTon => TotalKg / 1000m;
    public decimal SharePercent { get; set; }
    public eTopicLevel Level { get; set; } = eTopicLevel.Corporate;
    public int TargetId { get; set; }
    #endregion
}

public class ProblemTopicItem2
{
    #region class properties
    public int TopicId { get; set; }
    public string TopicKey { get; set; } = string.Empty;
    public string TopicTitle { get; set; } = string.Empty;
    public string ScopeDisplay { get; set; } = string.Empty;
    public eTopicLevel Level { get; set; } = eTopicLevel.Corporate;
    public string TargetTitle { get; set; } = string.Empty;
    #endregion
}

public class ProblemEfItem2
{
    #region class properties
    public int SourceId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public decimal EF { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    #endregion
}

public class DataQualitySummary2
{
    #region class properties
    public int InScopeTopicsCount { get; set; }
    public int TopicsWithDataCount { get; set; }
    public int TopicsWithoutDataCount => MissingDataTopics.Count;
    public int ExpiredEfCount => ExpiredEfs.Count;
    public List<ProblemTopicItem2> MissingDataTopics { get; set; } = new();
    public List<ProblemEfItem2> ExpiredEfs { get; set; } = new();
    #endregion
}

// [2026-09-25][Antigravity] DTO for Scope 3 15 standard value chain categories (GHG Protocol / ISO 14064-1)
public class Scope3CategoryItem2
{
    public int CategoryNo { get; set; }
    public string CategoryTitle { get; set; } = string.Empty;
    public decimal EmissionsTon { get; set; }
    public decimal SharePercent { get; set; }
    public bool HasData => EmissionsTon > 0m;
}

public class ExecutiveDashboardResult2
{
    #region class properties
    public decimal TotalEmissions { get; set; }
    public decimal Scope1Emissions { get; set; }
    public decimal Scope2Emissions { get; set; }
    public decimal Scope3Emissions { get; set; }
    public decimal SeparateEmissions { get; set; }

    public decimal Scope1Share { get; set; }
    public decimal Scope2Share { get; set; }
    public decimal Scope3Share { get; set; }

    // [2026-09-25][Antigravity] Gross vs Net Emissions & Carbon Offsets / RECs (ISO 14064-1 §5.2.3 compliant)
    public decimal GrossEmissions => TotalEmissions;
    public decimal CarbonOffsets { get; set; }
    public decimal NetEmissions => Math.Max(0m, GrossEmissions - CarbonOffsets);
    public decimal OffsetRatioPercent => GrossEmissions > 0m ? Math.Min(100m, (CarbonOffsets / GrossEmissions) * 100m) : 0m;

    public decimal? YoYChangePercent { get; set; }
    public bool BaseYearHasData { get; set; } = true;

    // Emission Intensity metrics
    public decimal? ProductionAmount { get; set; }
    public string? ProductionUnit { get; set; }
    public decimal? EmissionIntensity { get; set; }

    // Data Quality and Coverage
    public DataQualitySummary2 DataQuality { get; set; } = new();

    // Timestamp
    public DateTime LastUpdatedAt { get; set; } = AppTimeZone.LocalNow;

    public List<int> AvailableYears { get; set; } = new();
    public List<TbCorporate> Corporates { get; set; } = new();
    public List<TbCompany> Companies { get; set; } = new();
    public List<TbFacility> Facilities { get; set; } = new();

    public List<ScopeChartItem2> ScopeDistributionData { get; set; } = new();
    public List<MonthlyTrendItem2> MonthlyTrendData { get; set; } = new();
    public List<HotspotChartItem2> TopHotspotsData { get; set; } = new();
    public List<FacilityChartItem2> FacilityComparisonData { get; set; } = new();
    public List<HotspotRow2> TopHotspotsTable { get; set; } = new();

    // [2026-09-25][Antigravity] 12-Month Sparkline trend sequences for responsive KPI cards
    public List<double> MonthlyTotalSparkline { get; set; } = new();
    public List<double> MonthlyScope1Sparkline { get; set; } = new();
    public List<double> MonthlyScope2Sparkline { get; set; } = new();
    public List<double> MonthlyScope3Sparkline { get; set; } = new();

    // [2026-09-25][Antigravity] Scope 3 Standard 15 Categories Breakdown
    public List<Scope3CategoryItem2> Scope3CategoriesData { get; set; } = new();
    #endregion
}
