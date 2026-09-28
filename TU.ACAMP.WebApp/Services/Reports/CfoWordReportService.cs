using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using TU.ACAMP.Domain.Enums;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-23][Claude] ผลลัพธ์การสร้างรายงาน Word
/// </summary>
public sealed class CfoWordReportResult
{
    public byte[] Bytes { get; internal set; } = Array.Empty<byte>();

    /// <summary>ข้อความเตือน เช่น หาตารางในเทมเพลตไม่เจอ หรือยอดแยกก๊าซไม่ตรงยอดรวม</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// [2026-09-23][Claude] สร้างรายงาน Word "รายงานการปล่อยและดูดกลับก๊าซเรือนกระจกขององค์กร (AR5)" โดยเติมข้อมูลลงเทมเพลตทางการของ อบก.
/// - อ่านข้อมูลจาก CfoReportDocument (snapshot) อย่างเดียว ไม่แตะฐานข้อมูล -> สร้างไฟล์เดิมซ้ำจาก snapshot ได้
/// - ช่องที่ระบบไม่มีข้อมูล (3.1.1-3.1.3, 3.1.5, 3.2.2, 3.2.5, 3.2.7, 3.2.8, 6.3, 7.x, 8, ภาคผนวก) เว้นว่างตามฟอร์มเดิม
/// - ยอดรวมทุกตารางใช้ตัวเลขจาก CfoAuditReportDto ชุดเดียวกับ Excel (ไม่คำนวณซ้ำคนละเส้นทาง)
/// </summary>
public class CfoWordReportService
{
    private const string TonFormat = "#,##0.0000";
    private const decimal GasReconcileTolerance_tCO2e = 0.01m;

    private static readonly CultureInfo ThaiCulture = CultureInfo.GetCultureInfo("th-TH");
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CfoWordReportService> _logger;

    public CfoWordReportService(
        IWebHostEnvironment env,
        ILogger<CfoWordReportService> logger,
        IConfiguration? configuration = null)
    {
        _env = env;
        _logger = logger;
        _configuration = configuration ?? new ConfigurationManager();
    }

    /// <summary>
    /// ค้นเทมเพลตตาม config / โฟลเดอร์มาตรฐาน แล้วสร้างรายงาน
    /// </summary>
    /// <exception cref="FileNotFoundException">ไม่พบไฟล์เทมเพลต Word</exception>
    public CfoWordReportResult Generate(CfoReportDocument doc)
    {
        var templatePath = ReportTemplateLocator.Find(
            _env, _configuration, _logger,
            CfoWordTemplateMap.TemplateConfigKey,
            CfoWordTemplateMap.TemplateSearchPattern,
            CfoWordTemplateMap.TemplateNameMarker);

        if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
        {
            // [2026-09-23][Claude] ไม่มี fallback แบบ Excel: ถ้าไม่มีเทมเพลตทางการ ไม่ควรออกเอกสารคนละฟอร์ม
            throw new FileNotFoundException(
                $"ไม่พบไฟล์เทมเพลต Word (ค้นจาก config '{CfoWordTemplateMap.TemplateConfigKey}' และโฟลเดอร์ Files/Template, Files/Excels)");
        }

        return GenerateFromTemplate(doc, templatePath);
    }

    /// <summary>
    /// สร้างรายงานจากไฟล์เทมเพลตที่ระบุ (ใช้ตรงใน unit test ได้)
    /// </summary>
    public CfoWordReportResult GenerateFromTemplate(CfoReportDocument doc, string templatePath)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var result = new CfoWordReportResult();
        using var ms = new MemoryStream();
        using (var fs = File.OpenRead(templatePath))
        {
            fs.CopyTo(ms);
        }
        ms.Position = 0;

        using (var wordDoc = WordprocessingDocument.Open(ms, true))
        {
            var body = wordDoc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("เทมเพลต Word ไม่มี document body");

            var tables = LocateTables(body, result);

            // รูปทำก่อนเติมข้อความ: หากรอบรูปจากหัวข้อในเทมเพลตที่ยังไม่ถูกแก้
            FillImages(wordDoc.MainDocumentPart!, body, doc, result);

            FillCover(body, doc, result);
            FillIntroduction(body, doc, result);
            FillGeneralInfo(tables, doc);
            FillOrganizationBoundary(tables, doc);
            FillActivitiesByFacility(tables, doc);
            FillGreenhouseGases(tables);
            FillSources(tables, doc);
            FillMonitoring(tables, doc);
            FillScope1Summary(tables, doc, result);
            FillScopeSummaries(tables, doc);
            FillCarbonIntensity(tables, doc);
            FillBaseYear(body, tables, doc);
            FillBoundaryChanges(body, doc);
            FillBaseYearDifferences(body, doc);
            FillUncertainty(tables, doc);
            FillHeaderFooter(wordDoc.MainDocumentPart!, doc);

            wordDoc.MainDocumentPart!.Document.Save();
        }

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("[CFO Word] {Warning}", warning);
        }

        result.Bytes = ms.ToArray();
        return result;
    }

    #region Locate
    /// <summary>
    /// จับคู่ TableAnchor กับตารางจริงในเอกสาร (ระดับบนสุดของ body เท่านั้น) — ตารางหนึ่งถูกจับคู่ได้ครั้งเดียว
    /// </summary>
    private Dictionary<TableAnchor, W.Table> LocateTables(W.Body body, CfoWordReportResult result)
    {
        var candidates = new List<(W.Table Table, List<string> Preceding, string Header)>();
        var recentParagraphs = new List<string>();

        foreach (var element in body.ChildElements)
        {
            if (element is W.Paragraph p)
            {
                var text = Normalize(GetText(p));
                if (text.Length > 0) recentParagraphs.Add(text);
            }
            else if (element is W.Table t)
            {
                var firstRow = t.Elements<W.TableRow>().FirstOrDefault();
                var header = firstRow == null
                    ? string.Empty
                    : Normalize(string.Join(" | ", firstRow.Elements<W.TableCell>().Select(GetText)));
                candidates.Add((t, recentParagraphs.TakeLast(2).ToList(), header));
            }
        }

        var map = new Dictionary<TableAnchor, W.Table>();
        var used = new HashSet<W.Table>();

        foreach (var anchor in CfoWordTemplateMap.All)
        {
            var match = candidates.FirstOrDefault(c =>
                !used.Contains(c.Table)
                && (anchor.HeadingPrefix == null || c.Preceding.Any(pt => pt.StartsWith(anchor.HeadingPrefix, StringComparison.Ordinal)))
                && (anchor.HeaderContains == null || c.Header.Contains(anchor.HeaderContains, StringComparison.Ordinal)));

            if (match.Table != null)
            {
                map[anchor] = match.Table;
                used.Add(match.Table);
            }
            else
            {
                result.Warnings.Add($"ไม่พบตาราง '{anchor.Name}' ในเทมเพลต — ข้ามการเติมข้อมูลส่วนนี้");
            }
        }

        return map;
    }
    #endregion

    #region Cover / ข้อ 2 / ข้อ 3.1
    private void FillCover(W.Body body, CfoReportDocument doc, CfoWordReportResult result)
    {
        // หน้าปกอยู่ก่อนตารางแรกเสมอ
        var coverParagraphs = body.ChildElements
            .TakeWhile(e => e is not W.Table)
            .OfType<W.Paragraph>()
            .ToList();

        var reportDate = doc.PreparationDate ?? doc.CreatedAt.DateTime;

        void Append(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var paragraph = coverParagraphs.FirstOrDefault(p => Normalize(GetText(p)).StartsWith(label, StringComparison.Ordinal));
            if (paragraph == null)
            {
                result.Warnings.Add($"ไม่พบย่อหน้า '{label}' บนหน้าปก");
                return;
            }
            AppendText(paragraph, value.Trim());
        }

        Append(CfoWordTemplateMap.CoverOrganizationLabel, doc.OrganizationTitle);
        Append(CfoWordTemplateMap.CoverAddressLabel, doc.ContactAddress);
        Append(CfoWordTemplateMap.CoverReportDateLabel, FormatThaiDate(reportDate));
        Append(CfoWordTemplateMap.CoverPeriodLabel, GetPeriodText(doc));
    }

    /// <summary>
    /// [2026-09-23][Claude] ข้อ 1 บทนำ: แทนย่อหน้าคำแนะนำของเทมเพลตด้วยบทนำมาตรฐาน 3 ย่อหน้า (ระบุชื่อองค์กร)
    /// เนื้อหาเป็นข้อความกลาง ๆ ใช้เฉพาะข้อมูลที่มีใน snapshot — ผู้ใช้แก้ถ้อยคำต่อใน Word ได้
    /// </summary>
    private static void FillIntroduction(W.Body body, CfoReportDocument doc, CfoWordReportResult result)
    {
        var guidance = body.ChildElements
            .SkipWhile(e => !(e is W.Paragraph hp && Normalize(GetText(hp)) == CfoWordTemplateMap.IntroductionHeading))
            .Skip(1)
            .TakeWhile(e => e is W.Paragraph)
            .OfType<W.Paragraph>()
            .FirstOrDefault(p => Normalize(GetText(p)).StartsWith(CfoWordTemplateMap.IntroductionGuidancePrefix, StringComparison.Ordinal));

        if (guidance == null)
        {
            result.Warnings.Add("ไม่พบย่อหน้าคำแนะนำใต้หัวข้อ 'บทนำ' — ข้ามการเติมบทนำ");
            return;
        }

        OpenXmlElement insertAfter = guidance;
        var paragraphs = BuildIntroduction(doc);
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var target = i == 0 ? guidance : (W.Paragraph)guidance.CloneNode(true);
            SetParagraphText(target, paragraphs[i]);

            // ย่อหน้าเนื้อความ: เยื้องบรรทัดแรก + เว้นระยะหลังย่อหน้า (ย่อหน้าคำแนะนำเดิมไม่มีทั้งสองอย่าง)
            var pPr = target.ParagraphProperties ?? target.PrependChild(new W.ParagraphProperties());
            pPr.Indentation = new W.Indentation { FirstLine = CfoWordTemplateMap.IntroductionFirstLineIndent };
            pPr.SpacingBetweenLines = new W.SpacingBetweenLines { After = CfoWordTemplateMap.IntroductionSpacingAfter };

            if (i > 0)
            {
                insertAfter.InsertAfterSelf(target);
                insertAfter = target;
            }
        }
    }

    private static IReadOnlyList<string> BuildIntroduction(CfoReportDocument doc)
    {
        var org = string.IsNullOrWhiteSpace(doc.OrganizationTitle) ? "องค์กร" : doc.OrganizationTitle.Trim();
        var period = GetPeriodText(doc);
        var periodText = string.IsNullOrWhiteSpace(period) ? string.Empty : $" สำหรับรอบการติดตามผล {period.Trim()}";

        var approach = DescribeConsolidationApproach(doc.CalculateYearReport.ConsolidationApproach);
        var approachText = string.IsNullOrWhiteSpace(approach) ? string.Empty : $" โดยใช้แนวทาง{approach} ในการกำหนดขอบเขตองค์กร";

        var hasBaseYear = doc.BaseYear > 0 && doc.BaseYearReport != null && doc.BaseYear != doc.CalculateYear;
        var baseYearText = hasBaseYear ? $" และแสดงข้อมูลเปรียบเทียบกับปีฐาน {doc.BaseYear}" : string.Empty;

        return new[]
        {
            $"{org} ตระหนักถึงความสำคัญของปัญหาการเปลี่ยนแปลงสภาพภูมิอากาศ และมุ่งมั่นที่จะมีส่วนร่วมในการลดการปล่อยก๊าซเรือนกระจก " +
            $"จึงได้จัดทำรายงานการปล่อยและดูดกลับก๊าซเรือนกระจกขององค์กรฉบับนี้{periodText} " +
            "ตามข้อกำหนดในการคำนวณและรายงานคาร์บอนฟุตพริ้นท์ขององค์กร ขององค์การบริหารจัดการก๊าซเรือนกระจก (องค์การมหาชน)",

            "รายงานฉบับนี้ครอบคลุมแหล่งปล่อยก๊าซเรือนกระจกตามขอบเขตการดำเนินงานที่ระบุไว้ในหัวข้อที่ 3" +
            $"{approachText} คำนวณปริมาณการปล่อยก๊าซเรือนกระจกจากข้อมูลกิจกรรมและค่าสัมประสิทธิ์การปล่อยก๊าซเรือนกระจก " +
            $"พร้อมใช้ค่าศักยภาพในการทำให้เกิดภาวะโลกร้อน (GWP) ตาม IPCC Fifth Assessment Report (AR5){baseYearText}",

            $"วัตถุประสงค์ของการจัดทำรายงาน เพื่อให้{org}ทราบปริมาณและแหล่งที่มาของการปล่อยก๊าซเรือนกระจกที่สำคัญ " +
            "ใช้เป็นข้อมูลพื้นฐานในการวางแผนและติดตามผลการลดการปล่อยก๊าซเรือนกระจก " +
            "และใช้ประกอบการทวนสอบและรับรองผลคาร์บอนฟุตพริ้นท์ขององค์กรโดยองค์การบริหารจัดการก๊าซเรือนกระจก (องค์การมหาชน)"
        };
    }

    private static void FillGeneralInfo(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        if (!tables.TryGetValue(CfoWordTemplateMap.GeneralInfo, out var table)) return;

        SetCellIfValue(table, CfoWordTemplateMap.GeneralInfoOrganizationRow, CfoWordTemplateMap.GeneralInfoValueCol, doc.OrganizationTitle);
        SetCellIfValue(table, CfoWordTemplateMap.GeneralInfoAddressRow, CfoWordTemplateMap.GeneralInfoValueCol, doc.ContactAddress);
        SetCellIfValue(table, CfoWordTemplateMap.GeneralInfoPeriodRow, CfoWordTemplateMap.GeneralInfoValueCol, GetPeriodText(doc));

        var threshold = doc.CalculateYearReport.MaterialityThreshold;
        SetCellIfValue(table, CfoWordTemplateMap.GeneralInfoMaterialityRow, CfoWordTemplateMap.GeneralInfoValueCol,
            $"{threshold.ToString("0.##", CultureInfo.InvariantCulture)}% Materiality");
    }

    private static void FillOrganizationBoundary(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        if (!tables.TryGetValue(CfoWordTemplateMap.OrganizationBoundary, out var table)) return;

        SetCellIfValue(table, CfoWordTemplateMap.BoundaryApproachRow, CfoWordTemplateMap.BoundaryValueCol,
            DescribeConsolidationApproach(doc.CalculateYearReport.ConsolidationApproach));

        // ถ้าไม่ได้เลือกหน่วยย่อยเฉพาะ (null = ทั้งหมด) ระบบไม่มีรายชื่อ → คงคำแนะนำในฟอร์มไว้ให้ผู้ใช้กรอก
        string? facilities = null;
        if (doc.InScopeChildTitles is { Count: > 0 })
        {
            facilities = string.Join("\n", doc.InScopeChildTitles.Where(t => !string.IsNullOrWhiteSpace(t)));
        }
        else if (doc.Level == eTopicLevel.Facility)
        {
            facilities = doc.OrganizationTitle;
        }
        SetCellIfValue(table, CfoWordTemplateMap.BoundaryFacilitiesRow, CfoWordTemplateMap.BoundaryValueCol, facilities);
    }

    private static void FillActivitiesByFacility(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        var anchor = CfoWordTemplateMap.ActivitiesByFacility;
        if (!tables.TryGetValue(anchor, out var table)) return;

        var lines = Ordered(doc.CalculateYearReport.AuditLines
            .Where(l => l.ScopeNo is eTgoScopeNo.Scope1 or eTgoScopeNo.Scope2 or eTgoScopeNo.Scope3))
            .ToList();
        if (lines.Count == 0)
        {
            ClearDataRows(table, anchor);
            return;
        }

        // [2026-09-23][Claude] 1 แถว = 1 หน่วยงาน (LocationTitle ของหัวข้อกิจกรรม) — snapshot เก่าที่ไม่มีค่านี้รวมเป็นแถวเดียว
        var groups = lines.GroupBy(l => l.LocationTitle?.Trim() ?? string.Empty).ToList();

        static string ListOf(IEnumerable<CfoReportService.ActivityAuditLineDto> source, eTgoScopeNo scope)
            => string.Join("\n", source.Where(l => l.ScopeNo == scope)
                .Select(l => l.TopicTitle)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct());

        var rows = ResizeDataRows(table, anchor, groups.Count);
        for (int i = 0; i < groups.Count; i++)
        {
            SetCell(rows[i], CfoWordTemplateMap.ActivitiesFacilityCol, groups[i].Key.Length > 0 ? groups[i].Key : FacilityLabel(doc));
            SetCell(rows[i], CfoWordTemplateMap.ActivitiesScope1Col, ListOf(groups[i], eTgoScopeNo.Scope1));
            SetCell(rows[i], CfoWordTemplateMap.ActivitiesScope2Col, ListOf(groups[i], eTgoScopeNo.Scope2));
            SetCell(rows[i], CfoWordTemplateMap.ActivitiesScope3Col, ListOf(groups[i], eTgoScopeNo.Scope3));
        }
    }

    private static void FillGreenhouseGases(Dictionary<TableAnchor, W.Table> tables)
    {
        if (!tables.TryGetValue(CfoWordTemplateMap.GreenhouseGases, out var table)) return;

        // ล้างค่าตัวอย่าง (HCFC-22) ในแถว "ก๊าซอื่น ๆ เพิ่มเติม" — ระบบไม่มีข้อมูลชนิดก๊าซนอกข้อกำหนด
        var row = GetRow(table, CfoWordTemplateMap.GasesOtherRow);
        if (row != null) SetCell(row, CfoWordTemplateMap.GasesValueCol, string.Empty);
    }
    #endregion

    #region ข้อ 3.2.x แหล่งปล่อย / ข้อ 4.x การติดตามผล
    private static void FillSources(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        var report = doc.CalculateYearReport;
        var lines = report.AuditLines;
        var facility = FacilityLabel(doc);
        var totalGrossKg = lines
            .Where(l => l.ScopeNo is eTgoScopeNo.Scope1 or eTgoScopeNo.Scope2 or eTgoScopeNo.Scope3)
            .Sum(l => l.KgCO2e);

        void Fill(TableAnchor anchor, Func<CfoReportService.ActivityAuditLineDto, bool> filter,
            Func<CfoReportService.ActivityAuditLineDto, string>? nameOf = null)
        {
            if (!tables.TryGetValue(anchor, out var table)) return;
            nameOf ??= SourceLabel;

            // [2026-09-23][Claude] 1 แถว = แหล่งปล่อย x หน่วยงาน (แหล่งเดียวกันคนละหน่วยงานแยกแถว) รวม kgCO2e ของแถวไว้ประเมินความสำคัญ
            var items = Ordered(lines.Where(filter))
                .GroupBy(l => (Name: nameOf(l), Location: l.LocationTitle?.Trim() ?? string.Empty))
                .Select(g => (g.Key.Name, g.Key.Location, Lines: g.ToList()))
                .ToList();
            if (items.Count == 0)
            {
                ClearDataRows(table, anchor);
                return;
            }

            var rows = ResizeDataRows(table, anchor, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                // Facility: หัวข้อที่ผูกกับ Facility ใช้ชื่อ Facility นั้น, นอกนั้นเติมเฉพาะรายงานระดับ Facility
                var first = items[i].Lines[0];
                var facilityName = first.TopicLevel == eTopicLevel.Facility && items[i].Location.Length > 0 ? items[i].Location : facility;
                SetCell(rows[i], CfoWordTemplateMap.SourcesFacilityCol, facilityName);
                SetCell(rows[i], CfoWordTemplateMap.SourcesNameCol, items[i].Name);
                SetCell(rows[i], CfoWordTemplateMap.SourcesLocationCol, items[i].Location);
                SetCell(rows[i], CfoWordTemplateMap.SourcesSignificanceCol, DescribeSignificance(items[i].Lines, doc, totalGrossKg));
            }
        }

        Fill(CfoWordTemplateMap.Scope1Sources, l => l.ScopeNo == eTgoScopeNo.Scope1 && !IsBiogenic(l));
        // [2026-09-23][Claude] 3.2.2 แหล่งปล่อยที่ใช้ชีวมวล/ก๊าซชีวภาพ = child topic "[Biogenic] ..." (ชื่อแหล่ง = ชื่อหัวข้อหลักไม่มี prefix)
        Fill(CfoWordTemplateMap.BiomassSources, IsBiogenic, l => StripBiogenicPrefix(SourceLabel(l)));
        Fill(CfoWordTemplateMap.SeparateSources, l => l.ScopeNo == eTgoScopeNo.SeparateReport && !IsBiogenic(l));
        Fill(CfoWordTemplateMap.Scope2Sources, l => l.ScopeNo == eTgoScopeNo.Scope2);
        Fill(CfoWordTemplateMap.Scope3Sources, l => l.ScopeNo == eTgoScopeNo.Scope3);
    }

    /// <summary>
    /// [2026-09-23][Claude] ความสำคัญของแหล่งปล่อย (ข้อ 3.2.x คอลัมน์ "ความสำคัญ")
    /// - Scope 3: ใช้ผลประเมิน Fr-03.2 ของหมวดนั้น (Fr032Rubric.Evaluate — เกณฑ์เดียวกับ Excel / wizard) ถ้าหมวดถูกประเมินแล้ว
    /// - นอกนั้น (Scope 1, 2, รายงานแยก และ Scope 3 ที่ไม่มีผล Fr-03.2): สัดส่วนต่อการปล่อยรวม (Scope 1+2+3)
    ///   >= ระดับความมีสาระสำคัญ (Materiality Threshold ข้อ 2.9) = มีนัยสำคัญมาก
    /// </summary>
    private static string DescribeSignificance(List<CfoReportService.ActivityAuditLineDto> rowLines, CfoReportDocument doc, decimal totalGrossKg)
    {
        var threshold = doc.CalculateYearReport.MaterialityThreshold > 0m ? doc.CalculateYearReport.MaterialityThreshold : 5.0m;
        var first = rowLines[0];

        if (first.ScopeNo == eTgoScopeNo.Scope3 && doc.Fr032Categories is { Count: > 0 })
        {
            var categoryNo = CfoReportService.ParseScope3Category(first.ActivityGroupCode);
            var category = doc.Fr032Categories.FirstOrDefault(c => c.CategoryNo == categoryNo);
            if (category != null)
            {
                var eval = CfoReportService.Fr032Rubric.Evaluate(category, threshold);
                if (eval.IsAssessed)
                {
                    var label = eval.IsSignificant ? CfoWordTemplateMap.SignificantHigh : CfoWordTemplateMap.SignificantLow;
                    return $"{label}\n(Fr-03.2 หมวด {categoryNo} คะแนน {eval.WeightedScore.ToString("0.00", CultureInfo.InvariantCulture)})";
                }
            }
        }

        if (totalGrossKg <= 0m) return string.Empty;

        var share = rowLines.Sum(l => l.KgCO2e) * 100m / totalGrossKg;
        var result = share >= threshold ? CfoWordTemplateMap.SignificantHigh : CfoWordTemplateMap.SignificantLow;
        return $"{result}\n({share.ToString("0.##", CultureInfo.InvariantCulture)}% ของการปล่อยรวม)";
    }

    private static void FillMonitoring(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        var lines = doc.CalculateYearReport.AuditLines;

        void Fill(TableAnchor anchor, eTgoScopeNo scope)
        {
            if (!tables.TryGetValue(anchor, out var table)) return;

            var scoped = Ordered(lines.Where(l => l.ScopeNo == scope)).ToList();
            if (scoped.Count == 0)
            {
                ClearDataRows(table, anchor);
                return;
            }

            // ช่อง "จุดที่ตรวจวัด" และ "ตรวจวัด / หลักฐานการชำระเงิน / ประมาณค่า" ระบบไม่มีข้อมูล → เว้นว่าง
            // (ClearRow ล้างเครื่องหมายถูกตัวอย่างในเทมเพลตออกแล้ว)
            var rows = ResizeDataRows(table, anchor, scoped.Count);
            for (int i = 0; i < scoped.Count; i++)
            {
                var line = scoped[i];
                SetCell(rows[i], CfoWordTemplateMap.MonitoringSourceCol, SourceLabel(line));
                // [2026-09-24][Claude] ใช้ปริมาณที่ปรับแล้ว (Quantity x Ratio x Share%) ให้ตรงกับหน่วยของ child และค่า EF
                //     เดิมใช้ Quantity ดิบ ทำให้ WTT น้ำมันแสดงลิตรแต่ติดหน่วย kg (เกณฑ์เดียวกับ Excel Fr-04.2)
                SetCell(rows[i], CfoWordTemplateMap.MonitoringActivityCol,
                    $"{FormatQuantity(line.ConvertedQuantity)} {line.ActivityUnitName}".Trim());
                // [2026-09-23][Claude] จุดที่บันทึกข้อมูล = หน่วยงานที่หัวข้อกิจกรรมผูกอยู่ (ตำแหน่งมิเตอร์จริงระบบไม่มี)
                SetCell(rows[i], CfoWordTemplateMap.MonitoringPointCol, line.LocationTitle);
                SetCell(rows[i], CfoWordTemplateMap.MonitoringEvidenceCol,
                    line.AttachedFilesCount > 0 ? $"ไฟล์แนบในระบบ {line.AttachedFilesCount} ไฟล์" : string.Empty);
                SetCell(rows[i], CfoWordTemplateMap.MonitoringEfCol, DescribeEf(line));
            }
        }

        Fill(CfoWordTemplateMap.Scope1Monitoring, eTgoScopeNo.Scope1);
        Fill(CfoWordTemplateMap.Scope2Monitoring, eTgoScopeNo.Scope2);
        Fill(CfoWordTemplateMap.Scope3Monitoring, eTgoScopeNo.Scope3);
        Fill(CfoWordTemplateMap.SeparateMonitoring, eTgoScopeNo.SeparateReport);
    }
    #endregion

    #region ข้อ 5 สรุปปริมาณการปล่อย
    private static void FillScope1Summary(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc, CfoWordReportResult result)
    {
        var anchor = CfoWordTemplateMap.Scope1Summary;
        if (!tables.TryGetValue(anchor, out var table)) return;

        var report = doc.CalculateYearReport;
        var lines = Ordered(report.AuditLines.Where(l => l.ScopeNo == eTgoScopeNo.Scope1)).ToList();

        if (lines.Count > 0)
        {
            var rows = ResizeDataRows(table, anchor, lines.Count);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var gases = CfoGasEmissionCalculator.Calculate(line);

                SetCell(rows[i], CfoWordTemplateMap.Scope1SummaryIndexCol, (i + 1).ToString(CultureInfo.InvariantCulture));
                SetCell(rows[i], CfoWordTemplateMap.Scope1SummaryNameCol, SourceLabel(line));
                WriteGasCells(rows[i], CfoWordTemplateMap.Scope1SummaryFirstGasCol, gases);
                SetCell(rows[i], CfoWordTemplateMap.Scope1SummaryTotalCol, FormatTon(line.Total_tCO2e));

                // ยอดรวมรายแถวใช้ Total_tCO2e (EF รวม) ชุดเดียวกับ Excel — แจ้งเตือนถ้า EF รายก๊าซรวมกันไม่เท่า EF รวม
                if (Math.Abs(gases.Total - line.Total_tCO2e) > GasReconcileTolerance_tCO2e)
                {
                    result.Warnings.Add(
                        $"5.1 '{SourceLabel(line)}': ผลรวมรายก๊าซ {FormatTon(gases.Total)} tCO2e ไม่เท่ายอดรวม {FormatTon(line.Total_tCO2e)} tCO2e (EF รายก๊าซของแหล่งปล่อยนี้ไม่สอดคล้องกับ EF รวม)");
                }
            }
        }
        else
        {
            ClearDataRows(table, anchor);
        }

        var footer = GetFooterRow(table);
        if (footer != null)
        {
            WriteGasCells(footer, CfoWordTemplateMap.Scope1SummaryFooterFirstGasCol, CfoGasEmissionCalculator.Sum(lines));
            SetCell(footer, CfoWordTemplateMap.Scope1SummaryFooterTotalCol, FormatTon(report.TotalScope1_tCO2e));
        }
    }

    private static void FillScopeSummaries(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        var report = doc.CalculateYearReport;

        void Fill(TableAnchor anchor, eTgoScopeNo scope, decimal total)
        {
            if (!tables.TryGetValue(anchor, out var table)) return;

            var lines = Ordered(report.AuditLines.Where(l => l.ScopeNo == scope)).ToList();
            if (lines.Count > 0)
            {
                var rows = ResizeDataRows(table, anchor, lines.Count);
                for (int i = 0; i < lines.Count; i++)
                {
                    SetCell(rows[i], CfoWordTemplateMap.SummaryNameCol, SourceLabel(lines[i]));
                    SetCell(rows[i], CfoWordTemplateMap.SummaryValueCol, FormatTon(lines[i].Total_tCO2e));
                }
            }
            else
            {
                ClearDataRows(table, anchor);
            }

            var footer = GetFooterRow(table);
            if (footer != null) SetCell(footer, CfoWordTemplateMap.SummaryValueCol, FormatTon(total));
        }

        Fill(CfoWordTemplateMap.Scope2Summary, eTgoScopeNo.Scope2, report.TotalScope2_tCO2e);
        Fill(CfoWordTemplateMap.Scope3Summary, eTgoScopeNo.Scope3, report.TotalScope3_tCO2e);
        Fill(CfoWordTemplateMap.SeparateSummary, eTgoScopeNo.SeparateReport, report.TotalSeparate_tCO2e);
    }

    private static void FillCarbonIntensity(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        if (!tables.TryGetValue(CfoWordTemplateMap.CarbonIntensity, out var table)) return;

        var r = doc.CalculateYearReport;
        var s12 = r.TotalScope1_tCO2e + r.TotalScope2_tCO2e;
        var s123 = r.TotalGrossEmissions_tCO2e;

        void Value(int row, string text)
        {
            var tr = GetRow(table, row);
            if (tr != null) SetCell(tr, CfoWordTemplateMap.IntensityValueCol, text);
        }

        void Unit(int row, string text)
        {
            var tr = GetRow(table, row);
            if (tr != null) SetCell(tr, CfoWordTemplateMap.IntensityUnitCol, text);
        }

        Value(CfoWordTemplateMap.IntensityScope1Row, FormatTon(r.TotalScope1_tCO2e));
        Value(CfoWordTemplateMap.IntensityScope2Row, FormatTon(r.TotalScope2_tCO2e));
        Value(CfoWordTemplateMap.IntensityScope3Row, FormatTon(r.TotalScope3_tCO2e));
        Value(CfoWordTemplateMap.IntensityScope12Row, FormatTon(s12));
        Value(CfoWordTemplateMap.IntensityScope123Row, FormatTon(s123));

        // ผลผลิต: กติกาเดียวกับ Excel Fr-01 (รายการสินค้าหลายตัว = รวมจำนวน, รวมชื่อหน่วย)
        var (amount, unit) = GetProduction(doc);
        if (amount > 0m)
        {
            Value(CfoWordTemplateMap.IntensityProductionRow, FormatQuantity(amount));
            Value(CfoWordTemplateMap.IntensityPerScope12Row, FormatTon(s12 / amount));
            Value(CfoWordTemplateMap.IntensityPerScope123Row, FormatTon(s123 / amount));

            if (!string.IsNullOrWhiteSpace(unit))
            {
                Unit(CfoWordTemplateMap.IntensityProductionRow, unit);
                Unit(CfoWordTemplateMap.IntensityPerScope12Row, $"Ton CO2e/{unit}");
                Unit(CfoWordTemplateMap.IntensityPerScope123Row, $"Ton CO2e/{unit}");
            }
        }
        // ไม่มีผลผลิต → เว้นว่างตามฟอร์ม ห้ามหารด้วย 0
    }
    #endregion

    #region ข้อ 6 ปีฐาน
    private static void FillBaseYear(W.Body body, Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        // 6.1: เติมปีฐานในย่อหน้าเส้นประบรรทัดแรกหลังหัวข้อ
        if (doc.BaseYear > 0)
        {
            var dotted = body.ChildElements
                .SkipWhile(e => !(e is W.Paragraph hp && Normalize(GetText(hp)).StartsWith(CfoWordTemplateMap.BaseYearHeadingPrefix, StringComparison.Ordinal)))
                .Skip(1)
                .TakeWhile(e => e is W.Paragraph)
                .OfType<W.Paragraph>()
                .FirstOrDefault(p =>
                {
                    var t = Normalize(GetText(p));
                    return t.Length > 0 && t.All(c => c == CfoWordTemplateMap.DottedLineChar);
                });

            if (dotted != null)
            {
                var text = string.IsNullOrWhiteSpace(doc.BasePeriodText)
                    ? $"ปี {doc.BaseYear}"
                    : $"ปี {doc.BaseYear} ({doc.BasePeriodText.Trim()})";
                SetParagraphText(dotted, text);
            }
        }

        // 6.2: ตารางแหล่งปล่อยในปีฐาน — สร้างแถวใหม่ทั้งหมดจัดกลุ่มตามขอบเขต (ล้างข้อมูลตัวอย่างในเทมเพลต)
        var anchor = CfoWordTemplateMap.BaseYearBoundary;
        if (!tables.TryGetValue(anchor, out var table)) return;

        var baseReport = doc.BaseYearReport;
        if (baseReport == null)
        {
            ClearDataRows(table, anchor);
            return;
        }

        var calcReport = doc.CalculateYearReport;
        var sameYear = ReferenceEquals(baseReport, calcReport) || baseReport.Year == calcReport.Year;
        // key เดียวกับ Excel Fr-04.2 คอลัมน์ AS
        static string Key(CfoReportService.ActivityAuditLineDto l) => $"{l.TopicId}|{l.ActivityGroupId}|{l.SourceId}";

        var groups = new (string Label, eTgoScopeNo Scope)[]
        {
            (CfoWordTemplateMap.BaseYearScope1Label, eTgoScopeNo.Scope1),
            (CfoWordTemplateMap.BaseYearScope2Label, eTgoScopeNo.Scope2),
            (CfoWordTemplateMap.BaseYearScope3Label, eTgoScopeNo.Scope3),
            (CfoWordTemplateMap.BaseYearSeparateLabel, eTgoScopeNo.SeparateReport)
        };

        var entries = new List<(string Label, bool FirstInGroup, bool GroupHasMore, string Name, string Value, string Remark)>();
        foreach (var (label, scope) in groups)
        {
            var baseLines = Ordered(baseReport.AuditLines.Where(l => l.ScopeNo == scope)).ToList();
            var baseKeys = baseLines.Select(Key).ToHashSet();
            var calcKeys = calcReport.AuditLines.Where(l => l.ScopeNo == scope).Select(Key).ToHashSet();

            var items = new List<(string Name, string Value, string Remark)>();
            foreach (var l in baseLines)
            {
                var remark = !sameYear && !calcKeys.Contains(Key(l)) ? $"ไม่มีรายการนี้ในปีรายงาน {calcReport.Year}" : string.Empty;
                items.Add((SourceLabel(l), FormatTon(l.Total_tCO2e), remark));
            }

            if (!sameYear)
            {
                // รายการใหม่ในปีรายงานที่ไม่มีในปีฐาน (ตามตัวอย่างในฟอร์ม: ปริมาณปีฐาน = "ไม่มี")
                foreach (var l in Ordered(calcReport.AuditLines.Where(x => x.ScopeNo == scope && !baseKeys.Contains(Key(x)))))
                {
                    items.Add((SourceLabel(l), "ไม่มี", $"เริ่มมีรายการนี้ในปีรายงาน {calcReport.Year}"));
                }
            }

            if (items.Count == 0) items.Add((string.Empty, string.Empty, string.Empty)); // คงหัวแถวขอบเขตไว้ตามฟอร์ม

            for (int i = 0; i < items.Count; i++)
            {
                entries.Add((label, i == 0, items.Count > 1, items[i].Name, items[i].Value, items[i].Remark));
            }
        }

        // ต้นแบบแถว = แถวข้อมูลแรกที่ไม่มี vertical merge (ในเทมเพลตคือแถว "ขอบเขตที่ 2")
        var prototypeIndex = FindPlainRowIndex(table, anchor.DataStartRow);
        var rows = ResizeDataRows(table, anchor, entries.Count, prototypeIndex);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var labelCell = GetCell(rows[i], CfoWordTemplateMap.BaseYearScopeCol);
            if (labelCell != null)
            {
                SetCellText(labelCell, e.FirstInGroup ? e.Label : string.Empty);
                SetVerticalMerge(labelCell, !e.GroupHasMore ? null : e.FirstInGroup ? W.MergedCellValues.Restart : W.MergedCellValues.Continue);
            }
            SetCell(rows[i], CfoWordTemplateMap.BaseYearNameCol, e.Name);
            SetCell(rows[i], CfoWordTemplateMap.BaseYearValueCol, e.Value);
            SetCell(rows[i], CfoWordTemplateMap.BaseYearRemarkCol, e.Remark);
        }
    }
    #endregion

    #region รูปภาพ
    /// <summary>
    /// [2026-09-23][Claude] แทนกรอบสี่เหลี่ยมเปล่าในเทมเพลตด้วยรูป: ใช้รูปที่ผู้ใช้แนบใน wizard ก่อน ไม่มีจึงใช้รูป default ของระบบ
    /// ย่อ/ขยายรูปให้พอดีกรอบเดิมโดยคงสัดส่วน และคงตำแหน่ง/การจัดวางของกรอบเดิม (inline / anchor)
    /// </summary>
    private void FillImages(MainDocumentPart mainPart, W.Body body, CfoReportDocument doc, CfoWordReportResult result)
    {
        var slots = LocateImageSlots(body);

        foreach (var slot in Enum.GetValues<CfoWordImageSlot>())
        {
            if (!slots.TryGetValue(slot, out var placeholder))
            {
                result.Warnings.Add($"ไม่พบกรอบรูป '{slot}' ในเทมเพลต — ข้ามการใส่รูป");
                continue;
            }

            var bytes = ResolveImage(slot, doc);
            if (bytes == null || bytes.Length == 0)
            {
                result.Warnings.Add($"ไม่พบรูปสำหรับกรอบ '{slot}' (ทั้งรูปที่แนบและรูป default) — คงกรอบเปล่าไว้");
                continue;
            }

            if (!TryReadImageInfo(bytes, out var partType, out var widthPx, out var heightPx))
            {
                result.Warnings.Add($"รูปของกรอบ '{slot}' ไม่ใช่ PNG / JPEG / GIF — คงกรอบเปล่าไว้");
                continue;
            }

            if (!ReplaceWithPicture(mainPart, placeholder, bytes, partType, widthPx, heightPx, $"CFO {slot}"))
            {
                result.Warnings.Add($"กรอบรูป '{slot}' ไม่มีขนาด (wp:extent) — คงกรอบเปล่าไว้");
            }
        }
    }

    /// <summary>
    /// จับคู่กรอบรูป (wps shape) กับตำแหน่ง: กรอบก่อนหัวข้อ "บทนำ" = หน้าปก, นอกนั้นใช้หัวข้อ 3.1.1 / 3.1.2 / 3.1.3 / 7.2 ที่อยู่ก่อนกรอบ
    /// (3.1.1 และ 3.1.2 อยู่ในตารางเดียวกัน จึงไล่ย่อหน้าทุกระดับตามลำดับในเอกสาร)
    /// </summary>
    private static Dictionary<CfoWordImageSlot, W.Drawing> LocateImageSlots(W.Body body)
    {
        var map = new Dictionary<CfoWordImageSlot, W.Drawing>();
        CfoWordImageSlot? current = CfoWordImageSlot.Cover;

        foreach (var p in body.Descendants<W.Paragraph>())
        {
            if (p.Ancestors<W.TextBoxContent>().Any()) continue;

            var text = Normalize(GetTextOutsideTextBoxes(p));
            if (text == CfoWordTemplateMap.IntroductionHeading) current = null;
            else if (text.StartsWith(CfoWordTemplateMap.ImageStructureHeadingPrefix, StringComparison.Ordinal)) current = CfoWordImageSlot.OrganizationStructure;
            else if (text.StartsWith(CfoWordTemplateMap.ImagePlantLayoutHeadingPrefix, StringComparison.Ordinal)) current = CfoWordImageSlot.PlantLayout;
            else if (text.StartsWith(CfoWordTemplateMap.ImageProcessFlowHeadingPrefix, StringComparison.Ordinal)) current = CfoWordImageSlot.ProcessFlow;
            else if (text.StartsWith(CfoWordTemplateMap.ImageDataQualityHeadingPrefix, StringComparison.Ordinal)) current = CfoWordImageSlot.DataQuality;

            if (current == null || map.ContainsKey(current.Value)) continue;

            var shape = p.Descendants<W.Drawing>().FirstOrDefault(d =>
                !d.Ancestors<W.TextBoxContent>().Any()
                && d.Descendants<A.GraphicData>().Any(g => g.Uri?.Value == CfoWordTemplateMap.WordprocessingShapeUri));

            if (shape != null)
            {
                map[current.Value] = shape;
                current = null; // 1 หัวข้อ = 1 กรอบ
            }
        }

        return map;
    }

    private byte[]? ResolveImage(CfoWordImageSlot slot, CfoReportDocument doc) => slot switch
    {
        CfoWordImageSlot.Cover => DecodeBase64Image(doc.OrganizationImageBase64) ?? LoadDefaultImage(CfoWordTemplateMap.DefaultCoverImage),
        CfoWordImageSlot.OrganizationStructure => DecodeBase64Image(doc.Fr031ImageBase64) ?? LoadDefaultImage(CfoWordTemplateMap.DefaultStructureImage),
        CfoWordImageSlot.PlantLayout => LoadDefaultImage(CfoWordTemplateMap.DefaultPlantLayoutImage),
        CfoWordImageSlot.ProcessFlow => LoadDefaultImage(CfoWordTemplateMap.DefaultProcessFlowImage),
        CfoWordImageSlot.DataQuality => LoadDefaultImage(CfoWordTemplateMap.DefaultDataQualityImage),
        _ => null
    };

    /// <summary>data URL ("data:image/png;base64,...") หรือ base64 ล้วน → byte[] (ผิดรูปแบบ = null) — กติกาเดียวกับ Excel</summary>
    private byte[]? DecodeBase64Image(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            int comma = value.IndexOf(',');
            return Convert.FromBase64String((comma >= 0 ? value[(comma + 1)..] : value).Trim());
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "[CFO Word] รูปที่แนบไม่ใช่ base64 ที่ถูกต้อง จะใช้รูป default แทน");
            return null;
        }
    }

    /// <summary>ค้นรูป default ตามลำดับเดียวกับ Excel: wwwroot/images -> Files/Images -> โฟลเดอร์ build</summary>
    private byte[]? LoadDefaultImage(string fileName)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_env.WebRootPath)) candidates.Add(Path.Combine(_env.WebRootPath, "images", fileName));
        if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
        {
            candidates.Add(Path.Combine(_env.ContentRootPath, "Files", "Images", fileName));
            candidates.Add(Path.Combine(_env.ContentRootPath, "wwwroot", "images", fileName));
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Files", "Images", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", fileName));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return File.ReadAllBytes(found);

        _logger.LogWarning("[CFO Word] ไม่พบรูป default ของระบบ: {File}", fileName);
        return null;
    }

    /// <summary>
    /// แทน placeholder ด้วยรูป: โคลน wp:inline / wp:anchor เดิม (คงตำแหน่ง/การตัดคำ) แล้วเปลี่ยนเนื้อในเป็น pic:pic
    /// placeholder อยู่ใน mc:AlternateContent (มี VML fallback) → แทนทั้งก้อน
    /// </summary>
    private static bool ReplaceWithPicture(MainDocumentPart mainPart, W.Drawing placeholder, byte[] bytes,
        PartTypeInfo partType, long widthPx, long heightPx, string name)
    {
        var drawing = (W.Drawing)placeholder.CloneNode(true);
        var extent = drawing.Descendants<DW.Extent>().FirstOrDefault();
        var boxCx = extent?.Cx?.Value ?? 0L;
        var boxCy = extent?.Cy?.Value ?? 0L;
        if (extent == null || boxCx <= 0 || boxCy <= 0) return false;

        // ย่อให้พอดีกรอบ คงสัดส่วนรูป
        var scale = Math.Min((double)boxCx / widthPx, (double)boxCy / heightPx);
        var cx = (long)Math.Round(widthPx * scale);
        var cy = (long)Math.Round(heightPx * scale);
        extent.Cx = cx;
        extent.Cy = cy;

        // กรอบเดิมเผื่อขอบเส้น (effectExtent) — รูปไม่มีเส้นขอบ
        var effect = drawing.Descendants<DW.EffectExtent>().FirstOrDefault();
        if (effect != null)
        {
            effect.LeftEdge = 0L;
            effect.TopEdge = 0L;
            effect.RightEdge = 0L;
            effect.BottomEdge = 0L;
        }

        // กรอบลอย (หน้าปก): จัดรูปให้อยู่กลางพื้นที่กรอบเดิม
        var anchor = drawing.GetFirstChild<DW.Anchor>();
        if (anchor != null)
        {
            ShiftPositionOffset(anchor.GetFirstChild<DW.HorizontalPosition>(), (boxCx - cx) / 2);
            ShiftPositionOffset(anchor.GetFirstChild<DW.VerticalPosition>(), (boxCy - cy) / 2);
        }

        var docPr = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
        if (docPr != null) docPr.Name = name;

        var frameLocks = drawing.Descendants<A.GraphicFrameLocks>().FirstOrDefault();
        if (frameLocks != null) frameLocks.NoChangeAspect = true;

        var graphic = drawing.Descendants<A.Graphic>().FirstOrDefault();
        if (graphic == null) return false;

        var imagePart = mainPart.AddImagePart(partType);
        using (var stream = new MemoryStream(bytes))
        {
            imagePart.FeedData(stream);
        }
        var relId = mainPart.GetIdOfPart(imagePart);

        graphic.GraphicData = new A.GraphicData(
            new PIC.Picture(
                new PIC.NonVisualPictureProperties(
                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                    new PIC.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                new PIC.BlipFill(
                    new A.Blip { Embed = relId },
                    new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(
                    new A.Transform2D(
                        new A.Offset { X = 0L, Y = 0L },
                        new A.Extents { Cx = cx, Cy = cy }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
        };

        OpenXmlElement container = placeholder.Ancestors<AlternateContent>().FirstOrDefault() as OpenXmlElement ?? placeholder;
        container.InsertBeforeSelf(drawing);
        container.Remove();
        return true;
    }

    private static void ShiftPositionOffset(OpenXmlElement? position, long delta)
    {
        var offset = position?.GetFirstChild<DW.PositionOffset>();
        if (offset != null && long.TryParse(offset.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            offset.Text = (value + delta).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>อ่านชนิดและขนาดรูป (px) จาก header ของไฟล์ — ไม่พึ่ง System.Drawing (ใช้ได้ทั้ง Windows/Linux)</summary>
    private static bool TryReadImageInfo(byte[] b, out PartTypeInfo partType, out long width, out long height)
    {
        partType = ImagePartType.Png;
        width = height = 0;

        // PNG: signature 8 ไบต์ + IHDR (width/height big-endian ที่ไบต์ 16..23)
        if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
        {
            width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            partType = ImagePartType.Png;
            return width > 0 && height > 0;
        }

        // GIF: "GIF8" + width/height little-endian ที่ไบต์ 6..9
        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8')
        {
            width = b[6] | (b[7] << 8);
            height = b[8] | (b[9] << 8);
            partType = ImagePartType.Gif;
            return width > 0 && height > 0;
        }

        // JPEG: ไล่ marker หา SOFn (C0..CF ยกเว้น C4, C8, CC)
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                if (b[i] != 0xFF) { i++; continue; }
                byte marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }

                int segmentLength = (b[i + 2] << 8) | b[i + 3];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    height = (b[i + 5] << 8) | b[i + 6];
                    width = (b[i + 7] << 8) | b[i + 8];
                    partType = ImagePartType.Jpeg;
                    return width > 0 && height > 0;
                }
                if (segmentLength < 2) break;
                i += 2 + segmentLength;
            }
        }

        return false;
    }

    /// <summary>ข้อความของย่อหน้า ไม่รวมข้อความในกล่องข้อความ (กรอบรูปในเทมเพลตมีคำแนะนำอยู่ในกล่อง)</summary>
    private static string GetTextOutsideTextBoxes(W.Paragraph paragraph)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var d in paragraph.Descendants())
        {
            if (d is not (W.Text or W.TabChar or W.Break)) continue;
            if (d.Ancestors<W.TextBoxContent>().Any()) continue;
            sb.Append(d is W.Text t ? t.Text : " ");
        }
        return sb.ToString();
    }
    #endregion

    #region ข้อ 3.1.5 / 6.3 / 8 / หัว-ท้ายกระดาษ
    /// <summary>
    /// [2026-09-23][Claude] 3.1.5 ขอบเขตที่เพิ่มเข้ามา/ไม่นับรวม: หน่วยงานที่เลือกในขอบเขต + หน่วยงานที่มีแหล่งปล่อยต่างกันระหว่างปีฐานกับปีรายงาน
    /// เหตุผลของการเปลี่ยนแปลงระบบไม่มีข้อมูล — ผู้ใช้เติมเองใน Word
    /// </summary>
    private static void FillBoundaryChanges(W.Body body, CfoReportDocument doc)
    {
        var statements = new List<string>();

        if (doc.InScopeChildTitles is { Count: > 0 })
        {
            statements.Add($"รายงานฉบับนี้กำหนดขอบเขตองค์กรเฉพาะหน่วยงาน ได้แก่ {string.Join(", ", doc.InScopeChildTitles.Where(t => !string.IsNullOrWhiteSpace(t)))}");
        }

        var calc = doc.CalculateYearReport;
        var baseReport = doc.BaseYearReport;
        if (baseReport != null && baseReport.Year != calc.Year)
        {
            static HashSet<string> Locations(CfoReportService.CfoAuditReportDto r) => r.AuditLines
                .Select(l => l.LocationTitle?.Trim() ?? string.Empty)
                .Where(t => t.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            var calcLocations = Locations(calc);
            var baseLocations = Locations(baseReport);

            // snapshot เก่าไม่มีชื่อหน่วยงาน → เทียบไม่ได้ ไม่เขียนสรุป
            if (calcLocations.Count > 0 && baseLocations.Count > 0)
            {
                var added = calcLocations.Except(baseLocations).OrderBy(t => t, StringComparer.Ordinal).ToList();
                var removed = baseLocations.Except(calcLocations).OrderBy(t => t, StringComparer.Ordinal).ToList();

                if (added.Count > 0)
                    statements.Add($"หน่วยงานที่มีแหล่งปล่อยในปีรายงาน {calc.Year} แต่ไม่มีในปีฐาน {baseReport.Year}: {string.Join(", ", added)}");
                if (removed.Count > 0)
                    statements.Add($"หน่วยงานที่มีแหล่งปล่อยในปีฐาน {baseReport.Year} แต่ไม่มีในปีรายงาน {calc.Year}: {string.Join(", ", removed)}");
                if (added.Count == 0 && removed.Count == 0)
                    statements.Add($"ไม่มีหน่วยงานที่เพิ่มเข้ามาหรือไม่นับรวม เมื่อเทียบระหว่างปีฐาน {baseReport.Year} และปีรายงาน {calc.Year}");
            }
        }

        ReplaceParagraphAfterHeading(body, CfoWordTemplateMap.BoundaryChangeHeadingPrefix, statements);
    }

    /// <summary>
    /// [2026-09-23][Claude] 6.3 ความแตกต่างระหว่างปีฐานกับปีปัจจุบัน: ยอดรวม/รายขอบเขต และรายการแหล่งปล่อยที่หายไปหรือเพิ่มขึ้น
    /// (key เดียวกับข้อ 6.2 และ Excel Fr-04.2) — เหตุผลของความแตกต่างผู้ใช้เติมเอง
    /// </summary>
    private static void FillBaseYearDifferences(W.Body body, CfoReportDocument doc)
    {
        var calc = doc.CalculateYearReport;
        var baseReport = doc.BaseYearReport;
        var statements = new List<string>();

        if (baseReport == null || doc.BaseYear <= 0)
        {
            statements.Add("องค์กรไม่ได้กำหนดปีฐานในรายงานฉบับนี้ จึงไม่มีความแตกต่างระหว่างปีฐานและปีปัจจุบันให้รายงาน");
        }
        else if (baseReport.Year == calc.Year)
        {
            statements.Add($"องค์กรใช้ข้อมูลปีรายงาน {calc.Year} เป็นปีฐาน บัญชีรายการของทั้งสองปีจึงเหมือนกันทุกรายการ และไม่มีความแตกต่างให้รายงาน");
        }
        else
        {
            static string Change(decimal baseValue, decimal calcValue)
            {
                var delta = calcValue - baseValue;
                var text = $"{(delta >= 0 ? "+" : string.Empty)}{FormatTon(delta)} tCO2e";
                return baseValue > 0m
                    ? $"{text} ({(delta >= 0 ? "+" : string.Empty)}{(delta * 100m / baseValue).ToString("0.00", CultureInfo.InvariantCulture)}%)"
                    : text;
            }

            statements.Add(
                $"ปริมาณการปล่อยก๊าซเรือนกระจกรวม (ประเภทที่ 1+2+3) ปีฐาน {baseReport.Year} เท่ากับ {FormatTon(baseReport.TotalGrossEmissions_tCO2e)} tCO2e " +
                $"ปีรายงาน {calc.Year} เท่ากับ {FormatTon(calc.TotalGrossEmissions_tCO2e)} tCO2e เปลี่ยนแปลง {Change(baseReport.TotalGrossEmissions_tCO2e, calc.TotalGrossEmissions_tCO2e)} " +
                $"แยกเป็น ประเภทที่ 1 {Change(baseReport.TotalScope1_tCO2e, calc.TotalScope1_tCO2e)}, " +
                $"ประเภทที่ 2 {Change(baseReport.TotalScope2_tCO2e, calc.TotalScope2_tCO2e)}, " +
                $"ประเภทที่ 3 {Change(baseReport.TotalScope3_tCO2e, calc.TotalScope3_tCO2e)}");

            static string Key(CfoReportService.ActivityAuditLineDto l) => $"{l.TopicId}|{l.ActivityGroupId}|{l.SourceId}";
            var calcKeys = calc.AuditLines.Select(Key).ToHashSet();
            var baseKeys = baseReport.AuditLines.Select(Key).ToHashSet();

            var removed = Ordered(baseReport.AuditLines.Where(l => !calcKeys.Contains(Key(l)))).Select(SourceLabel).Distinct().ToList();
            var added = Ordered(calc.AuditLines.Where(l => !baseKeys.Contains(Key(l)))).Select(SourceLabel).Distinct().ToList();

            if (removed.Count > 0)
                statements.Add($"แหล่งปล่อยที่มีในปีฐานแต่ไม่มีในปีรายงาน ({removed.Count} รายการ): {string.Join(", ", removed)}");
            if (added.Count > 0)
                statements.Add($"แหล่งปล่อยที่เริ่มมีในปีรายงาน ({added.Count} รายการ): {string.Join(", ", added)}");
            if (removed.Count == 0 && added.Count == 0)
                statements.Add("รายการแหล่งปล่อยของปีฐานและปีรายงานตรงกันทุกรายการ ความแตกต่างเกิดจากปริมาณข้อมูลกิจกรรมและค่าการปล่อยของแต่ละแหล่ง");
        }

        ReplaceParagraphAfterHeading(body, CfoWordTemplateMap.BaseYearDifferenceHeadingPrefix, statements);
    }

    /// <summary>
    /// [2026-09-23][Claude] 8. ตารางผลประเมินความไม่แน่นอน: ล้างข้อมูลตัวอย่างของเทมเพลต แล้วเติม ประเภท / รายการ / คะแนนค่า EF (B)
    /// B ประเมินจากแหล่งที่มาของ EF ตามตารางอ้างอิงในเทมเพลต — คะแนนการเก็บข้อมูล (A) ระบบไม่มีข้อมูล จึงเว้น A, AxB, ระดับคุณภาพ
    /// </summary>
    private static void FillUncertainty(Dictionary<TableAnchor, W.Table> tables, CfoReportDocument doc)
    {
        var anchor = CfoWordTemplateMap.UncertaintyResult;
        if (!tables.TryGetValue(anchor, out var table)) return;

        var lines = doc.CalculateYearReport.AuditLines
            .Where(l => l.ScopeNo is eTgoScopeNo.Scope1 or eTgoScopeNo.Scope2 or eTgoScopeNo.Scope3)
            .OrderBy(l => (int)l.ScopeNo)
            .ThenBy(l => CfoReportService.GetSortableCodeKey(l.ActivityGroupCode), StringComparer.Ordinal)
            .ThenBy(l => l.TopicTitle, StringComparer.Ordinal)
            .ToList();

        if (lines.Count == 0)
        {
            ClearDataRows(table, anchor);
            return;
        }

        var rows = ResizeDataRows(table, anchor, lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            SetCell(rows[i], CfoWordTemplateMap.UncertaintyScopeCol, ((int)lines[i].ScopeNo).ToString(CultureInfo.InvariantCulture));
            SetCell(rows[i], CfoWordTemplateMap.UncertaintyNameCol, SourceLabel(lines[i]));
            SetCell(rows[i], CfoWordTemplateMap.UncertaintyEfScoreCol, EfQualityScore(lines[i])?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    /// <summary>คะแนนค่า EF (B) ตามแหล่งที่มาของ EF ที่บันทึกใน Emission Source — ไม่ระบุแหล่ง = null</summary>
    private static int? EfQualityScore(CfoReportService.ActivityAuditLineDto line)
    {
        if (line.SrcSelfCollect) return CfoWordTemplateMap.EfScoreMeasured;
        if (line.SrcSupplier || line.SrcTgoEf || line.SrcThLciDb || line.SrcThaiRes) return CfoWordTemplateMap.EfScoreSupplierOrNational;
        if (line.SrcIntDb) return CfoWordTemplateMap.EfScoreInternational;
        return null;
    }

    /// <summary>
    /// [2026-09-23][Claude] หัวกระดาษ "องค์กร" = ชื่อองค์กร, ท้ายกระดาษ "จัดทำโดย" = ผู้จัดทำ (ชื่อเดียวกับ Excel Fr-01 G4)
    /// "หน่วยงานทวนสอบ" / "ผู้ทวนสอบ" ระบบไม่มีข้อมูล คงว่างไว้
    /// </summary>
    private static void FillHeaderFooter(MainDocumentPart mainPart, CfoReportDocument doc)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [CfoWordTemplateMap.HeaderOrganizationLabel] = doc.OrganizationTitle,
            [CfoWordTemplateMap.FooterPreparedByLabel] = doc.GhgTeamName
        };

        void Fill(OpenXmlPartRootElement? root)
        {
            if (root == null) return;
            foreach (var row in root.Descendants<W.TableRow>())
            {
                var cells = row.Elements<W.TableCell>().ToList();
                for (int i = 0; i < cells.Count - 1; i++)
                {
                    if (values.TryGetValue(Normalize(GetText(cells[i])), out var value)
                        && !string.IsNullOrWhiteSpace(value)
                        && Normalize(GetText(cells[i + 1])).Length == 0)
                    {
                        SetCellText(cells[i + 1], value.Trim());
                    }
                }
            }
            root.Save();
        }

        foreach (var header in mainPart.HeaderParts) Fill(header.Header);
        foreach (var footer in mainPart.FooterParts) Fill(footer.Footer);
    }

    /// <summary>
    /// แทนย่อหน้าแรกหลังหัวข้อ (บรรทัดเส้นประ / คำแนะนำสีน้ำเงิน) ด้วยข้อความ 1 ย่อหน้าต่อ 1 ข้อ — ไม่มีข้อความ = คงฟอร์มเดิม
    /// </summary>
    private static void ReplaceParagraphAfterHeading(W.Body body, string headingPrefix, IReadOnlyList<string> statements)
    {
        if (statements.Count == 0) return;

        var target = body.ChildElements
            .SkipWhile(e => !(e is W.Paragraph hp && Normalize(GetText(hp)).StartsWith(headingPrefix, StringComparison.Ordinal)))
            .Skip(1)
            .FirstOrDefault() as W.Paragraph;
        if (target == null) return;

        OpenXmlElement insertAfter = target;
        for (int i = 0; i < statements.Count; i++)
        {
            var paragraph = i == 0 ? target : (W.Paragraph)target.CloneNode(true);
            SetParagraphText(paragraph, statements[i]);
            if (i > 0)
            {
                insertAfter.InsertAfterSelf(paragraph);
                insertAfter = paragraph;
            }
        }
    }
    #endregion

    #region Data helpers
    private static IEnumerable<CfoReportService.ActivityAuditLineDto> Ordered(IEnumerable<CfoReportService.ActivityAuditLineDto> lines)
        => lines
            .OrderBy(l => CfoReportService.GetSortableCodeKey(l.ActivityGroupCode), StringComparer.Ordinal)
            .ThenBy(l => l.TopicTitle, StringComparer.Ordinal)
            .ThenBy(l => l.SourceName, StringComparer.Ordinal);

    private static bool IsBiogenic(CfoReportService.ActivityAuditLineDto line)
        => (line.TopicTitle ?? string.Empty).TrimStart().StartsWith(CfoWordTemplateMap.BiogenicTitlePrefix, StringComparison.OrdinalIgnoreCase);

    private static string StripBiogenicPrefix(string name)
    {
        var t = name.TrimStart();
        return t.StartsWith(CfoWordTemplateMap.BiogenicTitlePrefix, StringComparison.OrdinalIgnoreCase)
            ? t[CfoWordTemplateMap.BiogenicTitlePrefix.Length..].Trim()
            : name;
    }

    /// <summary>ชื่อแหล่งปล่อย = ชื่อกิจกรรม (ชื่อ EF) ถ้าสองค่าไม่ซ้ำกัน</summary>
    private static string SourceLabel(CfoReportService.ActivityAuditLineDto line)
    {
        var topic = line.TopicTitle?.Trim() ?? string.Empty;
        var source = line.SourceName?.Trim() ?? string.Empty;
        if (topic.Length == 0) return source;
        if (source.Length == 0 || string.Equals(topic, source, StringComparison.OrdinalIgnoreCase)) return topic;
        return $"{topic} ({source})";
    }

    /// <summary>ที่มาของค่า EF: ค่า/หน่วย, แหล่งอ้างอิง</summary>
    private static string DescribeEf(CfoReportService.ActivityAuditLineDto line)
    {
        var text = $"{line.EF.ToString("0.######", CultureInfo.InvariantCulture)} kgCO2e/{line.EfUnitName}";
        if (!string.IsNullOrWhiteSpace(line.OriginSource)) text += $" — {line.OriginSource}";
        if (!string.IsNullOrWhiteSpace(line.EfRef)) text += $"\n{line.EfRef.Trim()}";
        return text;
    }

    /// <summary>Facility เติมได้เฉพาะรายงานระดับ Facility เพราะ audit line ไม่มีข้อมูล Facility</summary>
    private static string FacilityLabel(CfoReportDocument doc)
        => doc.Level == eTopicLevel.Facility ? doc.OrganizationTitle : string.Empty;

    private static string DescribeConsolidationApproach(string? approach)
    {
        if (string.IsNullOrWhiteSpace(approach)) return string.Empty;
        var a = approach.Trim();
        if (a.Contains("Operational", StringComparison.OrdinalIgnoreCase)) return "ควบคุมดำเนินงาน (Operational Control)";
        if (a.Contains("Financial", StringComparison.OrdinalIgnoreCase)) return "ควบคุมทางการเงิน (Financial Control)";
        if (a.Contains("Equity", StringComparison.OrdinalIgnoreCase)) return "สัดส่วนการถือหุ้น (Equity Share)";
        return a;
    }

    private static string GetPeriodText(CfoReportDocument doc)
        => !string.IsNullOrWhiteSpace(doc.CalculatePeriodText) ? doc.CalculatePeriodText.Trim() : doc.PeriodDescription;

    private static (decimal Amount, string Unit) GetProduction(CfoReportDocument doc)
    {
        if (doc.CalcProductItems is { Count: > 0 })
        {
            var amount = doc.CalcProductItems.Sum(x => x.Amount);
            var units = doc.CalcProductItems.Select(x => x.Unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            var unit = units.Count > 0 ? string.Join(", ", units) : (doc.CalculateProductionUnit ?? string.Empty);
            return (amount, unit);
        }

        return (doc.CalculateProductionAmount ?? 0m, doc.CalculateProductionUnit ?? string.Empty);
    }

    private static string FormatTon(decimal value) => value.ToString(TonFormat, CultureInfo.InvariantCulture);

    private static string FormatQuantity(decimal value) => value.ToString("#,##0.####", CultureInfo.InvariantCulture);

    /// <summary>ค่าเป็น 0 แสดง "-" เพื่อให้อ่านตารางแยกก๊าซง่าย</summary>
    private static string FormatGas(decimal value) => value == 0m ? "-" : FormatTon(value);

    private static string FormatThaiDate(DateTime date) => date.ToString("d MMMM yyyy", ThaiCulture);

    private static void WriteGasCells(W.TableRow row, int firstCol, CfoGasBreakdown g)
    {
        var values = new[] { g.CO2, g.FossilCH4, g.CH4, g.N2O, g.SF6, g.NF3, g.HFCs, g.PFCs };
        for (int i = 0; i < values.Length; i++)
        {
            SetCell(row, firstCol + i, FormatGas(values[i]));
        }
    }
    #endregion

    #region OpenXML helpers
    private static string Normalize(string? text) => WhitespaceRegex.Replace(text ?? string.Empty, " ").Trim();

    /// <summary>ข้อความของ element รวม tab / line break เป็นช่องว่าง (w:tab ไม่ใช่ w:t)</summary>
    private static string GetText(OpenXmlElement element)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var d in element.Descendants())
        {
            switch (d)
            {
                case W.Text t: sb.Append(t.Text); break;
                case W.TabChar: sb.Append(' '); break;
                case W.Break: sb.Append(' '); break;
            }
        }
        return sb.ToString();
    }

    private static W.TableRow? GetRow(W.Table table, int index)
        => table.Elements<W.TableRow>().ElementAtOrDefault(index);

    private static W.TableCell? GetCell(W.TableRow row, int index)
        => row.Elements<W.TableCell>().ElementAtOrDefault(index);

    private static W.TableRow? GetFooterRow(W.Table table)
        => table.Elements<W.TableRow>().LastOrDefault();

    private static void SetCell(W.TableRow row, int col, string? value)
    {
        var cell = GetCell(row, col);
        if (cell != null) SetCellText(cell, value);
    }

    private static void SetCellIfValue(W.Table table, int row, int col, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var tr = GetRow(table, row);
        if (tr != null) SetCell(tr, col, value.Trim());
    }

    /// <summary>
    /// แทนข้อความทั้งเซลล์ โดยคง pPr และ rPr (ฟอนต์ไทย w:cs / ขนาด) ของเซลล์เดิม — รองรับหลายบรรทัดด้วย '\n'
    /// </summary>
    private static void SetCellText(W.TableCell cell, string? value)
    {
        var paragraphs = cell.Elements<W.Paragraph>().ToList();
        var first = paragraphs.FirstOrDefault();
        var paraProps = first?.ParagraphProperties?.CloneNode(true) as W.ParagraphProperties;
        var runProps = first != null ? CaptureRunProperties(first) : null;

        foreach (var p in paragraphs) p.Remove();

        var lines = string.IsNullOrEmpty(value)
            ? new[] { string.Empty }
            : value.Replace("\r\n", "\n").Split('\n');

        foreach (var line in lines)
        {
            var p = new W.Paragraph();
            if (paraProps != null) p.AppendChild(paraProps.CloneNode(true));
            if (line.Length > 0) p.AppendChild(CreateRun(runProps, line));
            cell.AppendChild(p);
        }
    }

    /// <summary>แทนข้อความทั้งย่อหน้า โดยคง pPr และ rPr ของ run แรก</summary>
    private static void SetParagraphText(W.Paragraph paragraph, string value)
    {
        var runProps = CaptureRunProperties(paragraph);
        foreach (var r in paragraph.Elements<W.Run>().ToList()) r.Remove();
        paragraph.AppendChild(CreateRun(runProps, value));
    }

    /// <summary>
    /// ต่อท้ายข้อความในย่อหน้า (หน้าปก: "ชื่อองค์กร : [tab]" + ค่า) ใช้ฟอนต์ของ run ข้อความสุดท้าย
    /// ตัดตัวหนาออกเพื่อให้ค่าทุกบรรทัดบนหน้าปกเป็นน้ำหนักเดียวกัน (run ของป้ายแต่ละบรรทัดในเทมเพลตหนาไม่เท่ากัน)
    /// </summary>
    private static void AppendText(W.Paragraph paragraph, string value)
    {
        var lastTextRun = paragraph.Elements<W.Run>().LastOrDefault(r => r.Elements<W.Text>().Any());
        var runProps = lastTextRun?.RunProperties != null
            ? AsValueStyle(lastTextRun.RunProperties, removeBold: true)
            : CaptureRunProperties(paragraph);
        paragraph.AppendChild(CreateRun(runProps, value));
    }

    /// <summary>
    /// rPr ต้นแบบของย่อหน้า: run แรกที่มี rPr, ถ้าไม่มี run ใช้ rPr ของ paragraph mark (เซลล์ว่างในเทมเพลตเก็บฟอนต์ไว้ที่นี่)
    /// </summary>
    private static W.RunProperties? CaptureRunProperties(W.Paragraph paragraph)
    {
        var fromRun = paragraph.Elements<W.Run>().Select(r => r.RunProperties).FirstOrDefault(rp => rp != null);
        if (fromRun != null) return AsValueStyle(fromRun);

        var markProps = paragraph.ParagraphProperties?.ParagraphMarkRunProperties;
        if (markProps == null) return null;

        var runProps = new W.RunProperties();
        foreach (var child in markProps.ChildElements)
        {
            // เครื่องหมาย revision ของ paragraph mark ใช้กับ run ไม่ได้
            if (child is W.Inserted or W.Deleted or W.MoveFrom or W.MoveTo || child.LocalName == "rPrChange") continue;
            runProps.AppendChild(child.CloneNode(true));
        }
        return AsValueStyle(runProps);
    }

    /// <summary>
    /// [2026-09-23][Claude] ช่องกรอกในเทมเพลต อบก. ตั้งรูปแบบ "ข้อความแนะนำ" ไว้ (สีน้ำเงิน / ตัวเอียง / ตัวพิมพ์ใหญ่ / ขีดเส้นใต้ประ)
    /// ค่าที่ระบบเติมต้องเป็นข้อความปกติ จึงคงเฉพาะฟอนต์ ขนาด และตัวหนา แล้วตัดรูปแบบแนะนำออก
    /// (caps ทำให้ภาษาอังกฤษกลายเป็นตัวพิมพ์ใหญ่ทั้งหมด เช่น "1 JAN 2025")
    /// </summary>
    private static W.RunProperties AsValueStyle(W.RunProperties source, bool removeBold = false)
    {
        var rp = (W.RunProperties)source.CloneNode(true);
        rp.Color = null;
        rp.Caps = null;
        rp.SmallCaps = null;
        rp.Italic = null;
        rp.ItalicComplexScript = null;
        rp.Underline = null;
        rp.Highlight = null;

        // เทมเพลตหลายช่องกำหนดเฉพาะขนาดอักษรไทย (szCs) ทำให้ตัวเลข/อังกฤษเล็กกว่าข้อความไทย → ใช้ขนาดเดียวกัน
        if (rp.FontSize == null && rp.FontSizeComplexScript?.Val?.Value is string csSize)
        {
            rp.FontSize = new W.FontSize { Val = csSize };
        }

        if (removeBold)
        {
            rp.Bold = null;
            rp.BoldComplexScript = null;
        }
        return rp;
    }

    private static W.Run CreateRun(W.RunProperties? runProps, string text)
    {
        var run = new W.Run();
        if (runProps != null) run.AppendChild((W.RunProperties)runProps.CloneNode(true));
        run.AppendChild(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    /// <summary>
    /// ปรับจำนวนแถวข้อมูล (ระหว่างหัวตารางกับแถวท้าย) ให้เท่ากับ count โดยโคลนแถวต้นแบบแล้วล้างข้อความ
    /// </summary>
    private static List<W.TableRow> ResizeDataRows(W.Table table, TableAnchor anchor, int count, int? prototypeIndex = null)
    {
        var rows = table.Elements<W.TableRow>().ToList();
        int start = anchor.DataStartRow;
        int endExclusive = rows.Count - anchor.FooterRows;
        if (start < 1 || start >= endExclusive)
        {
            throw new InvalidDataException($"โครงตาราง '{anchor.Name}' ไม่ตรงกับเทมเพลต (แถวข้อมูล {start}..{endExclusive - 1})");
        }

        var prototype = (W.TableRow)rows[prototypeIndex ?? start].CloneNode(true);
        OpenXmlElement insertAfter = rows[start - 1];

        for (int i = start; i < endExclusive; i++) rows[i].Remove();

        var created = new List<W.TableRow>(count);
        for (int i = 0; i < count; i++)
        {
            var row = (W.TableRow)prototype.CloneNode(true);
            ClearRow(row);
            insertAfter.InsertAfterSelf(row);
            insertAfter = row;
            created.Add(row);
        }
        return created;
    }

    /// <summary>ไม่มีข้อมูล: คงจำนวนแถวตามฟอร์ม แต่ล้างข้อความ/เครื่องหมายตัวอย่างออก</summary>
    private static void ClearDataRows(W.Table table, TableAnchor anchor)
    {
        var rows = table.Elements<W.TableRow>().ToList();
        for (int i = anchor.DataStartRow; i < rows.Count - anchor.FooterRows; i++)
        {
            ClearRow(rows[i]);
        }
    }

    private static void ClearRow(W.TableRow row)
    {
        foreach (var cell in row.Elements<W.TableCell>())
        {
            SetCellText(cell, string.Empty);
        }
    }

    private static int FindPlainRowIndex(W.Table table, int fromRow)
    {
        var rows = table.Elements<W.TableRow>().ToList();
        for (int i = fromRow; i < rows.Count; i++)
        {
            var first = rows[i].Elements<W.TableCell>().FirstOrDefault();
            if (first?.TableCellProperties?.VerticalMerge == null) return i;
        }
        return fromRow;
    }

    private static void SetVerticalMerge(W.TableCell cell, W.MergedCellValues? value)
    {
        var tcPr = cell.TableCellProperties;
        if (tcPr == null)
        {
            if (value == null) return;
            tcPr = cell.PrependChild(new W.TableCellProperties());
        }
        tcPr.VerticalMerge = value == null ? null : new W.VerticalMerge { Val = value.Value };
    }
    #endregion
}
