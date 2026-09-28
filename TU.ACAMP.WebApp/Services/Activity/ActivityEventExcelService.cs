using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.WebApp.Services.Activity;

/// <summary>สถานะของแถวนำเข้า Activity Event (แนวคิดเดียวกับ eImportMappingState ของ Topic)</summary>
public enum eEventImportState
{
    Ready = 0,      // ข้อมูลครบและอ่านค่าได้ พร้อมนำเข้า
    NeedsFix = 1,   // อ่านค่าไม่ได้ (เวลา/จำนวน) — ให้ผู้ใช้แก้ไขในแถวได้
    Invalid = 2     // ผิดเชิงโครงสร้าง เช่น ไม่มีทั้งเวลาและจำนวน
}

/// <summary>ทางเลือกเมื่อเดือนนั้นมี Event อยู่แล้วในระบบ</summary>
public enum eExistingEventAction
{
    Skip = 0,
    Overwrite = 1,
    ImportAsNew = 2
}

/// <summary>ทางเลือกระดับแถวของรายการที่ไม่ซ้ำ</summary>
public enum eEventRowAction
{
    Default = 0,
    Skip = 1,
    Remove = 2
}

public class ActivityEventImportItemDto
{
    public int RowNumber { get; set; }

    // ค่าดิบจากไฟล์
    public string TimeText { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    // ค่าที่แปลงแล้ว
    public DateTime? TimeStamp { get; set; }
    public decimal Quantity { get; set; }

    public eEventImportState ImportState { get; set; } = eEventImportState.Ready;
    public bool IsManuallyFixed { get; set; } = false;

    public bool IsValid { get; set; } = true;
    public List<string> ValidationErrors { get; set; } = new();

    /// <summary>ซ้ำกันเองภายในไฟล์เดียวกัน (ข้ามเสมอ)</summary>
    public bool IsDuplicatedInFile { get; set; } = false;

    // Database Matching
    public bool IsExistingEventMatched { get; set; } = false;
    public int? MatchedEventId { get; set; }
    public decimal ExistingQuantity { get; set; }
    public eExistingEventAction ExistingAction { get; set; } = eExistingEventAction.Skip;
    public eEventRowAction RowAction { get; set; } = eEventRowAction.Default;

    public string PeriodText => TimeStamp.HasValue ? TimeStamp.Value.ToString("yyyy-MM") : "-";
}

/// <summary>
/// สร้างแม่แบบ Excel และอ่านไฟล์นำเข้า Activity Event พร้อม Preview รายแถว
/// โครงสร้างคอลัมน์ตรงกับไฟล์ที่ ActivityEventService.ExportToExcel สร้างไว้ (Remark / Time (yyyy-MM) / Quantity)
/// </summary>
public class ActivityEventExcelService
{
    #region class field
    private const int MaxRemarkLength = 500;
    private const int HeaderScanRowLimit = 5;
    #endregion

    #region class methods - Template
    /// <summary>
    /// สร้างแม่แบบนำเข้า: หัวตาราง + ตัวอย่างข้อมูล 3 แถว และคำอธิบายไฟล์ (ตัวสีเทา) ที่แถวที่ 7 โดยเว้นว่าง 2 แถว
    /// </summary>
    public Task<byte[]> GenerateTemplateExcelAsync(string unitTitle = "", string topicTitle = "")
    {
        #region hide [2026-09-14]
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Import Activity Events");

        var qtyHeader = string.IsNullOrWhiteSpace(unitTitle) ? "Quantity*" : $"Quantity* ({unitTitle})";
        string[] headers = { "Remark", "Time (yyyy-MM)*", qtyHeader };
        WriteHeaderRow(ws, headers);

        // ตัวอย่างข้อมูล 3 แถว (แถวที่ 2-4)
        var sampleYear = TU.ACAMP.Domain.Tools.AppTimeZone.LocalNow.Year;
        object[][] sampleRows = new object[][]
        {
            new object[] { "มิเตอร์หลัก อาคารสำนักงาน", $"{sampleYear:0000}-01", 1250.50m },
            new object[] { "มิเตอร์หลัก อาคารสำนักงาน", $"{sampleYear:0000}-02", 1180.75m },
            new object[] { "รวมมิเตอร์สำรอง เดือน มี.ค.", $"{sampleYear:0000}-03", 1320.00m }
        };

        for (int r = 0; r < sampleRows.Length; r++)
        {
            int row = r + 2;

            ws.Cell(row, 1).Value = XLCellValue.FromObject(sampleRows[r][0]);

            var timeCell = ws.Cell(row, 2);
            timeCell.Style.NumberFormat.Format = "@"; // บังคับเป็น Text กัน Excel แปลงเป็นวันที่
            timeCell.Value = sampleRows[r][1].ToString();
            timeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var qtyCell = ws.Cell(row, 3);
            qtyCell.Value = XLCellValue.FromObject(sampleRows[r][2]);
            qtyCell.Style.NumberFormat.Format = "#,##0.00";
        }

        // เว้นว่าง 2 แถว (แถวที่ 5-6) แล้วจึงเป็นคำอธิบายไฟล์ (ตัวเอียงสีเทา) ที่แถวที่ 7
        var note = ws.Cell(7, 1);
        note.Value = string.IsNullOrWhiteSpace(topicTitle)
            ? "ไฟล์นี้คือแม่แบบนำเข้าข้อมูลกิจกรรม (Activity Events) ของ ACAMP — หนึ่งแถวคือข้อมูลของหนึ่งเดือน (รูปแบบ yyyy-MM) โดยแถวที่ 2-4 เป็นตัวอย่าง ให้ลบทิ้งแล้วกรอกข้อมูลจริงแทน"
            : $"ไฟล์นี้คือแม่แบบนำเข้าข้อมูลกิจกรรม (Activity Events) ของ Topic \"{topicTitle}\" — หนึ่งแถวคือข้อมูลของหนึ่งเดือน (รูปแบบ yyyy-MM) หน่วยของ Quantity คือ {(string.IsNullOrWhiteSpace(unitTitle) ? "ตามที่ตั้งไว้ใน Topic" : unitTitle)} โดยแถวที่ 2-4 เป็นตัวอย่าง ให้ลบทิ้งแล้วกรอกข้อมูลจริงแทน";
        note.Style.Font.Italic = true;
        note.Style.Font.FontColor = XLColor.Gray;

        var note2 = ws.Cell(8, 1);
        note2.Value = "หมายเหตุ: ระบบตรวจจับตำแหน่งหัวตารางอัตโนมัติ สลับลำดับคอลัมน์ได้ และรองรับหัวตารางภาษาไทย (หมายเหตุ / เดือน / ปริมาณ)";
        note2.Style.Font.Italic = true;
        note2.Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();
        ws.Column(1).Width = 40;
        ws.Column(3).Width = 18;
        ws.Row(1).Height = 25;
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return Task.FromResult(ms.ToArray());
        #endregion
    }
    #endregion

    #region class methods - Import
    /// <summary>
    /// อ่านไฟล์ Excel นำเข้า ตรวจจับตำแหน่งคอลัมน์อัตโนมัติ และเทียบกับ Event เดิมของ Topic
    /// </summary>
    public Task<List<ActivityEventImportItemDto>> ParseImportExcelAsync(
        Stream stream,
        List<TbActivityEvent>? existingEvents = null)
    {
        #region hide [2026-09-14]
        var results = new List<ActivityEventImportItemDto>();

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheets.FirstOrDefault();
        if (ws == null) return Task.FromResult(results);

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 1 || lastCol < 1) return Task.FromResult(results);

        var (headerRow, colRemark, colTime, colQty) = DetectHeader(ws, lastRow, lastCol);
        if (headerRow == 0)
        {
            // ไม่พบหัวตาราง ใช้ตำแหน่งมาตรฐานเดียวกับไฟล์ Export
            headerRow = 1;
            colRemark = 1;
            colTime = 2;
            colQty = 3;
        }
        if (colTime <= 0) colTime = 2;
        if (colQty <= 0) colQty = 3;
        if (colRemark <= 0) colRemark = 1;

        var seenPeriods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int row = headerRow + 1; row <= lastRow; row++)
        {
            var cellTime = ws.Cell(row, colTime);
            var cellQty = ws.Cell(row, colQty);
            var remark = ws.Cell(row, colRemark).GetString().Trim();

            if (cellTime.IsEmpty() && cellQty.IsEmpty()) continue;
            if (remark.Equals("Total", StringComparison.OrdinalIgnoreCase)) continue;

            var item = new ActivityEventImportItemDto
            {
                RowNumber = row,
                Remark = remark,
                TimeText = cellTime.GetString().Trim(),
                QuantityText = cellQty.GetString().Trim()
            };

            // --- Time ---
            if (cellTime.Value.IsDateTime)
            {
                item.TimeStamp = cellTime.Value.GetDateTime();
                item.TimeText = item.TimeStamp.Value.ToString("yyyy-MM");
            }
            else if (TryParsePeriod(item.TimeText, out var parsed))
            {
                item.TimeStamp = parsed;
            }
            else
            {
                item.ImportState = eEventImportState.NeedsFix;
                item.ValidationErrors.Add(string.IsNullOrWhiteSpace(item.TimeText)
                    ? "ไม่พบค่าเวลา (Time) ในแถวนี้"
                    : $"รูปแบบเวลาไม่ถูกต้อง '{item.TimeText}' ต้องเป็น YYYY-MM");
            }

            // --- Quantity ---
            if (!TryParseQuantity(item.QuantityText, out var qty))
            {
                item.ImportState = eEventImportState.NeedsFix;
                item.ValidationErrors.Add(string.IsNullOrWhiteSpace(item.QuantityText)
                    ? "ไม่พบค่า Quantity ในแถวนี้"
                    : $"Quantity ไม่ใช่ตัวเลข '{item.QuantityText}'");
            }
            else if (qty < 0)
            {
                item.ImportState = eEventImportState.NeedsFix;
                item.ValidationErrors.Add("Quantity ต้องไม่ติดลบ (ISO 14064-1:2018 §5.2.3)");
            }
            else
            {
                item.Quantity = qty;
            }

            if (item.Remark.Length > MaxRemarkLength)
            {
                item.ImportState = eEventImportState.Invalid;
                item.ValidationErrors.Add($"Remark ยาวเกิน {MaxRemarkLength} ตัวอักษร");
            }

            ResolveDuplicateState(item, existingEvents, seenPeriods, registerPeriod: true);
            RefreshValidity(item);

            results.Add(item);
        }

        return Task.FromResult(results);
        #endregion
    }

    /// <summary>
    /// ผู้ใช้แก้ค่าเวลา/จำนวนในแถว Preview เอง แล้วตรวจสอบใหม่ (แนวคิดเดียวกับ ApplyManualSource ของ Topic)
    /// </summary>
    public void ApplyManualValue(
        ActivityEventImportItemDto item,
        DateTime? period,
        decimal? quantity,
        string? remark,
        List<ActivityEventImportItemDto>? allItems = null,
        List<TbActivityEvent>? existingEvents = null)
    {
        #region hide [2026-09-14]
        if (item == null) return;

        item.ValidationErrors.Clear();
        item.ImportState = eEventImportState.Ready;
        item.IsManuallyFixed = true;

        if (remark != null) item.Remark = remark.Trim();

        if (period.HasValue)
        {
            item.TimeStamp = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(period.Value);
            item.TimeText = item.TimeStamp.Value.ToString("yyyy-MM");
        }
        else
        {
            item.TimeStamp = null;
            item.ImportState = eEventImportState.NeedsFix;
            item.ValidationErrors.Add("กรุณาระบุเดือนของข้อมูล (yyyy-MM)");
        }

        if (quantity.HasValue && quantity.Value >= 0)
        {
            item.Quantity = quantity.Value;
            item.QuantityText = quantity.Value.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            item.ImportState = eEventImportState.NeedsFix;
            item.ValidationErrors.Add("กรุณาระบุ Quantity ที่ไม่ติดลบ");
        }

        if (item.Remark.Length > MaxRemarkLength)
        {
            item.ImportState = eEventImportState.Invalid;
            item.ValidationErrors.Add($"Remark ยาวเกิน {MaxRemarkLength} ตัวอักษร");
        }

        // ตรวจซ้ำใหม่ทั้งกับไฟล์เดียวกันและกับฐานข้อมูล
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (allItems != null)
        {
            foreach (var other in allItems)
            {
                if (ReferenceEquals(other, item)) continue;
                if (other.RowAction == eEventRowAction.Remove) continue;
                if (other.TimeStamp.HasValue) seen.Add(other.TimeStamp.Value.ToString("yyyy-MM"));
            }
        }

        item.IsDuplicatedInFile = false;
        item.IsExistingEventMatched = false;
        item.MatchedEventId = null;
        ResolveDuplicateState(item, existingEvents, seen, registerPeriod: false);
        RefreshValidity(item);
        #endregion
    }

    /// <summary>
    /// แปลงแถวที่พร้อมนำเข้าเป็น Entity (Id > 0 = แทนที่รายการเดิม)
    /// </summary>
    public List<TbActivityEvent> BuildEventsToSave(
        IEnumerable<ActivityEventImportItemDto>? items,
        int topicId,
        int subscriptionId)
    {
        #region hide [2026-09-14]
        var events = new List<TbActivityEvent>();
        if (items == null) return events;

        foreach (var item in items)
        {
            if (!IsReady(item) || !item.TimeStamp.HasValue) continue;

            var overwrite = item.IsExistingEventMatched
                            && item.ExistingAction == eExistingEventAction.Overwrite
                            && item.MatchedEventId.HasValue
                            && item.MatchedEventId.Value > 0;

            events.Add(new TbActivityEvent
            {
                Id = overwrite ? item.MatchedEventId!.Value : 0,
                TopicId = topicId,
                SubscriptionId = subscriptionId,
                TimeStamp = item.TimeStamp.Value,
                Quantity = item.Quantity,
                Remark = item.Remark ?? string.Empty
            });
        }

        return events;
        #endregion
    }

    /// <summary>แถวนี้จะถูกนำเข้าจริงหรือไม่ ตามสถานะและ action ที่ผู้ใช้เลือก</summary>
    public bool IsReady(ActivityEventImportItemDto item)
    {
        #region hide [2026-09-14]
        if (item == null) return false;
        if (item.RowAction == eEventRowAction.Remove) return false;
        if (!item.IsValid || item.ImportState != eEventImportState.Ready) return false;
        if (item.IsDuplicatedInFile) return false;
        if (item.IsExistingEventMatched) return item.ExistingAction != eExistingEventAction.Skip;
        return item.RowAction != eEventRowAction.Skip;
        #endregion
    }
    #endregion

    #region class methods - Private helpers
    private static void ResolveDuplicateState(
        ActivityEventImportItemDto item,
        List<TbActivityEvent>? existingEvents,
        HashSet<string> seenPeriods,
        bool registerPeriod)
    {
        #region hide [2026-09-14]
        if (!item.TimeStamp.HasValue) return;

        var period = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(item.TimeStamp.Value);
        item.TimeStamp = period;
        var key = period.ToString("yyyy-MM");

        if (seenPeriods.Contains(key))
        {
            item.IsDuplicatedInFile = true;
            item.ImportState = eEventImportState.Invalid;
            item.ValidationErrors.Add($"เดือน {key} ซ้ำกันเองภายในไฟล์");
            return;
        }

        if (registerPeriod) seenPeriods.Add(key);

        var matched = existingEvents?.FirstOrDefault(e =>
            e.TimeStamp.Year == period.Year && e.TimeStamp.Month == period.Month);

        if (matched != null)
        {
            item.IsExistingEventMatched = true;
            item.MatchedEventId = matched.Id;
            item.ExistingQuantity = matched.Quantity;
        }
        #endregion
    }

    private static void RefreshValidity(ActivityEventImportItemDto item)
    {
        #region hide [2026-09-14]
        item.IsValid = item.ImportState != eEventImportState.Invalid;
        #endregion
    }

    private static bool TryParsePeriod(string text, out DateTime value)
    {
        #region hide [2026-09-14]
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (DateTime.TryParseExact(text,
                new[] { "yyyy-MM", "yyyy/MM", "yyyy-M", "yyyy/M", "yyyy-MM-dd", "yyyy/MM/dd" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return true;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        #endregion
    }

    private static bool TryParseQuantity(string text, out decimal value)
    {
        #region hide [2026-09-14]
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        return decimal.TryParse(text,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
        #endregion
    }

    private static void WriteHeaderRow(IXLWorksheet ws, string[] headers)
    {
        #region hide [2026-09-14]
        var headerBg = XLColor.FromHtml("#059669");
        var headerFg = XLColor.White;

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
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
    /// สแกนหัวตาราง 5 แถวแรก หาตำแหน่งคอลัมน์ Remark / Time / Quantity (รองรับไทย-อังกฤษ และสลับตำแหน่งได้)
    /// </summary>
    private static (int HeaderRow, int ColRemark, int ColTime, int ColQty) DetectHeader(
        IXLWorksheet ws, int lastRow, int lastCol)
    {
        #region hide [2026-09-14]
        int scanLimit = Math.Min(lastRow, HeaderScanRowLimit);

        for (int row = 1; row <= scanLimit; row++)
        {
            int colRemark = -1, colTime = -1, colQty = -1;

            for (int col = 1; col <= lastCol; col++)
            {
                var h = ws.Cell(row, col).GetString().Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(h)) continue;

                if (colQty == -1 && (h.StartsWith("quantity") || h.StartsWith("qty") ||
                                     h.Contains("ปริมาณ") || h.Contains("จำนวน")))
                {
                    colQty = col;
                    continue;
                }

                if (colTime == -1 && (h.StartsWith("time") || h.StartsWith("date") || h.StartsWith("period") ||
                                      h.Contains("เดือน") || h.Contains("วันที่") || h.Contains("ช่วงเวลา")))
                {
                    colTime = col;
                    continue;
                }

                if (colRemark == -1 && (h.StartsWith("remark") || h.StartsWith("note") || h.StartsWith("description") ||
                                        h.Contains("หมายเหตุ") || h.Contains("คำอธิบาย")))
                {
                    colRemark = col;
                }
            }

            if (colTime != -1 || colQty != -1) return (row, colRemark, colTime, colQty);
        }

        return (0, -1, -1, -1);
        #endregion
    }
    #endregion
}
