using System.Text.RegularExpressions;
using ClosedXML.Excel;
using static TU.ACAMP.WebApp.Services.Reports.CfoReportService;

namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>[2026-09-24][Claude] ข้อมูลหนึ่งแถวที่อ่านได้จากไฟล์นำเข้า Fr-03.2 (null = ไฟล์ไม่มีคอลัมน์นั้น / เว้นว่าง)</summary>
public sealed class CfoFr032ImportRow
{
    public int RowNumber { get; set; }
    public int CategoryNo { get; set; }
    public bool? HasSource { get; set; }
    public int? InfluenceScore { get; set; }
    public int? RiskScore { get; set; }
    public int? SectorGuidanceScore { get; set; }
    public int? OutsourcingScore { get; set; }
    public int? EngagementScore { get; set; }
    public string? Remark { get; set; }
}

/// <summary>[2026-09-24][Claude] ผลการอ่านไฟล์นำเข้า Fr-03.2 (มี Error แม้แถวเดียว = ไม่นำเข้าเลยทั้งไฟล์)</summary>
public sealed class CfoFr032ImportResult
{
    public List<CfoFr032ImportRow> Rows { get; } = new();
    public List<string> Errors { get; } = new();
    public bool IsValid => Errors.Count == 0 && Rows.Count > 0;
}

/// <summary>
/// [2026-09-24][Claude] Import / Export Excel ของตารางให้คะแนน Fr-03.2 (Scope 3 Significance Assessment) ในหน้า CFO Wizard
/// - Export: ชีต "Fr-03.2 Scoring" (15 หมวด) + ชีต "Scoring Matrix" (เกณฑ์การให้คะแนนชุดเดียวกับที่เขียนลงฟอร์ม)
/// - Import: อ่านเฉพาะคอลัมน์ที่ผู้ใช้แก้ได้ (Has source, Influence, Risk, Sector, Outsourcing, Engagement, Remark)
///   จับคู่แถวด้วยเลขหมวด (No 1..15) ตรวจจับหัวตารางอัตโนมัติ สลับตำแหน่งคอลัมน์ได้
///   Magnitude / Weighted score / Significant เป็นค่าที่ระบบคำนวณ จึงไม่อ่านกลับ
/// </summary>
public class CfoFr032ExcelService
{
    public const string ScoringSheetName = "Fr-03.2 Scoring";
    public const string MatrixSheetName = "Scoring Matrix";
    public const int MaxRemarkLength = 500;

    private const string HNo = "No";
    private const string HCategory = "Category (B)";
    private const string HHasSource = "Has source (C) Y/N";
    private const string HMagnitude = "Magnitude (D) % [system]";
    private const string HInfluence = "Influence (E) 1-5";
    private const string HRisk = "Risk (F) 1-5";
    private const string HSector = "Sector (H) 1-5";
    private const string HOutsourcing = "Outsourcing (K) 1-5";
    private const string HEngagement = "Engagement (N) 1-5";
    private const string HWeighted = "Weighted score [system]";
    private const string HSignificant = "Significant [system]";
    private const string HRemark = "Remark (O)";

    private static readonly Regex LevelRegex = new(@"^\s*(?:ระดับ|level)?\s*([1-5])(?:\.0+)?\s*(?:\(.*\))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    #region Export
    public byte[] Export(IEnumerable<CfoFr032CategoryDto> categories, decimal materialityThreshold, string? organizationTitle)
    {
        #region hide [2026-09-24]
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(ScoringSheetName);

        ws.Cell(1, 1).Value = "Fr-03.2 Scope 3 Significance Assessment";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        ws.Cell(2, 1).Value = string.IsNullOrWhiteSpace(organizationTitle) ? "-" : organizationTitle;
        ws.Cell(3, 1).Value = $"แก้ได้เฉพาะคอลัมน์ Has source (Y/N), คะแนน 1-5 และ Remark — คอลัมน์ [system] ระบบคำนวณเอง ไม่ถูกอ่านตอนนำเข้า | Significance threshold T = {materialityThreshold:0.##}%";
        ws.Cell(3, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;

        const int headerRow = 5;
        var headers = new[] { HNo, HCategory, HHasSource, HMagnitude, HInfluence, HRisk, HSector, HOutsourcing, HEngagement, HWeighted, HSignificant, HRemark };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = headers[c].Contains("[system]") ? XLColor.FromHtml("#64748B") : XLColor.FromHtml("#0F5132");
            cell.Style.Alignment.WrapText = true;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        ws.Row(headerRow).Height = 32;

        int row = headerRow + 1;
        foreach (var cat in (categories ?? Enumerable.Empty<CfoFr032CategoryDto>()).OrderBy(c => c.CategoryNo))
        {
            var eval = Fr032Rubric.Evaluate(cat, materialityThreshold);

            ws.Cell(row, 1).Value = cat.CategoryNo;
            ws.Cell(row, 2).Value = cat.CategoryTitle;
            ws.Cell(row, 3).Value = cat.HasSource ? "Y" : "N";
            ws.Cell(row, 4).Value = (double)cat.MagnitudePercent;
            ws.Cell(row, 4).Style.NumberFormat.Format = "0.00";
            SetScore(ws.Cell(row, 5), cat.InfluenceScore);
            SetScore(ws.Cell(row, 6), cat.RiskScore);
            SetScore(ws.Cell(row, 7), cat.SectorGuidanceScore);
            SetScore(ws.Cell(row, 8), cat.OutsourcingScore);
            SetScore(ws.Cell(row, 9), cat.EngagementScore);
            if (eval.IsAssessed)
            {
                ws.Cell(row, 10).Value = (double)eval.WeightedScore;
                ws.Cell(row, 10).Style.NumberFormat.Format = "0.00";
                ws.Cell(row, 11).Value = eval.IsSignificant ? "Significant" : "Not significant";
            }
            else
            {
                ws.Cell(row, 10).Value = "N/A";
                ws.Cell(row, 11).Value = "N/A";
            }
            ws.Cell(row, 12).Value = cat.Remark ?? string.Empty;

            foreach (var sysCol in new[] { 4, 10, 11 })
            {
                ws.Cell(row, sysCol).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                ws.Cell(row, sysCol).Style.Font.FontColor = XLColor.FromHtml("#475569");
            }
            row++;
        }

        int lastDataRow = Math.Max(row - 1, headerRow + 1);

        // Data validation: Has source = Y/N, คะแนน = จำนวนเต็ม 1..5 (เว้นว่างได้)
        var hasSourceRange = ws.Range(headerRow + 1, 3, lastDataRow, 3);
        var yn = hasSourceRange.CreateDataValidation();
        yn.List("\"Y,N\"", true);
        yn.IgnoreBlanks = true;

        var scoreRange = ws.Range(headerRow + 1, 5, lastDataRow, 9);
        var dv = scoreRange.CreateDataValidation();
        dv.WholeNumber.Between(1, 5);
        dv.IgnoreBlanks = true;
        dv.ErrorTitle = "Score 1-5";
        dv.ErrorMessage = "กรอกระดับคะแนน 1 ถึง 5 หรือเว้นว่าง";

        ws.Range(headerRow, 1, lastDataRow, headers.Length).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Range(headerRow, 1, lastDataRow, headers.Length).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Columns().AdjustToContents();
        ws.Column(2).Width = Math.Min(ws.Column(2).Width, 48);
        ws.Column(12).Width = Math.Max(ws.Column(12).Width, 30);
        foreach (var c in new[] { 3, 4, 5, 6, 7, 8, 9, 10, 11 }) ws.Column(c).Width = Math.Max(ws.Column(c).Width, 14);
        ws.SheetView.FreezeRows(headerRow);

        WriteMatrixSheet(workbook, materialityThreshold);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }

    private static void SetScore(IXLCell cell, int? score)
    {
        if (score.HasValue) cell.Value = score.Value;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void WriteMatrixSheet(XLWorkbook workbook, decimal materialityThreshold)
    {
        #region hide [2026-09-24]
        var ws = workbook.Worksheets.Add(MatrixSheetName);
        ws.Cell(1, 1).Value = Fr032Rubric.GetRubricHeadline(materialityThreshold);
        ws.Cell(1, 1).Style.Font.Bold = true;

        ws.Cell(3, 1).Value = "เกณฑ์ (คอลัมน์ในฟอร์ม)";
        ws.Cell(3, 2).Value = "น้ำหนัก";
        for (int lv = 1; lv <= 5; lv++) ws.Cell(3, 2 + lv).Value = Fr032Rubric.GetScoreLabel(lv);
        var header = ws.Range(3, 1, 3, 7);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");

        int r = 4;
        foreach (var m in Fr032Rubric.GetRubricMatrix(materialityThreshold))
        {
            ws.Cell(r, 1).Value = $"{m.Criterion} [{m.Column}]";
            ws.Cell(r, 2).Value = (double)m.Weight;
            ws.Cell(r, 2).Style.NumberFormat.Format = "0%";
            for (int lv = 0; lv < 5; lv++) ws.Cell(r, 3 + lv).Value = m.Levels[lv];
            r++;
        }
        var table = ws.Range(3, 1, r - 1, 7);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Alignment.WrapText = true;
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        r++;
        foreach (var line in Fr032Rubric.GetRubricFooterLines(materialityThreshold))
        {
            ws.Cell(r, 1).Value = "• " + line;
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.Gray;
            r++;
        }

        ws.Column(1).Width = 34;
        ws.Column(2).Width = 9;
        for (int c = 3; c <= 7; c++) ws.Column(c).Width = 28;
        #endregion
    }
    #endregion

    #region Import
    public CfoFr032ImportResult Parse(Stream stream)
    {
        #region hide [2026-09-24]
        var result = new CfoFr032ImportResult();

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            result.Errors.Add("ไม่สามารถอ่านไฟล์ได้ กรุณาใช้ไฟล์ .xlsx ที่ Export จากหน้านี้");
            return result;
        }

        using (workbook)
        {
            var ws = workbook.Worksheets.FirstOrDefault(w => string.Equals(w.Name, ScoringSheetName, StringComparison.OrdinalIgnoreCase))
                     ?? workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                result.Errors.Add("ไม่พบชีตข้อมูลในไฟล์");
                return result;
            }

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            // ตรวจจับหัวตาราง: แถวแรกที่มีคอลัมน์ "No" และคอลัมน์ที่แก้ได้อย่างน้อย 1 คอลัมน์
            int headerRow = -1;
            var cols = new Dictionary<string, int>();
            for (int r = 1; r <= Math.Min(lastRow, 20) && headerRow == -1; r++)
            {
                var map = new Dictionary<string, int>();
                for (int c = 1; c <= lastCol; c++)
                {
                    var key = MatchHeader(ws.Cell(r, c).GetString());
                    if (key != null && !map.ContainsKey(key)) map[key] = c;
                }
                if (map.ContainsKey(HNo) && map.Keys.Any(k => k != HNo))
                {
                    headerRow = r;
                    cols = map;
                }
            }

            if (headerRow == -1)
            {
                result.Errors.Add("ไม่พบหัวตาราง (ต้องมีคอลัมน์ No และคอลัมน์คะแนนอย่างน้อย 1 คอลัมน์) กรุณาใช้ไฟล์ที่ Export จากหน้านี้");
                return result;
            }

            var seen = new HashSet<int>();
            for (int r = headerRow + 1; r <= lastRow; r++)
            {
                var noText = ws.Cell(r, cols[HNo]).GetString().Trim();
                if (string.IsNullOrEmpty(noText)) continue;

                if (!int.TryParse(noText, out var no) || no < 1 || no > 15)
                {
                    result.Errors.Add($"แถว {r}: No ต้องเป็นเลขหมวด 1-15 (พบ \"{noText}\")");
                    continue;
                }
                if (!seen.Add(no))
                {
                    result.Errors.Add($"แถว {r}: หมวด {no} ซ้ำในไฟล์");
                    continue;
                }

                var item = new CfoFr032ImportRow { RowNumber = r, CategoryNo = no };

                if (cols.TryGetValue(HHasSource, out var cHas))
                {
                    var text = ws.Cell(r, cHas).GetString().Trim();
                    if (text.Length > 0)
                    {
                        var parsed = ParseYesNo(text);
                        if (parsed == null) result.Errors.Add($"แถว {r} (หมวด {no}): Has source ต้องเป็น Y หรือ N (พบ \"{text}\")");
                        item.HasSource = parsed;
                    }
                }

                item.InfluenceScore = ReadScore(ws, r, cols, HInfluence, no, result);
                item.RiskScore = ReadScore(ws, r, cols, HRisk, no, result);
                item.SectorGuidanceScore = ReadScore(ws, r, cols, HSector, no, result);
                item.OutsourcingScore = ReadScore(ws, r, cols, HOutsourcing, no, result);
                item.EngagementScore = ReadScore(ws, r, cols, HEngagement, no, result);

                if (cols.TryGetValue(HRemark, out var cRemark))
                {
                    var remark = ws.Cell(r, cRemark).GetString().Trim();
                    if (remark.Length > MaxRemarkLength)
                    {
                        result.Errors.Add($"แถว {r} (หมวด {no}): Remark ยาวเกิน {MaxRemarkLength} ตัวอักษร");
                    }
                    item.Remark = remark.Length == 0 ? null : remark;
                }

                result.Rows.Add(item);
            }

            if (result.Rows.Count == 0 && result.Errors.Count == 0)
            {
                result.Errors.Add("ไม่พบข้อมูลหมวดในไฟล์");
            }
        }

        return result;
        #endregion
    }

    /// <summary>
    /// นำผลการอ่านไปใส่ในรายการ 15 หมวด (ทับเฉพาะคอลัมน์ที่มีในไฟล์ / หมวดที่ไม่อยู่ในไฟล์ไม่ถูกแตะ)
    /// หมวดที่ Has source = N จะล้างคะแนนทิ้ง (สอดคล้องกับหน้าจอที่ปิดการเลือกคะแนน)
    /// ค่าที่นำเข้าถือว่าผู้ใช้ทบทวนแล้ว จึงเคลียร์ธง IsAutoScored
    /// </summary>
    public int Apply(List<CfoFr032CategoryDto> categories, CfoFr032ImportResult parsed)
    {
        #region hide [2026-09-24]
        if (categories == null || parsed == null || !parsed.IsValid) return 0;

        int applied = 0;
        foreach (var row in parsed.Rows)
        {
            var cat = categories.FirstOrDefault(c => c.CategoryNo == row.CategoryNo);
            if (cat == null) continue;

            if (row.HasSource.HasValue) cat.HasSource = row.HasSource.Value;

            if (!cat.HasSource)
            {
                cat.InfluenceScore = null;
                cat.RiskScore = null;
                cat.SectorGuidanceScore = null;
                cat.OutsourcingScore = null;
                cat.EngagementScore = null;
            }
            else
            {
                cat.InfluenceScore = row.InfluenceScore;
                cat.RiskScore = row.RiskScore;
                cat.SectorGuidanceScore = row.SectorGuidanceScore;
                cat.OutsourcingScore = row.OutsourcingScore;
                cat.EngagementScore = row.EngagementScore;
            }

            cat.Remark = row.Remark;
            cat.IsAutoScored = false;
            applied++;
        }
        return applied;
        #endregion
    }

    private static int? ReadScore(IXLWorksheet ws, int r, Dictionary<string, int> cols, string header, int no, CfoFr032ImportResult result)
    {
        if (!cols.TryGetValue(header, out var c)) return null;
        var cell = ws.Cell(r, c);
        var text = cell.GetString().Trim();
        if (text.Length == 0) return null;

        if (cell.DataType == XLDataType.Number)
        {
            var d = cell.GetDouble();
            if (d >= 1 && d <= 5 && Math.Abs(d - Math.Round(d)) < 1e-9) return (int)Math.Round(d);
        }
        else
        {
            var m = LevelRegex.Match(text);
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }

        var name = header.Split(' ')[0];
        result.Errors.Add($"แถว {r} (หมวด {no}): {name} ต้องเป็นระดับ 1-5 หรือเว้นว่าง (พบ \"{text}\")");
        return null;
    }

    private static bool? ParseYesNo(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        return t switch
        {
            "y" or "yes" or "true" or "1" or "มี" or "✓" or "x" => true,
            "n" or "no" or "false" or "0" or "ไม่มี" or "-" => false,
            _ => null
        };
    }

    /// <summary>จับคู่หัวคอลัมน์ด้วยคำขึ้นต้น (ไม่สนตัวพิมพ์) เพื่อรองรับการสลับตำแหน่งคอลัมน์</summary>
    private static string? MatchHeader(string raw)
    {
        var t = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length == 0) return null;
        if (t == "no" || t == "no." || t == "ลำดับ" || t == "หมวด") return HNo;
        if (t.StartsWith("has source")) return HHasSource;
        if (t.StartsWith("influence")) return HInfluence;
        if (t.StartsWith("risk")) return HRisk;
        if (t.StartsWith("sector")) return HSector;
        if (t.StartsWith("outsourcing")) return HOutsourcing;
        if (t.StartsWith("engagement")) return HEngagement;
        if (t.StartsWith("remark")) return HRemark;
        return null;
    }
    #endregion
}
