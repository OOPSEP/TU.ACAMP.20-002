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

public class EmissionGroupImportItemDto
{
    public int RowNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ScopeText { get; set; } = string.Empty;

    public eTgoScopeNo Scope { get; set; } = eTgoScopeNo.Scope1;
    // ISO Category กำหนดได้เฉพาะกลุ่มมาตรฐาน (Default) เท่านั้น กลุ่ม Custom ที่นำเข้าใช้ค่าตั้งต้นเดียวกับปุ่ม Add ในหน้าจอ
    public eIsoCategoryNo IsoCategoryNo { get; set; } = eIsoCategoryNo.Category1;
    public string GeneratedCode { get; set; } = string.Empty;

    public bool IsValid { get; set; } = true;
    public bool IsDuplicated { get; set; } = false;
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// นำเข้า/ส่งออกข้อมูล Emission Group ผ่านไฟล์ Excel (.xlsx)
/// รองรับเพียง 2 ฟิลด์หลักคือ Title และ Scope พร้อมกลไก Dynamic Column Ordering (ไทย/อังกฤษ)
/// รหัสกลุ่ม (Code) ถูกสร้างอัตโนมัติตามขอบเขตการรายงาน โดยกลุ่มที่ผู้ใช้สร้างเองแยก namespace เป็น 03C01, 03C02
/// </summary>
public class EmissionGroupExcelService
{
    #region class field
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private const int MaxTitleLength = 250;

    /// <summary>ตัวคั่นแยก namespace รหัสของกลุ่มที่ผู้ใช้สร้างเอง ออกจากกลุ่มมาตรฐานของระบบ</summary>
    public const string CustomCodeMarker = "C";
    #endregion

    #region class constructor
    public EmissionGroupExcelService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        #region hide [2026-09-06]
        _dbFactory = dbFactory;
        #endregion
    }
    #endregion

    #region class methods - Export
    /// <summary>
    /// ส่งออกรายการกลุ่มการปล่อยที่กำลังแสดงอยู่เป็นไฟล์ Excel
    /// </summary>
    public Task<byte[]> ExportGroupsToExcelAsync(List<TbEmissionGroup> groups, string contextName = "")
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Emission Groups");

        // Code เป็นรหัสภายในระบบ ไม่ส่งออกไปกับไฟล์
        string[] headers = new[] { "#", "Title", "Scope", "Type" };
        WriteHeaderRow(worksheet, headers);

        int rowIdx = 2;
        int seq = 1;

        foreach (var group in (groups ?? new List<TbEmissionGroup>())
            .OrderBy(g => g.TgoScopeNo)
            .ThenBy(g => g.Code))
        {
            worksheet.Cell(rowIdx, 1).Value = seq++;
            worksheet.Cell(rowIdx, 2).Value = group.Title ?? string.Empty;
            worksheet.Cell(rowIdx, 3).Value = FormatScope(group.TgoScopeNo);
            worksheet.Cell(rowIdx, 4).Value = group.SourceType == eSourceType.Default ? "System" : "Custom";
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
    /// สร้างแม่แบบไฟล์นำเข้า (Title, Scope) พร้อมข้อมูลตัวอย่าง
    /// </summary>
    public Task<byte[]> GenerateTemplateExcelAsync()
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Import Emission Groups");

        string[] headers = new[] { "#", "Title*", "Scope*" };
        WriteHeaderRow(worksheet, headers);

        object[][] sampleRows = new object[][]
        {
            new object[] { 1, "Stationary Combustion", "Scope 1" },
            new object[] { 2, "Electricity, grid mix", "Scope 2" },
            new object[] { 3, "Purchased Goods and Services", "Scope 3" },
            new object[] { 4, "Biogenic CO2", "Separate Report" }
        };

        for (int r = 0; r < sampleRows.Length; r++)
        {
            for (int c = 0; c < sampleRows[r].Length; c++)
            {
                worksheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(sampleRows[r][c]);
            }
        }

        // [2026-09-20][Antigravity] ไม่ใส่แถว Hint/หมายเหตุท้ายตาราง และไม่กล่าวถึง Code เพื่อไม่ให้เกิดแถวข้อมูลส่วนเกิน (Extra Row)
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
    /// อ่านไฟล์ Excel นำเข้า ตรวจจับตำแหน่งคอลัมน์อัตโนมัติ ตรวจสอบความถูกต้อง และกำหนดรหัสกลุ่มถัดไปให้แต่ละรายการ
    /// </summary>
    public async Task<List<EmissionGroupImportItemDto>> ParseImportExcelAsync(Stream stream, int? subscriptionId = null)
    {
        #region hide [2026-09-06]
        var results = new List<EmissionGroupImportItemDto>();

        using var context = await _dbFactory.CreateDbContextAsync();

        // กลุ่มที่ถือว่าซ้ำ = System Default + Custom ของ Subscription เดียวกัน
        var existingQuery = context.EmissionGroups.Where(g => !g.IsDeleted);
        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var targetSubId = subscriptionId.Value;
            existingQuery = existingQuery.Where(g => g.SourceType == eSourceType.Default || g.SubscriptionId == targetSubId);
        }
        else
        {
            existingQuery = existingQuery.Where(g => g.SourceType == eSourceType.Default);
        }

        var existingGroups = await existingQuery
            .Select(g => new { g.Title, g.Code, g.TgoScopeNo })
            .ToListAsync();

        var existingTitleKeys = new HashSet<string>(
            existingGroups
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .Select(g => BuildTitleKey(g.TgoScopeNo, g.Title!)),
            StringComparer.OrdinalIgnoreCase);

        // เลขลำดับที่ถูกใช้ไปแล้วของแต่ละ Scope (ใช้จองช่องว่างที่ว่างอยู่ก่อน ไม่ไล่เลขขึ้นเรื่อย ๆ)
        var usedCodeNumbers = new Dictionary<eTgoScopeNo, HashSet<int>>();
        foreach (eTgoScopeNo scope in Enum.GetValues<eTgoScopeNo>())
        {
            usedCodeNumbers[scope] = GetUsedCodeNumbers(existingGroups.Select(g => g.Code), scope, custom: true);
        }

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return results;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 1 || lastCol < 1) return results;

        // Dynamic Column Ordering: สแกนหัวตารางเพื่อหาตำแหน่งคอลัมน์ Title / Scope
        var (headerRow, colTitle, colScope) = DetectHeader(worksheet, lastRow, lastCol);
        if (colTitle == -1) return results;

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้าย (" ", "  ", หรือมากกว่า รวมถึง NBSP / whitespace)
            var title = TrimTitle(GetCellString(worksheet.Cell(row, colTitle)));
            var scopeText = colScope > 0 ? GetCellString(worksheet.Cell(row, colScope)) : string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(scopeText)) continue;

            // [2026-09-20][Antigravity] ข้ามแถวหมายเหตุ/คำแนะนำ (เช่น แถวคำอธิบายท้ายเทมเพลตเก่า) เพื่อป้องกันไม่ให้เกิดแถวข้อมูลส่วนเกิน (Extra Invalid Row)
            if (IsHintOrRemarkRow(title, scopeText)) continue;

            var item = new EmissionGroupImportItemDto
            {
                RowNumber = row,
                Title = title,
                ScopeText = scopeText
            };

            var parsedScope = ParseScope(scopeText);
            if (parsedScope.HasValue)
            {
                item.Scope = parsedScope.Value;
            }
            else
            {
                item.IsValid = false;
                item.ValidationErrors.Add(string.IsNullOrWhiteSpace(scopeText)
                    ? "Scope is required."
                    : $"Unrecognized scope value '{scopeText}'.");
            }

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

                if (parsedScope.HasValue)
                {
                    var key = BuildTitleKey(item.Scope, title);

                    if (!seenInFile.Add(key))
                    {
                        item.IsValid = false;
                        item.IsDuplicated = true;
                        item.ValidationErrors.Add("Duplicated title within the same scope in this file.");
                    }
                    else if (existingTitleKeys.Contains(key))
                    {
                        item.IsValid = false;
                        item.IsDuplicated = true;
                        item.ValidationErrors.Add("This emission group already exists in the library.");
                    }
                }
            }

            if (item.IsValid)
            {
                var slot = TakeNextFreeSlot(usedCodeNumbers[item.Scope]);
                if (slot > 0)
                {
                    item.GeneratedCode = BuildCode(item.Scope, slot, custom: true);
                }
                else
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add($"No available group code left for {FormatScope(item.Scope)} (limit 99).");
                }
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
    /// สแกนหาแถวหัวตาราง (5 แถวแรก) และตำแหน่งคอลัมน์ Title / Scope รองรับไทย-อังกฤษ และสลับตำแหน่งได้อิสระ
    /// </summary>
    private static (int HeaderRow, int ColTitle, int ColScope) DetectHeader(IXLWorksheet worksheet, int lastRow, int lastCol)
    {
        #region hide [2026-09-06]
        int scanLimit = Math.Min(lastRow, 5);

        for (int row = 1; row <= scanLimit; row++)
        {
            int colTitle = -1;
            int colScope = -1;

            for (int col = 1; col <= lastCol; col++)
            {
                var hText = GetCellString(worksheet.Cell(row, col)).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(hText)) continue;

                // [2026-09-19][Antigravity] รองรับหัวข้อ Scope ทุกขอบเขต 1-4 เช่น "ขอบเขต 1-4", "1-4", "scope 1-4", "scope", "ขอบเขต", "separate", "biogenic"
                if (colScope == -1 &&
                    (hText.Contains("scope") ||
                     hText.Contains("ขอบเขต") ||
                     hText.Contains("separate") ||
                     hText.Contains("biogenic") ||
                     hText.Contains("แยกรายงาน") ||
                     hText == "1" || hText == "2" || hText == "3" || hText == "4"))
                {
                    colScope = col;
                    continue;
                }

                if (colTitle == -1 && (hText.Contains("title") || hText.Contains("group") || hText.Contains("name") ||
                                       hText.Contains("ชื่อ") || hText.Contains("กลุ่ม")))
                {
                    colTitle = col;
                }
            }

            if (colTitle != -1) return (row, colTitle, colScope);
        }

        return (0, -1, -1);
        #endregion
    }

    /// <summary>
    /// แปลงข้อความขอบเขตการรายงานเป็น enum รองรับทั้งตัวเลข ภาษาไทย และภาษาอังกฤษ ครบทุกขอบเขต (Scope 1, 2, 3, 4)
    /// </summary>
    private static eTgoScopeNo? ParseScope(string scopeText)
    {
        #region hide [2026-09-19]
        // [2026-09-19][Antigravity] รองรับทุกขอบเขต 1-4 ทั้งตัวเลข ภาษาไทย ("ขอบเขต 1-4", "ขอบเขตที่ 1-4") และภาษาอังกฤษ ("scope 1-4", "scope1-4")
        if (string.IsNullOrWhiteSpace(scopeText)) return null;

        var text = scopeText.Trim().ToLowerInvariant();

        // 1. รับค่าตัวเลข เช่น 1, 2, 3, 4, 1.0, 2.0, 3.0, 4.0
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var decVal))
        {
            var scopeNumber = (int)decVal;
            return scopeNumber switch
            {
                1 => eTgoScopeNo.Scope1,
                2 => eTgoScopeNo.Scope2,
                3 => eTgoScopeNo.Scope3,
                4 => eTgoScopeNo.SeparateReport,
                _ => null
            };
        }

        // 2. Separate Report / Biogenic / ขอบเขต 4
        if (text.Contains("separate") || text.Contains("biogenic") || text.Contains("แยกรายงาน") ||
            text.Contains("รายงานแยก") || text.Contains("นอกขอบเขต") || text.Contains("ชีวภาพ") ||
            text.Contains("ชีวมวล") || text.Contains("biomass") ||
            text.Contains("scope 4") || text.Contains("scope4") || text.Contains("scope-4") ||
            text.Contains("ขอบเขต 4") || text.Contains("ขอบเขต4") || text.Contains("ขอบเขตที่ 4") || text.Contains("ขอบเขตที่4"))
            return eTgoScopeNo.SeparateReport;

        // 3. Scope 3 / ขอบเขต 3
        if (text.Contains("scope 3") || text.Contains("scope3") || text.Contains("scope-3") ||
            text.Contains("ขอบเขต 3") || text.Contains("ขอบเขต3") || text.Contains("ขอบเขตที่ 3") || text.Contains("ขอบเขตที่3") ||
            text.Contains("other indirect") || text.Contains("ทางอ้อมอื่นๆ") || text.Contains("ต้นน้ำ") || text.Contains("ปลายน้ำ") ||
            text.Contains("value chain") || text.Contains("upstream") || text.Contains("downstream"))
            return eTgoScopeNo.Scope3;

        // 4. Scope 2 / ขอบเขต 2
        if (text.Contains("scope 2") || text.Contains("scope2") || text.Contains("scope-2") ||
            text.Contains("ขอบเขต 2") || text.Contains("ขอบเขต2") || text.Contains("ขอบเขตที่ 2") || text.Contains("ขอบเขตที่2") ||
            text.Contains("energy indirect") || text.Contains("indirect energy") || text.Contains("พลังงานทางอ้อม"))
            return eTgoScopeNo.Scope2;

        // 5. Scope 1 / ขอบเขต 1
        if (text.Contains("scope 1") || text.Contains("scope1") || text.Contains("scope-1") ||
            text.Contains("ขอบเขต 1") || text.Contains("ขอบเขต1") || text.Contains("ขอบเขตที่ 1") || text.Contains("ขอบเขตที่1") ||
            text.Contains("direct") || text.Contains("การปล่อยโดยตรง") || text.Contains("โดยตรง"))
            return eTgoScopeNo.Scope1;

        // 6. เผื่อมีตัวเลขผสมในข้อความ (ตรวจ 4, 3, 2, 1 ตามลำดับ)
        if (text.Contains("4")) return eTgoScopeNo.SeparateReport;
        if (text.Contains("3")) return eTgoScopeNo.Scope3;
        if (text.Contains("2")) return eTgoScopeNo.Scope2;
        if (text.Contains("1")) return eTgoScopeNo.Scope1;

        return null;
        #endregion
    }

    public static string FormatScope(eTgoScopeNo scope)
    {
        #region hide [2026-09-06]
        return scope switch
        {
            eTgoScopeNo.Scope1 => "Scope 1",
            eTgoScopeNo.Scope2 => "Scope 2",
            eTgoScopeNo.Scope3 => "Scope 3",
            eTgoScopeNo.SeparateReport => "Separate Report",
            _ => "-"
        };
        #endregion
    }

    private static string GetScopePrefix(eTgoScopeNo scope)
    {
        #region hide [2026-09-06]
        return scope switch
        {
            eTgoScopeNo.Scope1 => "01",
            eTgoScopeNo.Scope2 => "02",
            eTgoScopeNo.Scope3 => "03",
            eTgoScopeNo.SeparateReport => "04",
            _ => "01"
        };
        #endregion
    }

    /// <summary>
    /// รวบรวมลำดับรหัสที่ถูกใช้ไปแล้วของขอบเขตที่กำหนด
    /// custom = true จะอ่านเฉพาะรหัสของกลุ่มที่ผู้ใช้สร้างเอง (รูปแบบ 03C01) แยก namespace ออกจากกลุ่มมาตรฐาน (0301)
    /// </summary>
    public static HashSet<int> GetUsedCodeNumbers(IEnumerable<string?> existingCodes, eTgoScopeNo scope, bool custom)
    {
        #region hide [2026-09-06]
        var prefix = GetScopePrefix(scope);
        var customPrefix = $"{prefix}{CustomCodeMarker}";
        var used = new HashSet<int>();

        foreach (var code in existingCodes)
        {
            if (string.IsNullOrWhiteSpace(code)) continue;

            string codeRaw = code!.Split('-')[0].Split('_')[0].Trim();
            bool isCustomCode = codeRaw.StartsWith(customPrefix, StringComparison.OrdinalIgnoreCase);

            if (custom != isCustomCode) continue;

            // กลุ่มมาตรฐานตัดสองหลักแรก (0301 -> 01) ส่วนกลุ่ม custom ตัด prefix + ตัวคั่น (03C01 -> 01)
            int skip = isCustomCode ? customPrefix.Length : prefix.Length;
            if (!isCustomCode && !codeRaw.StartsWith(prefix)) continue;
            if (codeRaw.Length <= skip) continue;

            if (int.TryParse(codeRaw.Substring(skip), out int number) && number > 0)
            {
                used.Add(number);
            }
        }

        return used;
        #endregion
    }

    /// <summary>
    /// จองลำดับรหัสที่ว่างต่ำสุด (1-99) เพื่อไม่ให้เลขไล่ขึ้นเรื่อย ๆ เมื่อมีผู้ใช้สร้างกลุ่มจำนวนมาก
    /// คืนค่า 0 เมื่อไม่มีช่องว่างเหลือ
    /// </summary>
    public static int TakeNextFreeSlot(HashSet<int> usedNumbers)
    {
        #region hide [2026-09-06]
        for (int i = 1; i <= 99; i++)
        {
            if (usedNumbers.Add(i)) return i;
        }
        return 0;
        #endregion
    }

    /// <summary>
    /// ประกอบรหัสกลุ่มตามขอบเขตและลำดับที่จองได้ เช่น 0301 (มาตรฐาน) หรือ 03C01 (กลุ่มที่ผู้ใช้สร้างเอง)
    /// </summary>
    public static string BuildCode(eTgoScopeNo scope, int slot, bool custom)
    {
        #region hide [2026-09-06]
        var prefix = GetScopePrefix(scope);
        return custom ? $"{prefix}{CustomCodeMarker}{slot:D2}" : $"{prefix}{slot:D2}";
        #endregion
    }

    private static string BuildTitleKey(eTgoScopeNo scope, string title)
    {
        #region hide [2026-09-06]
        return $"{(int)scope}|{TrimTitle(title)}";
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

    /// <summary>
    /// [2026-09-20][Antigravity] ตรวจสอบว่าเป็นแถวหมายเหตุ/คำแนะนำหรือไม่ เพื่อไม่ให้นำมาเป็นรายการข้อมูลนำเข้า
    /// </summary>
    public static bool IsHintOrRemarkRow(string? title, string? scopeText)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;

        var lower = title.Trim().ToLowerInvariant();
        if (lower.StartsWith("หมายเหตุ") ||
            lower.StartsWith("คำแนะนำ") ||
            lower.StartsWith("คำอธิบาย") ||
            lower.StartsWith("note:") ||
            lower.StartsWith("note :") ||
            lower.StartsWith("remark:") ||
            lower.StartsWith("remark :") ||
            lower.StartsWith("*หมายเหตุ") ||
            lower.StartsWith("*note") ||
            lower.StartsWith("*remark"))
        {
            return true;
        }

        // หากไม่มีระบุ Scope และข้อความมีคำว่าหมายเหตุ หรือขึ้นต้นด้วย Note/Remark/*
        if (string.IsNullOrWhiteSpace(scopeText) &&
            (lower.Contains("หมายเหตุ") ||
             lower.StartsWith("note") ||
             lower.StartsWith("remark") ||
             lower.StartsWith("*")))
        {
            return true;
        }

        return false;
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
