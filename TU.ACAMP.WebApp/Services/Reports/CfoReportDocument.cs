using System;
using System.Text.Json.Serialization;
using TU.ACAMP.Domain.Enums;

namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// Immutable verification report snapshot document stored as JSON on disk
/// </summary>
public class CfoReportDocument
{
    public string ReportKey { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public eTopicLevel Level { get; set; } = eTopicLevel.Corporate;
    public int TargetId { get; set; }
    public string OrganizationTitle { get; set; } = string.Empty;
    public string OrganizationCode { get; set; } = string.Empty;
    // [2026-09-15][Antigravity] Fr-01 and Fr-03.2 custom metadata overrides for audit trail
    public string GhgTeamName { get; set; } = string.Empty;
    public string CertifierOrgTitle { get; set; } = string.Empty;
    public List<int> Scope3CheckedCategories { get; set; } = new();

    // [2026-09-16][Antigravity] Fr-01 Full Sheet Metadata for Snapshot Persistence
    public DateTime? PreparationDate { get; set; }
    public string? OrganizationImageBase64 { get; set; }
    public string? CalculatePeriodText { get; set; }
    public decimal? CalculateProductionAmount { get; set; }
    public string? CalculateProductionUnit { get; set; }
    public List<CfoReportService.CfoProductItemDto>? CalcProductItems { get; set; }
    public string? BasePeriodText { get; set; }
    public decimal? BaseProductionAmount { get; set; }
    public string? BaseProductionUnit { get; set; }
    public List<CfoReportService.CfoProductItemDto>? BaseProductItems { get; set; }
    public string? OrgInfoMultiLine { get; set; }
    public string? OrgInfo1 { get; set; }
    public string? OrgInfo2 { get; set; }
    public string? OrgInfo3 { get; set; }
    public string? OrgInfo4 { get; set; }
    public string? OrgInfo5 { get; set; }
    public string? ContactAddress { get; set; }
    public DateTime? RegistrationDate { get; set; }

    // [2026-09-18][Claude] Fr-02 / Fr-03.1 / Fr-03.2 overrides — เก็บไว้ให้ snapshot สร้างไฟล์เดิมซ้ำได้
    public string? Fr02ImageBase64 { get; set; }
    public DateTime? Fr02DiagramDate { get; set; }
    public string? Fr031ImageBase64 { get; set; }
    public DateTime? Fr031DiagramDate { get; set; }
    public DateTime? Fr031CompletionDate { get; set; }
    public List<CfoReportService.CfoFr032CategoryDto>? Fr032Categories { get; set; }

    // [2026-09-16][Antigravity] In-Scope Organizational Boundary (Boundary Inclusion: e.g. 2 out of 5 companies)
    public List<int>? InScopeChildIds { get; set; }
    public List<string>? InScopeChildTitles { get; set; }

    public int BaseYear { get; set; }
    public int CalculateYear { get; set; }
    public string PeriodDescription { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "Verified / Locked";

    public CfoReportService.CfoAuditReportDto CalculateYearReport { get; set; } = new();
    public CfoReportService.CfoAuditReportDto? BaseYearReport { get; set; }

    public bool HasExcelSnapshot { get; set; }
    public bool HasWordSnapshot { get; set; }
    public string GwpVersion { get; set; } = "IPCC AR5 (2014)";
    public MultiYearComparisonDto? Comparison { get; set; }
}

/// <summary>
/// Multi-year progress and alignment comparison between Calculate Year and Base Year (M-08)
/// </summary>
public class MultiYearComparisonDto
{
    public decimal Scope1Delta_tCO2e { get; set; }
    public decimal Scope1PercentChange { get; set; }

    public decimal Scope2Delta_tCO2e { get; set; }
    public decimal Scope2PercentChange { get; set; }

    public decimal Scope3Delta_tCO2e { get; set; }
    public decimal Scope3PercentChange { get; set; }

    public decimal GrossDelta_tCO2e { get; set; }
    public decimal GrossPercentChange { get; set; }

    public decimal SeparateDelta_tCO2e { get; set; }
    public decimal SeparatePercentChange { get; set; }
}

/// <summary>
/// Lightweight metadata item for listing in the Report Registry
/// </summary>
public class CfoReportSummaryItem
{
    public string ReportKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public eTopicLevel Level { get; set; }
    public int TargetId { get; set; }
    public string OrganizationTitle { get; set; } = string.Empty;
    public string OrganizationCode { get; set; } = string.Empty;

    public int BaseYear { get; set; }
    public int CalculateYear { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "Verified / Locked";

    public decimal TotalScope1_tCO2e { get; set; }
    public decimal TotalScope2_tCO2e { get; set; }
    public decimal TotalScope3_tCO2e { get; set; }
    public decimal TotalGrossEmissions_tCO2e { get; set; }

    public bool HasExcelFile { get; set; }
    public bool HasWordFile { get; set; }
    public string FileDirectory { get; set; } = string.Empty;
}
