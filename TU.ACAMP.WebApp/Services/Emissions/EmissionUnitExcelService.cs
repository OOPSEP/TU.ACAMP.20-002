using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Emissions;

public class EmissionUnitImportItemDto
{
    public int RowNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    public bool IsValid { get; set; } = true;
    public bool IsDuplicated { get; set; } = false;
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// นำเข้า/ส่งออกข้อมูล Emission Unit ผ่านไฟล์ Excel (.xlsx)
/// รองรับเพียง 2 ฟิลด์หลักคือ Title และ Remark พร้อมกลไก Dynamic Column Ordering (ไทย/อังกฤษ)
/// </summary>
public class EmissionUnitExcelService
{
    #region class field
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private const int MaxTitleLength = 200;
    private const int MaxRemarkLength = 500;
    #endregion

    #region class constructor
    public EmissionUnitExcelService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        #region hide [2026-09-06]
        _dbFactory = dbFactory;
        #endregion
    }
    #endregion

    #region class methods - Export
    /// <summary>
    /// ส่งออกรายการหน่วยวัดที่กำลังแสดงอยู่เป็นไฟล์ Excel (โครงสร้างเดียวกับแม่แบบนำเข้า)
    /// </summary>
    public Task<byte[]> ExportUnitsToExcelAsync(List<TbEmissionUnit> units, string contextName = "")
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Emission Units");

        string[] headers = new[] { "#", "Title", "Remark", "Type" };
        WriteHeaderRow(worksheet, headers);

        int rowIdx = 2;
        int seq = 1;

        foreach (var unit in units ?? new List<TbEmissionUnit>())
        {
            worksheet.Cell(rowIdx, 1).Value = seq++;
            worksheet.Cell(rowIdx, 2).Value = unit.Title ?? string.Empty;
            worksheet.Cell(rowIdx, 3).Value = unit.Remark ?? string.Empty;
            worksheet.Cell(rowIdx, 4).Value = unit.SourceType == eSourceType.Default ? "System" : "Custom";
            rowIdx++;
        }

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            var noteCell = worksheet.Cell(rowIdx + 1, 2);
            noteCell.Value = $"Context: {contextName}";
            noteCell.Style.Font.Italic = true;
            noteCell.Style.Font.FontColor = XLColor.Gray;
        }

        worksheet.Columns().AdjustToContents();
        worksheet.Row(1).Height = 25;
        worksheet.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
        #endregion
    }

    /// <summary>
    /// สร้างแม่แบบไฟล์นำเข้า (Title, Remark) พร้อมข้อมูลตัวอย่าง
    /// </summary>
    public Task<byte[]> GenerateTemplateExcelAsync()
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Import Emission Units");

        string[] headers = new[] { "#", "Title*", "Remark" };
        WriteHeaderRow(worksheet, headers);

        object[][] sampleRows = new object[][]
        {
            new object[] { 1, "kg", "Kilogram (กิโลกรัม)" },
            new object[] { 2, "L", "Liter (ลิตร)" },
            new object[] { 3, "kWh", "Kilowatt-hour (กิโลวัตต์-ชั่วโมง)" }
        };

        for (int r = 0; r < sampleRows.Length; r++)
        {
            for (int c = 0; c < sampleRows[r].Length; c++)
            {
                worksheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(sampleRows[r][c]);
            }
        }

        var hint = worksheet.Cell(sampleRows.Length + 3, 2);
        hint.Value = "หมายเหตุ: ระบบตรวจจับหัวตารางอัตโนมัติ สลับตำแหน่งคอลัมน์ได้ และรองรับหัวตารางภาษาไทย (หน่วย / หมายเหตุ)";
        hint.Style.Font.Italic = true;
        hint.Style.Font.FontColor = XLColor.Gray;

        worksheet.Columns().AdjustToContents();
        worksheet.Row(1).Height = 25;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
        #endregion
    }
    #endregion

    #region class methods - Import
    /// <summary>
    /// อ่านไฟล์ Excel นำเข้า ตรวจจับตำแหน่งคอลัมน์อัตโนมัติ และตรวจสอบความถูกต้องเทียบกับฐานข้อมูล
    /// </summary>
    public async Task<List<EmissionUnitImportItemDto>> ParseImportExcelAsync(Stream stream, int? subscriptionId = null)
    {
        #region hide [2026-09-06]
        var results = new List<EmissionUnitImportItemDto>();

        using var context = await _dbFactory.CreateDbContextAsync();

        // หน่วยที่ถือว่าซ้ำ = System Default + Custom ของ Subscription เดียวกัน
        var existingQuery = context.Units.Where(u => !u.IsDeleted);
        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var targetSubId = subscriptionId.Value;
            existingQuery = existingQuery.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == targetSubId);
        }
        else
        {
            existingQuery = existingQuery.Where(u => u.SourceType == eSourceType.Default);
        }

        var existingTitles = await existingQuery
            .Select(u => u.Title)
            .ToListAsync();

        var existingSet = new HashSet<string>(
            existingTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => TrimTitle(t)),
            StringComparer.OrdinalIgnoreCase);

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return results;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 1 || lastCol < 1) return results;

        // Dynamic Column Ordering: สแกนหัวตารางเพื่อหาตำแหน่งคอลัมน์ Title / Remark
        var (headerRow, colTitle, colRemark) = DetectHeader(worksheet, lastRow, lastCol);
        if (colTitle == -1) return results;

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้าย (" ", "  ", หรือมากกว่า รวมถึง NBSP / whitespace)
            var title = TrimTitle(GetCellString(worksheet.Cell(row, colTitle)));
            var remark = colRemark > 0 ? GetCellString(worksheet.Cell(row, colRemark)) : string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(remark)) continue;

            var item = new EmissionUnitImportItemDto
            {
                RowNumber = row,
                Title = title,
                Remark = remark
            };

            if (string.IsNullOrWhiteSpace(title))
            {
                item.IsValid = false;
                item.ValidationErrors.Add("Title is required.");
            }
            else
            {
                if (title.Length > MaxTitleLength)
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add($"Title exceeds {MaxTitleLength} characters.");
                }

                if (!seenInFile.Add(title))
                {
                    item.IsValid = false;
                    item.IsDuplicated = true;
                    item.ValidationErrors.Add("Duplicated title within this file.");
                }
                else if (existingSet.Contains(title))
                {
                    item.IsValid = false;
                    item.IsDuplicated = true;
                    item.ValidationErrors.Add("This unit already exists in the library.");
                }
            }

            if (remark.Length > MaxRemarkLength)
            {
                item.IsValid = false;
                item.ValidationErrors.Add($"Remark exceeds {MaxRemarkLength} characters.");
            }

            results.Add(item);
        }

        return results;
        #endregion
    }
    #endregion

    #region class methods - Private helpers
    private static void WriteHeaderRow(IXLWorksheet worksheet, string[] headers)
    {
        #region hide [2026-09-06]
        var headerBg = XLColor.FromHtml("#059669"); // Emerald 600
        var headerFg = XLColor.White;

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = headerFg;
            cell.Style.Fill.BackgroundColor = headerBg;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }
        #endregion
    }

    /// <summary>
    /// สแกนหาแถวหัวตาราง (5 แถวแรก) และตำแหน่งคอลัมน์ Title / Remark รองรับไทย-อังกฤษ และสลับตำแหน่งได้อิสระ
    /// </summary>
    private static (int HeaderRow, int ColTitle, int ColRemark) DetectHeader(IXLWorksheet worksheet, int lastRow, int lastCol)
    {
        #region hide [2026-09-06]
        int scanLimit = Math.Min(lastRow, 5);

        for (int row = 1; row <= scanLimit; row++)
        {
            int colTitle = -1;
            int colRemark = -1;

            for (int col = 1; col <= lastCol; col++)
            {
                var hText = GetCellString(worksheet.Cell(row, col)).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(hText)) continue;

                if (colRemark == -1 && (hText.Contains("remark") || hText.Contains("description") || hText.Contains("note") ||
                                        hText.Contains("หมายเหตุ") || hText.Contains("คำอธิบาย") || hText.Contains("รายละเอียด")))
                {
                    colRemark = col;
                    continue;
                }

                if (colTitle == -1 && (hText.Contains("title") || hText.Contains("symbol") || hText.Contains("unit") ||
                                       hText.Contains("หน่วย") || hText.Contains("สัญลักษณ์") || hText.Contains("ชื่อ")))
                {
                    colTitle = col;
                }
            }

            if (colTitle != -1) return (row, colTitle, colRemark);
        }

        return (0, -1, -1);
        #endregion
    }

    /// <summary>
    /// [2026-09-19][Antigravity] ตัดช่องว่างหัวและท้าย ทั้ง space เดี่ยว (" "), หลายช่องว่าง ("  "), tabs, NBSP (\u00A0), และ whitespace ทุกชนิด
    /// </summary>
    public static string TrimTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        char[] whitespaceChars = [' ', '\t', '\r', '\n', '\u00A0', '\u200B', '\uFEFF', '\u3000'];
        return title.Trim(whitespaceChars).Trim();
    }

    private static string GetCellString(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return string.Empty;
        try
        {
            var val = cell.Value;
            if (val.IsBlank) return string.Empty;
            if (val.IsText) return val.GetText().Trim();
            if (val.IsNumber) return val.GetNumber().ToString(System.Globalization.CultureInfo.InvariantCulture).Trim();
            return cell.GetFormattedString().Trim();
        }
        catch
        {
            return cell.GetString()?.Trim() ?? string.Empty;
        }
    }
    #endregion
}
