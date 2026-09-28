using TU.ACAMP.Domain.Enums;

namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-24][Claude] เอกสารการเปิดเผยข้อมูลด้านสภาพภูมิอากาศตาม IFRS S2 (ISSB) สำหรับแบบ 56-1 One Report
/// ตัวเลขทั้งหมดคัดลอกจาก CFO Report snapshot ที่ Verified / Locked (SourceCfoReportKey) ไม่คำนวณใหม่
/// เก็บเป็น JSON ในโฟลเดอร์เดียวกับ snapshot ต้นทาง
/// </summary>
public class IssbClimateDisclosureDocument
{
    #region source
    public string SourceCfoReportKey { get; set; } = string.Empty;
    public string SourceCfoReportTitle { get; set; } = string.Empty;
    public eTopicLevel Level { get; set; }
    public int TargetId { get; set; }
    public string OrganizationTitle { get; set; } = string.Empty;
    public int ReportingYear { get; set; }
    public int BaseYear { get; set; }
    public string PeriodDescription { get; set; } = string.Empty;
    /// <summary>[2026-09-24][Claude] เวลาที่สร้าง CFO snapshot ต้นทาง</summary>
    public DateTimeOffset SourceCreatedAt { get; set; }
    /// <summary>[2026-09-24][Claude] true = snapshot สร้างก่อนสิ้นปีที่รายงาน ข้อมูลอาจไม่ครบ 12 เดือน</summary>
    public bool IsPartialYear { get; set; }
    #endregion

    #region narratives (ผู้ใช้กรอก)
    public string Governance { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string RiskManagement { get; set; } = string.Empty;
    public string MetricsTargetsNarrative { get; set; } = string.Empty;
    #endregion

    #region inputs (ผู้ใช้กรอก)
    /// <summary>รายได้ปีที่รายงาน (ล้านบาท) ใช้คำนวณ GHG intensity (null = ไม่แสดง)</summary>
    public decimal? RevenueMillionThb { get; set; }
    /// <summary>รายได้ปีฐาน (ล้านบาท)</summary>
    public decimal? BaseRevenueMillionThb { get; set; }
    public string MeasurementApproach { get; set; } = IssbClimateDisclosureService.DefaultMeasurementApproach;
    public string VerifierName { get; set; } = string.Empty;
    public string AssuranceLevel { get; set; } = "Limited assurance";
    #endregion

    #region metrics (คัดลอกจาก CFO snapshot)
    public string ConsolidationApproach { get; set; } = string.Empty;
    public string GwpVersion { get; set; } = string.Empty;
    public IssbScopeTotals Reporting { get; set; } = new();
    public IssbScopeTotals? Base { get; set; }
    public List<IssbCategoryLine> Scope3Categories { get; set; } = new();
    public List<IssbCategoryLine> SeparateCategories { get; set; } = new();
    public IssbNetZeroTarget? NetZeroTarget { get; set; }
    #endregion

    #region audit
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    #endregion
}

/// <summary>ยอดรวมรายขอบเขต (tCO2e) — Separate = Biogenic CO2 / ก๊าซนอกพิธีสารเกียวโต รายงานแยก ไม่รวมใน Gross</summary>
public class IssbScopeTotals
{
    public decimal Scope1 { get; set; }
    public decimal Scope2 { get; set; }
    public decimal Scope3 { get; set; }
    public decimal Separate { get; set; }
    public decimal Scope1And2 => Scope1 + Scope2;
    public decimal Gross => Scope1 + Scope2 + Scope3;
}

public class IssbCategoryLine
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal Total_tCO2e { get; set; }
}

public class IssbNetZeroTarget
{
    public int BaseYear { get; set; }
    public int TargetYear { get; set; }
    public decimal TargetReductionPercent { get; set; }
}
