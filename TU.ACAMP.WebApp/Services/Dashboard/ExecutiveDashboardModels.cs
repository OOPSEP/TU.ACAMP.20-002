using System;
using System.Collections.Generic;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Organization;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class ScopeChartItem
{
    #region class properties
    public string ScopeName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class MonthlyTrendItem
{
    #region class properties
    public string MonthName { get; set; } = string.Empty;
    public double ActualAmount { get; set; }
    public double BaseYearAmount { get; set; }
    public double NetZeroTargetAmount { get; set; }
    // [2026-09-25][Gemini] สัดส่วนเกณฑ์แจ้งเตือน (Default: 0.8 = 80%)
    public double WarningThresholdRatio { get; set; } = 0.8;

    // [2026-09-25][Gemini] สีเขียว: ปล่อยต่ำกว่าหรือเท่ากับ 80% ของเส้น Net-Zero Limit (Met Target)
    public double ActualMetTargetAmount => ActualAmount > 0 && (NetZeroTargetAmount <= 0 || ActualAmount <= NetZeroTargetAmount * WarningThresholdRatio) ? ActualAmount : 0.0;
    // [2026-09-25][Gemini] สีส้ม: ปล่อยเข้าใกล้เส้น limit (> 80% และ <= 100% ของ Net-Zero Pathway)
    public double ActualWarningTargetAmount => NetZeroTargetAmount > 0 && ActualAmount > NetZeroTargetAmount * WarningThresholdRatio && ActualAmount <= NetZeroTargetAmount ? ActualAmount : 0.0;
    // [2026-09-25][Gemini] สีแดง: ปล่อยเกินกว่า 100% ของเส้น Net-Zero Pathway
    public double ActualExceededTargetAmount => NetZeroTargetAmount > 0 && ActualAmount > NetZeroTargetAmount ? ActualAmount : 0.0;
    #endregion
}

public class HotspotChartItem
{
    #region class properties
    public string GroupName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class FacilityChartItem
{
    #region class properties
    public string FacilityName { get; set; } = string.Empty;
    public double Amount { get; set; }
    #endregion
}

public class HotspotRow
{
    #region class properties
    public int Rank { get; set; }
    public int TopicId { get; set; }
    public string ActivityTopic { get; set; } = string.Empty;
    public string ActivityGroup { get; set; } = string.Empty;
    public string ScopeDisplay { get; set; } = string.Empty;
    public decimal TotalKg { get; set; }
    public decimal TotalTon => TotalKg / 1000m;
    public decimal SharePercent { get; set; }
    #endregion
}

public class ExecutiveDashboardResult
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

    public decimal? YoYChangePercent { get; set; }
    public bool BaseYearHasData { get; set; } = true;

    public List<int> AvailableYears { get; set; } = new();
    public List<TbCorporate> Corporates { get; set; } = new();

    public List<ScopeChartItem> ScopeDistributionData { get; set; } = new();
    public List<MonthlyTrendItem> MonthlyTrendData { get; set; } = new();
    public List<HotspotChartItem> TopHotspotsData { get; set; } = new();
    public List<FacilityChartItem> FacilityComparisonData { get; set; } = new();
    public List<HotspotRow> TopHotspotsTable { get; set; } = new();
    #endregion
}
