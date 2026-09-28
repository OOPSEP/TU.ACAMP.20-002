using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-24][Claude] สร้าง / บันทึก / ออกไฟล์ Word ของการเปิดเผยข้อมูลด้านสภาพภูมิอากาศตาม IFRS S2 (ISSB)
/// - ตัวเลขดึงจาก CFO Report snapshot ที่ Verified / Locked เสมอ (snapshot แก้ไม่ได้ ตัวเลขจึงคงที่)
/// - ข้อความเชิงบรรยาย / รายได้ / ผู้ทวนสอบ เก็บใน issb_climate_disclosure.json ในโฟลเดอร์ snapshot
/// - โครงเอกสาร 4 หัวข้อ: Governance / Strategy / Risk Management / Metrics and Targets
/// </summary>
public class IssbClimateDisclosureService
{
    #region constants
    public const string JsonFileName = "issb_climate_disclosure.json";
    public const string WordFileName = "issb_climate_disclosure.docx";
    private const string FontName = "TH Sarabun New";

    public const string DefaultMeasurementApproach =
        "คำนวณตามข้อกำหนดการคำนวณและรายงานคาร์บอนฟุตพริ้นท์ขององค์กร (CFO) ขององค์การบริหารจัดการก๊าซเรือนกระจก (อบก.) ซึ่งสอดคล้องกับ ISO 14064-1:2018 " +
        "ใช้ภายใต้ข้อผ่อนปรนระยะเปลี่ยนผ่านที่อนุญาตให้ใช้มาตรฐานที่เทียบเท่า GHG Protocol / Scope 2 คำนวณด้วยค่าการปล่อยก๊าซเรือนกระจกของไฟฟ้าตามที่ใช้ในรายงาน CFO";

    public const string GovernanceGuidance =
        "[โปรดระบุ: คณะกรรมการหรือคณะกรรมการชุดย่อยที่กำกับดูแลความเสี่ยงและโอกาสด้านสภาพภูมิอากาศ, ความถี่และวิธีที่คณะกรรมการได้รับรายงาน, " +
        "การนำประเด็นสภาพภูมิอากาศมาพิจารณาในกลยุทธ์ การตัดสินใจ และการกำหนดค่าตอบแทน, บทบาทของฝ่ายบริหารในการติดตามและควบคุม]";

    public const string StrategyGuidance =
        "[โปรดระบุ: ความเสี่ยงและโอกาสด้านสภาพภูมิอากาศที่คาดว่าจะกระทบองค์กร (ความเสี่ยงทางกายภาพ / ความเสี่ยงจากการเปลี่ยนผ่าน) และกรอบเวลา, " +
        "ผลกระทบต่อโมเดลธุรกิจและห่วงโซ่คุณค่า, แผนการเปลี่ยนผ่าน, ผลกระทบทางการเงิน, ความสามารถในการรับมือ (Climate resilience) รวมถึงการวิเคราะห์สถานการณ์]";

    public const string RiskManagementGuidance =
        "[โปรดระบุ: กระบวนการและข้อมูลที่ใช้ระบุ ประเมิน จัดลำดับความสำคัญ และติดตามความเสี่ยงด้านสภาพภูมิอากาศ, กระบวนการระบุโอกาส, " +
        "และการบูรณาการเข้ากับกระบวนการบริหารความเสี่ยงโดยรวมขององค์กร]";
    #endregion

    #region fields
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly CfoReportStorageService _storage;
    private readonly ILogger<IssbClimateDisclosureService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    #endregion

    public IssbClimateDisclosureService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        CfoReportStorageService storage,
        ILogger<IssbClimateDisclosureService> logger)
    {
        _dbFactory = dbFactory;
        _storage = storage;
        _logger = logger;
    }

    #region load / save
    /// <summary>
    /// โหลดเอกสาร ISSB ของ snapshot: ตัวเลขสร้างใหม่จาก snapshot ทุกครั้ง แล้วซ้อนค่าที่ผู้ใช้บันทึกไว้ (ถ้ามี)
    /// คืน null เมื่อไม่พบ snapshot
    /// </summary>
    public async Task<IssbClimateDisclosureDocument?> LoadAsync(string reportKey, eTopicLevel level, int targetId)
    {
        #region hide [2026-09-24]
        var cfo = await _storage.GetReportAsync(reportKey, level, targetId);
        var dir = _storage.GetReportDirectory(reportKey, level, targetId);
        if (cfo == null || dir == null) return null;

        var doc = new IssbClimateDisclosureDocument();
        var jsonPath = Path.Combine(dir, JsonFileName);
        if (File.Exists(jsonPath))
        {
            try
            {
                doc = JsonSerializer.Deserialize<IssbClimateDisclosureDocument>(await File.ReadAllTextAsync(jsonPath), JsonOptions) ?? doc;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot read ISSB disclosure JSON for report {Key}", reportKey);
            }
        }

        ApplyMetricsFromSnapshot(doc, cfo);
        doc.NetZeroTarget = await FindNetZeroTargetAsync(level, targetId);
        if (doc.CreatedAt == default) doc.CreatedAt = AppTimeZone.LocalNow;
        return doc;
        #endregion
    }

    /// <summary>บันทึก JSON และไฟล์ Word ลงโฟลเดอร์ snapshot คืนค่าไบต์ของไฟล์ Word</summary>
    public async Task<byte[]> SaveAsync(IssbClimateDisclosureDocument doc, string updatedBy)
    {
        #region hide [2026-09-24]
        var dir = _storage.GetReportDirectory(doc.SourceCfoReportKey, doc.Level, doc.TargetId)
                  ?? throw new InvalidOperationException("Source CFO report snapshot was not found.");

        doc.UpdatedAt = AppTimeZone.LocalNow;
        doc.UpdatedBy = updatedBy ?? string.Empty;

        var bytes = GenerateWord(doc);
        await File.WriteAllTextAsync(Path.Combine(dir, JsonFileName), JsonSerializer.Serialize(doc, JsonOptions));
        await File.WriteAllBytesAsync(Path.Combine(dir, WordFileName), bytes);
        return bytes;
        #endregion
    }
    #endregion

    #region metrics
    /// <summary>คัดลอกตัวเลขจาก CFO snapshot ลงเอกสาร ISSB (ไม่แตะข้อความที่ผู้ใช้กรอก)</summary>
    public static void ApplyMetricsFromSnapshot(IssbClimateDisclosureDocument doc, CfoReportDocument cfo)
    {
        #region hide [2026-09-24]
        var calc = cfo.CalculateYearReport;

        doc.SourceCfoReportKey = cfo.ReportKey;
        doc.SourceCfoReportTitle = cfo.Title;
        doc.Level = cfo.Level;
        doc.TargetId = cfo.TargetId;
        // [2026-09-24][Claude] ใช้ชื่อองค์กรจากผลคำนวณ (ไม่มีรหัสภายในนำหน้า เช่น "D7-CRP-001 — ") สำหรับเอกสารเปิดเผยต่อสาธารณะ
        doc.OrganizationTitle = !string.IsNullOrWhiteSpace(calc.OrganizationTitle) ? calc.OrganizationTitle : cfo.OrganizationTitle;
        doc.ReportingYear = cfo.CalculateYear;
        doc.PeriodDescription = cfo.PeriodDescription;
        doc.SourceCreatedAt = cfo.CreatedAt;
        doc.IsPartialYear = cfo.CalculateYear > 0 && cfo.CreatedAt.Date < new DateTime(cfo.CalculateYear + 1, 1, 1);
        doc.ConsolidationApproach = calc.ConsolidationApproach;
        doc.GwpVersion = calc.GwpVersionDisplay;

        doc.Reporting = ToTotals(calc);
        // [2026-09-24][Claude] เทียบปีฐานเฉพาะเมื่อปีฐานเป็นคนละปีกับปีที่รายงาน (ปีเดียวกัน = เปลี่ยนแปลง 0% ทุกแถว ไม่มีความหมาย)
        var hasBase = cfo.BaseYearReport != null && cfo.BaseYear > 0 && cfo.BaseYear != cfo.CalculateYear;
        doc.BaseYear = hasBase ? cfo.BaseYear : 0;
        doc.Base = hasBase ? ToTotals(cfo.BaseYearReport!) : null;

        doc.Scope3Categories = GroupLines(calc.AuditLines.Where(l => l.ScopeNo == eTgoScopeNo.Scope3));
        doc.SeparateCategories = GroupLines(calc.AuditLines.Where(l => l.ScopeNo == eTgoScopeNo.SeparateReport));
        #endregion
    }

    // [2026-09-24][Claude] ปัดแต่ละขอบเขตเป็น 2 ตำแหน่งก่อน แล้วค่อยรวม เพื่อให้ยอดรวมในตารางเท่ากับผลบวกของตัวเลขที่แสดง
    //     (เดิมรวมจากค่าดิบ: 584.05 + 1,349.31 แสดงเป็น 1,933.35)
    private static IssbScopeTotals ToTotals(CfoReportService.CfoAuditReportDto r) => new()
    {
        Scope1 = R2(r.TotalScope1_tCO2e),
        Scope2 = R2(r.TotalScope2_tCO2e),
        Scope3 = R2(r.TotalScope3_tCO2e),
        Separate = R2(r.TotalSeparate_tCO2e)
    };

    private static decimal R2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static List<IssbCategoryLine> GroupLines(IEnumerable<CfoReportService.ActivityAuditLineDto> lines) =>
        lines.GroupBy(l => new { l.ActivityGroupCode, l.ActivityGroupTitle })
             .Select(g => new IssbCategoryLine { Code = g.Key.ActivityGroupCode, Title = g.Key.ActivityGroupTitle, Total_tCO2e = R2(g.Sum(x => x.Total_tCO2e)) })
             .Where(x => x.Total_tCO2e != 0m)
             .OrderBy(x => x.Code)
             .ToList();

    /// <summary>% การเปลี่ยนแปลงเทียบปีฐาน (null = ไม่มีปีฐานหรือปีฐานเป็น 0)</summary>
    public static decimal? PercentChange(decimal current, decimal? baseValue) =>
        baseValue.HasValue && baseValue.Value != 0m ? (current - baseValue.Value) * 100m / baseValue.Value : null;

    /// <summary>tCO2e ต่อรายได้ 1 ล้านบาท (null = ไม่มีรายได้)</summary>
    public static decimal? Intensity(decimal tCO2e, decimal? revenueMillionThb) =>
        revenueMillionThb.HasValue && revenueMillionThb.Value > 0m ? tCO2e / revenueMillionThb.Value : null;

    /// <summary>เป้าหมาย Net Zero ของ Corporate ที่ครอบเป้าหมายนี้ (รายการล่าสุด)</summary>
    private async Task<IssbNetZeroTarget?> FindNetZeroTargetAsync(eTopicLevel level, int targetId)
    {
        #region hide [2026-09-24]
        try
        {
            using var context = await _dbFactory.CreateDbContextAsync();

            int? corporateId = level switch
            {
                eTopicLevel.Corporate => targetId,
                eTopicLevel.Company => await context.Companies.Where(c => c.Id == targetId).Select(c => (int?)c.CorporateId).FirstOrDefaultAsync(),
                _ => await (from f in context.Facilities
                            join c in context.Companies on f.CompanyId equals c.Id
                            where f.Id == targetId
                            select (int?)c.CorporateId).FirstOrDefaultAsync()
            };
            if (corporateId == null) return null;

            var target = await context.NetZeroTargets
                .Where(t => t.CorporateId == corporateId && !t.IsDeleted)
                .OrderByDescending(t => t.Id)
                .FirstOrDefaultAsync();

            return target == null ? null : new IssbNetZeroTarget
            {
                BaseYear = target.BaseYear,
                TargetYear = target.TargetYear,
                TargetReductionPercent = target.TargetReductionPercent
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot load net zero target for {Level} {Id}", level, targetId);
            return null;
        }
        #endregion
    }
    #endregion

    #region word
    public byte[] GenerateWord(IssbClimateDisclosureDocument d)
    {
        #region hide [2026-09-24]
        using var ms = new MemoryStream();
        using (var wd = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = wd.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body());
            AddDefaultStyles(main);
            var body = main.Document.Body!;

            // ---------- หัวเอกสาร ----------
            body.Append(Para("การเปิดเผยข้อมูลที่เกี่ยวข้องกับสภาพภูมิอากาศ", bold: true, size: 40, align: W.JustificationValues.Center));
            body.Append(Para("Climate-related Disclosures (IFRS S2)", bold: true, size: 32, align: W.JustificationValues.Center, color: "374151"));
            body.Append(Para($"{d.OrganizationTitle} | รอบการรายงาน {d.PeriodDescription}", size: 28, align: W.JustificationValues.Center, color: "374151"));

            if (d.IsPartialYear)
            {
                body.Append(Para($"หมายเหตุ: รายงาน CFO ต้นทางสร้างเมื่อ {d.SourceCreatedAt.ToString("dd/MM/yyyy", Inv)} ก่อนสิ้นปี {d.ReportingYear} ข้อมูลอาจไม่ครบ 12 เดือน " +
                                 "— ไม่ควรใช้ยื่นในแบบ 56-1 One Report จนกว่าจะสร้างรายงาน CFO ของปีที่ครบรอบแล้ว", bold: true, color: "B91C1C"));
            }

            body.Append(Heading("หลักเกณฑ์การจัดทำ"));
            body.Append(Para(
                $"จัดทำโดยอ้างอิงโครงสร้างของมาตรฐาน IFRS S2 Climate-related Disclosures (และข้อกำหนดที่เกี่ยวข้องของ IFRS S1) " +
                $"ตามแนวทางการนำมาใช้แบบเป็นลำดับของสำนักงาน ก.ล.ต. ที่เริ่มจากการเปิดเผยข้อมูลด้านสภาพภูมิอากาศ (Climate-first) " +
                $"ข้อมูลก๊าซเรือนกระจกอ้างอิงรายงานคาร์บอนฟุตพริ้นท์ขององค์กร \"{d.SourceCfoReportTitle}\""));

            // ---------- 1-3 ข้อความเชิงบรรยาย ----------
            body.Append(Heading("1. การกำกับดูแล (Governance)"));
            AppendNarrative(body, d.Governance, GovernanceGuidance);

            body.Append(Heading("2. กลยุทธ์ (Strategy)"));
            AppendNarrative(body, d.Strategy, StrategyGuidance);

            body.Append(Heading("3. การบริหารความเสี่ยง (Risk Management)"));
            AppendNarrative(body, d.RiskManagement, RiskManagementGuidance);

            // ---------- 4 ตัวชี้วัดและเป้าหมาย ----------
            body.Append(Heading("4. ตัวชี้วัดและเป้าหมาย (Metrics and Targets)"));
            if (!string.IsNullOrWhiteSpace(d.MetricsTargetsNarrative)) AppendNarrative(body, d.MetricsTargetsNarrative, string.Empty);

            body.Append(SubHeading("4.1 ปริมาณการปล่อยก๊าซเรือนกระจก (ตันคาร์บอนไดออกไซด์เทียบเท่า, tCO₂e)"));
            var hasBase = d.Base != null;
            var ghgHeaders = hasBase
                ? new[] { "ขอบเขต", $"ปี {d.ReportingYear}", $"ปีฐาน {d.BaseYear}", "เปลี่ยนแปลง (%)" }
                : new[] { "ขอบเขต", $"ปี {d.ReportingYear}" };
            var ghgRows = new List<string[]>
            {
                GhgRow("ขอบเขตที่ 1 (Scope 1) การปล่อยทางตรง", d.Reporting.Scope1, d.Base?.Scope1, hasBase),
                GhgRow("ขอบเขตที่ 2 (Scope 2) การปล่อยทางอ้อมจากพลังงาน", d.Reporting.Scope2, d.Base?.Scope2, hasBase),
                GhgRow("รวมขอบเขตที่ 1 และ 2", d.Reporting.Scope1And2, d.Base?.Scope1And2, hasBase),
                GhgRow("ขอบเขตที่ 3 (Scope 3) การปล่อยทางอ้อมอื่น ๆ", d.Reporting.Scope3, d.Base?.Scope3, hasBase),
                GhgRow("รวมขอบเขตที่ 1, 2 และ 3", d.Reporting.Gross, d.Base?.Gross, hasBase)
            };
            body.Append(Table(ghgHeaders, ghgRows, rightAlignFrom: 1, boldRows: new[] { 2, 4 }));

            if (d.Scope3Categories.Any())
            {
                body.Append(SubHeading("4.2 การปล่อยก๊าซเรือนกระจกขอบเขตที่ 3 จำแนกตามประเภท"));
                body.Append(Table(new[] { "รหัส", "ประเภท", "tCO₂e" },
                    d.Scope3Categories.Select(c => new[] { c.Code, c.Title, Num(c.Total_tCO2e) }).ToList(), rightAlignFrom: 2));
            }

            if (d.Reporting.Separate != 0m)
            {
                body.Append(SubHeading("4.3 รายงานแยก (ไม่รวมในยอดรวมข้างต้น)"));
                body.Append(Para($"การปล่อย CO₂ จากชีวมวล (Biogenic CO₂) และก๊าซเรือนกระจกนอกพิธีสารเกียวโต รวม {Num(d.Reporting.Separate)} tCO₂e"));
                if (d.SeparateCategories.Any())
                {
                    body.Append(Table(new[] { "รหัส", "ประเภท", "tCO₂e" },
                        d.SeparateCategories.Select(c => new[] { c.Code, c.Title, Num(c.Total_tCO2e) }).ToList(), rightAlignFrom: 2));
                }
            }

            var intensity12 = Intensity(d.Reporting.Scope1And2, d.RevenueMillionThb);
            if (intensity12.HasValue)
            {
                body.Append(SubHeading("4.4 ความเข้มข้นของการปล่อยก๊าซเรือนกระจก (GHG Intensity)"));
                // [2026-09-24][Claude] แสดงคอลัมน์ปีฐานเฉพาะเมื่อมีปีฐานที่ใช้เทียบได้ (กันรายได้ปีฐานที่บันทึกค้างไว้โผล่มาโดยไม่มียอดปล่อยให้เทียบ)
                var gross = Intensity(d.Reporting.Gross, d.RevenueMillionThb)!.Value;
                List<string[]> intRows;
                string[] intHeaders;
                if (d.Base != null)
                {
                    var base12 = Intensity(d.Base.Scope1And2, d.BaseRevenueMillionThb);
                    var baseGross = Intensity(d.Base.Gross, d.BaseRevenueMillionThb);
                    intHeaders = new[] { "ตัวชี้วัด", $"ปี {d.ReportingYear}", $"ปีฐาน {d.BaseYear}" };
                    intRows = new List<string[]>
                    {
                        new[] { "รายได้ (ล้านบาท)", Num(d.RevenueMillionThb!.Value), d.BaseRevenueMillionThb.HasValue ? Num(d.BaseRevenueMillionThb.Value) : "-" },
                        new[] { "Scope 1+2 ต่อรายได้ (tCO₂e / ล้านบาท)", Num(intensity12.Value, 4), base12.HasValue ? Num(base12.Value, 4) : "-" },
                        new[] { "Scope 1+2+3 ต่อรายได้ (tCO₂e / ล้านบาท)", Num(gross, 4), baseGross.HasValue ? Num(baseGross.Value, 4) : "-" }
                    };
                }
                else
                {
                    intHeaders = new[] { "ตัวชี้วัด", $"ปี {d.ReportingYear}" };
                    intRows = new List<string[]>
                    {
                        new[] { "รายได้ (ล้านบาท)", Num(d.RevenueMillionThb!.Value) },
                        new[] { "Scope 1+2 ต่อรายได้ (tCO₂e / ล้านบาท)", Num(intensity12.Value, 4) },
                        new[] { "Scope 1+2+3 ต่อรายได้ (tCO₂e / ล้านบาท)", Num(gross, 4) }
                    };
                }
                body.Append(Table(intHeaders, intRows, rightAlignFrom: 1));
            }

            body.Append(SubHeading("4.5 วิธีการวัดและข้อสมมติ"));
            body.Append(Bullet($"มาตรฐานและวิธีการ: {(string.IsNullOrWhiteSpace(d.MeasurementApproach) ? DefaultMeasurementApproach : d.MeasurementApproach)}"));
            body.Append(Bullet($"การกำหนดขอบเขตองค์กร (Consolidation approach): {d.ConsolidationApproach}"));
            body.Append(Bullet($"ค่าศักยภาพในการทำให้เกิดภาวะโลกร้อน (GWP): {d.GwpVersion}"));

            body.Append(SubHeading("4.6 เป้าหมายด้านสภาพภูมิอากาศ"));
            body.Append(d.NetZeroTarget != null
                ? Para($"ลดการปล่อยก๊าซเรือนกระจกลง {d.NetZeroTarget.TargetReductionPercent.ToString("0.##", Inv)}% ภายในปี {d.NetZeroTarget.TargetYear} เทียบกับปีฐาน {d.NetZeroTarget.BaseYear}")
                : Para("[โปรดระบุเป้าหมายด้านสภาพภูมิอากาศ ขอบเขตที่ครอบคลุม ปีฐาน และความคืบหน้า — ยังไม่ได้บันทึกเป้าหมาย Net Zero ในระบบ]", italic: true, color: "6B7280"));

            body.Append(SubHeading("4.7 การทวนสอบ"));
            body.Append(!string.IsNullOrWhiteSpace(d.VerifierName)
                ? Para($"ข้อมูลก๊าซเรือนกระจกขอบเขตที่ 1 และ 2 ได้รับการทวนสอบโดย {d.VerifierName} ระดับการรับรอง: {d.AssuranceLevel}")
                : Para("[โปรดระบุชื่อผู้ทวนสอบที่ขึ้นทะเบียนกับ อบก. หรือผู้ทวนสอบตามมาตรฐานสากล และระดับการรับรอง]", italic: true, color: "6B7280"));

            body.Append(Para($"จัดทำจากระบบ ACAMP เมื่อ {AppTimeZone.LocalNow.ToString("dd/MM/yyyy HH:mm", Inv)} | อ้างอิงรายงาน CFO: {d.SourceCfoReportKey}", size: 24, italic: true, color: "6B7280"));

            body.Append(new W.SectionProperties(
                new W.PageSize { Width = 11906U, Height = 16838U },
                new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134U, Right = 1134U, Header = 709U, Footer = 709U, Gutter = 0U }));

            main.Document.Save();
        }
        return ms.ToArray();
        #endregion
    }

    private static string[] GhgRow(string label, decimal current, decimal? baseValue, bool hasBase)
    {
        if (!hasBase) return new[] { label, Num(current) };
        var pct = PercentChange(current, baseValue);
        return new[] { label, Num(current), baseValue.HasValue ? Num(baseValue.Value) : "-", pct.HasValue ? pct.Value.ToString("+0.0;-0.0;0.0", Inv) : "-" };
    }

    private static string Num(decimal value, int decimals = 2) => value.ToString("N" + decimals, Inv);

    private static void AppendNarrative(W.Body body, string text, string guidance)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!string.IsNullOrEmpty(guidance)) body.Append(Para(guidance, italic: true, color: "6B7280"));
            return;
        }
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            body.Append(Para(line.TrimEnd()));
        }
    }

    private static void AddDefaultStyles(MainDocumentPart main)
    {
        var stylePart = main.AddNewPart<StyleDefinitionsPart>();
        stylePart.Styles = new W.Styles(
            new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                    new W.RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName, EastAsia = FontName },
                    new W.FontSize { Val = "32" },
                    new W.FontSizeComplexScript { Val = "32" },
                    new W.Languages { Val = "en-US", Bidi = "th-TH" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                    new W.SpacingBetweenLines { After = "80", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }))));
        stylePart.Styles.Save();
    }

    private static W.Paragraph Heading(string text) =>
        Para(text, bold: true, size: 36, color: "0F5132", spacingBefore: "240");

    private static W.Paragraph SubHeading(string text) =>
        Para(text, bold: true, size: 32, color: "111827", spacingBefore: "160");

    private static W.Paragraph Bullet(string text) => Para("•  " + text, indentLeft: "360");

    private static W.Paragraph Para(string text, bool bold = false, int? size = null, bool italic = false,
        W.JustificationValues? align = null, string? color = null, string? spacingBefore = null, string? indentLeft = null)
    {
        var rp = new W.RunProperties();
        if (bold) { rp.Append(new W.Bold()); rp.Append(new W.BoldComplexScript()); }
        if (italic) { rp.Append(new W.Italic()); rp.Append(new W.ItalicComplexScript()); }
        if (color != null) rp.Append(new W.Color { Val = color });
        if (size.HasValue) { rp.Append(new W.FontSize { Val = size.Value.ToString(Inv) }); rp.Append(new W.FontSizeComplexScript { Val = size.Value.ToString(Inv) }); }

        var pp = new W.ParagraphProperties();
        if (spacingBefore != null) pp.Append(new W.SpacingBetweenLines { Before = spacingBefore, After = "80" });
        if (indentLeft != null) pp.Append(new W.Indentation { Left = indentLeft });
        if (align.HasValue) pp.Append(new W.Justification { Val = align.Value });

        return new W.Paragraph(pp, new W.Run(rp, new W.Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Table Table(string[] headers, List<string[]> rows, int rightAlignFrom, int[]? boldRows = null)
    {
        var table = new W.Table();
        table.Append(new W.TableProperties(
            new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4U, Color = "9CA3AF" })));

        table.Append(Row(headers, rightAlignFrom, isHeader: true, bold: true));
        for (int i = 0; i < rows.Count; i++)
        {
            table.Append(Row(rows[i], rightAlignFrom, isHeader: false, bold: boldRows?.Contains(i) == true));
        }
        return table;
    }

    private static W.TableRow Row(string[] cells, int rightAlignFrom, bool isHeader, bool bold)
    {
        var tr = new W.TableRow();
        for (int c = 0; c < cells.Length; c++)
        {
            var tcp = new W.TableCellProperties();
            if (isHeader) tcp.Append(new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = "E2E8F0" });
            var align = !isHeader && c >= rightAlignFrom ? W.JustificationValues.Right : (isHeader ? W.JustificationValues.Center : W.JustificationValues.Left);
            tr.Append(new W.TableCell(tcp, Para(cells[c], bold: bold, size: 28, align: align)));
        }
        return tr;
    }
    #endregion
}
