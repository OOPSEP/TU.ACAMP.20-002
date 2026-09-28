using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Common;

using Microsoft.AspNetCore.Components.Authorization;

namespace TU.ACAMP.WebApp.Services.Activity;

public class ActivityEventService : BaseService
{
    private readonly ILogger<ActivityEventService> _logger;
    private readonly GhgEventBusService? _eventBus;

    public ActivityEventService(
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<ActivityEventService> logger,
        GhgEventBusService? eventBus = null)
        : base(dbContextFactory, authStateProvider)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    /// <summary>
    /// ดึงรายการ Activity Events ทั้งหมดของ Topic ที่ระบุ
    /// </summary>
    public async Task<List<TbActivityEvent>> GetByTopicIdAsync(int topicId, int? year = null)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Events
                .Where(e => e.TopicId == topicId && !e.IsDeleted);

            if (year.HasValue && year.Value > 0)
            {
                query = query.Where(e => e.TimeStamp.Year == year.Value);
            }

            var events = await query
                .OrderByDescending(e => e.TimeStamp)
                .AsNoTracking()
                .ToListAsync();

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching events for topic ID: {TopicId}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการปีที่ปรากฏใน Activity Events (TimeStamp.Year >= 2000 และ !IsDeleted)
    /// ร่วมกับปีปัจจุบัน (Current Year) เรียงจากมากไปน้อย (Descending)
    /// </summary>
    public async Task<List<int>> GetAvailableReportingYearsAsync(int? subscriptionId = null, IEnumerable<int>? allowedSubscriptionIds = null)
    {
        // [2026-09-17][Gemini] ดึงเฉพาะปีที่มีข้อมูลจริงใน TbActivityEvent รวมกับปีปัจจุบันสำหรับตัวเลือก Reporting Year ในหน้า List
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Events
                .AsNoTracking()
                .Where(e => !e.IsDeleted && e.TimeStamp.Year >= 2000);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(e => e.SubscriptionId == subscriptionId.Value);
            }
            else if (allowedSubscriptionIds != null)
            {
                var allowedList = allowedSubscriptionIds.ToList();
                if (allowedList.Count > 0)
                {
                    query = query.Where(e => allowedList.Contains(e.SubscriptionId));
                }
                else
                {
                    return new List<int> { DateTime.UtcNow.Year };
                }
            }

            var years = await query
                .Select(e => e.TimeStamp.Year)
                .Distinct()
                .ToListAsync();

            var currentYear = DateTime.UtcNow.Year;
            if (!years.Contains(currentYear))
            {
                years.Add(currentYear);
            }

            return years.OrderByDescending(y => y).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available reporting years.");
            return new List<int> { DateTime.UtcNow.Year };
        }
    }

    /// <summary>
    /// ดึงรายการ Activity Event ตาม ID
    /// </summary>
    public async Task<TbActivityEvent?> GetByIdAsync(int id)
    {
        try
        {
            using var context = await CreateContextAsync();
            return await context.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching event for ID: {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// ดึง Activity Event โดยบังคับขอบเขตของ API Key ครบทุกชั้น
    /// (Subscription + ScopeLevel/ScopeTargetId ของ Topic ที่ Event นั้นสังกัด)
    /// คืน null เมื่อไม่พบ หรืออยู่นอกขอบเขตของ Key ผู้เรียกควรตอบ 404 เหมือนกันทั้งสองกรณี
    /// </summary>
    public async Task<TbActivityEvent?> GetByIdForApiKeyScopeAsync(
        int id,
        int subscriptionId,
        TU.ACAMP.Domain.Enums.eTopicLevel? scopeLevel,
        int? scopeTargetId)
    {
        // [2026-09-13][Claude] เพิ่มเมธอดนี้เพราะ API endpoint ฝั่ง attachment/get เดิมตรวจแค่ SubscriptionId
        //     ทำให้ API Key ที่จำกัด Scope ไว้ที่ระดับหนึ่ง (เช่นโรงงานเดียว) อ่านและแนบไฟล์
        //     ข้าม Target อื่นใน Subscription เดียวกันได้ ทั้งที่ฝั่ง Ingestion บังคับ Scope อยู่แล้ว
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Events
                .AsNoTracking()
                .Where(e => e.Id == id && !e.IsDeleted && e.SubscriptionId == subscriptionId);

            if (scopeLevel.HasValue && scopeTargetId.HasValue)
            {
                query = query.Where(e => e.Topic != null
                                      && e.Topic.Level == scopeLevel.Value
                                      && e.Topic.TargetId == scopeTargetId.Value);
            }

            return await query.FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching scoped event for ID: {Id}", id);
            throw;
        }
    }

    /// <summary>
    /// สร้างรายการ Activity Event ใหม่
    /// </summary>
    public async Task<TbActivityEvent> CreateAsync(TbActivityEvent item)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            // [GHG-08] ISO 14064-1:2018 §5.2.3 requires GHG removals to be reported separately from emissions,
            // and prohibits deducting carbon credits/offsets from gross emissions. Non-negative quantity is strictly enforced.
            if (item.Quantity < 0)
            {
                throw new InvalidOperationException("Quantity must be greater than or equal to 0 (Quantity ต้องไม่ติดลบ ตามมาตรฐาน ISO 14064-1:2018 §5.2.3).");
            }

            if (!string.IsNullOrWhiteSpace(item.ExternalRefId))
            {
                item.ExternalRefId = item.ExternalRefId.Trim();
                var exists = await context.Events
                    .AsNoTracking()
                    .AnyAsync(e => e.SubscriptionId == item.SubscriptionId && e.ExternalRefId == item.ExternalRefId && !e.IsDeleted);

                if (exists)
                {
                    throw new InvalidOperationException($"External reference ID '{item.ExternalRefId}' already exists in this workspace.");
                }
            }

            item.TimeStamp = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(item.TimeStamp);
            item.Remark = item.Remark?.Trim() ?? string.Empty;
            context.Events.Add(item);
            await context.SaveChangesAsync(currentUserId);

            // [2026-09-25][Antigravity] แจ้งเตือนไปยัง GhgEventBusService เพื่อให้ Worker ตรวจสอบเกณฑ์การแจ้งเตือนค่า GHG
            if (_eventBus != null)
            {
                _ = _eventBus.PublishEventIngestedAsync(new GhgEventIngestedNotification(
                    item.SubscriptionId,
                    item.TopicId,
                    string.Empty,
                    item.ExternalRefId,
                    item.Quantity,
                    item.TimeStamp));
            }

            _logger.LogInformation("Activity event created for Topic ID {TopicId}: {Qty} at {Date}", item.TopicId, item.Quantity, item.TimeStamp);
            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating activity event for topic ID: {TopicId}", item.TopicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงรายการ Activity Event
    /// </summary>
    public async Task<TbActivityEvent> UpdateAsync(TbActivityEvent item)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var existing = await context.Events.FirstOrDefaultAsync(e => e.Id == item.Id && !e.IsDeleted);
            if (existing == null)
            {
                throw new KeyNotFoundException($"Event ID {item.Id} not found.");
            }

            // [GHG-08] ISO 14064-1:2018 §5.2.3: Non-negative quantity validation
            if (item.Quantity < 0)
            {
                throw new InvalidOperationException("Quantity must be greater than or equal to 0 (Quantity ต้องไม่ติดลบ ตามมาตรฐาน ISO 14064-1:2018 §5.2.3).");
            }

            if (!string.IsNullOrWhiteSpace(item.ExternalRefId))
            {
                item.ExternalRefId = item.ExternalRefId.Trim();
                var exists = await context.Events
                    .AsNoTracking()
                    .AnyAsync(e => e.SubscriptionId == existing.SubscriptionId && e.ExternalRefId == item.ExternalRefId && e.Id != item.Id && !e.IsDeleted);

                if (exists)
                {
                    throw new InvalidOperationException($"External reference ID '{item.ExternalRefId}' already exists in this workspace.");
                }
            }

            var currentUserId = await GetCurrentUserIdAsync();
            existing.TimeStamp = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(item.TimeStamp);
            existing.Quantity = item.Quantity;
            existing.ExternalRefId = item.ExternalRefId;
            existing.Remark = item.Remark?.Trim() ?? string.Empty;
            existing.JsonDataFiles = item.JsonDataFiles ?? System.Text.Json.Nodes.JsonNode.Parse("[]")!;

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Activity event updated: ID {Id}", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating activity event ID: {Id}", item.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบรายการ Activity Event (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var existing = await context.Events.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
            if (existing == null) return false;

            var currentUserId = await GetCurrentUserIdAsync();
            existing.SoftDelete(currentUserId);
            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Activity event deleted: ID {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting activity event ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบ/เคลียร์รายการ Activity Events ทั้งหมดของ Topic (Soft Delete)
    /// </summary>
    public async Task<int> ClearEventsByTopicIdAsync(int topicId)
    {
        #region hide [2026-09-11]
        try
        {
            using var context = await CreateContextAsync();
            var events = await context.Events
                .Where(e => e.TopicId == topicId && !e.IsDeleted)
                .ToListAsync();

            if (!events.Any()) return 0;

            var currentUserId = await GetCurrentUserIdAsync();
            foreach (var ev in events)
            {
                ev.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Cleared {Count} activity events for Topic ID {TopicId}", events.Count, topicId);
            return events.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing activity events for Topic ID: {TopicId}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ส่งออกรายการ Activity Events เป็นไฟล์ Excel (.xlsx) รูปแบบเรียบง่าย (Row 1 เป็น Header, ไม่มีแถว Total)
    /// </summary>
    public byte[] ExportToExcel(
        List<TbActivityEvent> events,
        string topicTitle,
        string unitTitle,
        Func<TbActivityEvent, decimal> emissionsCalculator)
    {
        #region hide [2026-08-20]
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Activity Events");

        // Row 1: Table Headers (Remark, Time (yyyy-MM), Quantity)
        var qtyHeader = string.IsNullOrWhiteSpace(unitTitle) ? "Quantity" : $"Quantity ({unitTitle})";
        var headers = new[] { "Remark", "Time (yyyy-MM)", qtyHeader };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#10b981");
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        }

        // Data Rows starting from Row 2
        int row = 2;
        foreach (var ev in events)
        {
            ws.Cell(row, 1).Value = ev.Remark;
            ws.Cell(row, 2).Value = ev.TimeStamp.ToString("yyyy-MM");
            ws.Cell(row, 3).Value = ev.Quantity;

            ws.Cell(row, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;
            ws.Cell(row, 2).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }

    /// <summary>
    /// [GHG-16] ตัวเลือกการจัดการข้อมูลซ้ำตอนนำเข้า Excel
    /// </summary>
    public enum DuplicateHandlingMode
    {
        Skip = 0,      // ข้ามรายการซ้ำ (Default)
        Replace = 1,   // แทนที่รายการเดิม
        AddNew = 2     // เพิ่มเป็นรายการใหม่
    }

    /// <summary>
    /// นำเข้าข้อมูล Activity Events จากไฟล์ Excel (.xlsx)
    /// โดยยึด Row 1 เป็น Header และค้นหาตำแหน่ง Column ตามชื่อ Header (รองรับการสลับตำแหน่ง Column)
    /// </summary>
    public (List<TbActivityEvent> importedEvents, List<string> errors, List<string> warnings) ImportFromExcel(
        System.IO.Stream stream,
        int topicId,
        int subscriptionId,
        List<TbActivityEvent>? existingEvents = null,
        DuplicateHandlingMode duplicateMode = DuplicateHandlingMode.Skip)
    {
        #region hide [2026-08-20]
        var importedEvents = new List<TbActivityEvent>();
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var ws = workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                errors.Add("Excel file contains no worksheets.");
                return (importedEvents, errors, warnings);
            }

            var firstRow = ws.Row(1);
            int remarkCol = -1;
            int timeCol = -1;
            int qtyCol = -1;

            // ค้นหาตำแหน่ง Column ตามชื่อ Header ใน Row 1
            foreach (var cell in firstRow.CellsUsed())
            {
                var val = cell.Value.ToString().Trim();
                int colNum = cell.Address.ColumnNumber;
                if (val.StartsWith("Remark", StringComparison.OrdinalIgnoreCase) || val.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                {
                    remarkCol = colNum;
                }
                else if (val.StartsWith("Time", StringComparison.OrdinalIgnoreCase) || val.StartsWith("Date", StringComparison.OrdinalIgnoreCase))
                {
                    timeCol = colNum;
                }
                else if (val.StartsWith("Quantity", StringComparison.OrdinalIgnoreCase) || val.StartsWith("Qty", StringComparison.OrdinalIgnoreCase))
                {
                    qtyCol = colNum;
                }
            }

            // Fallback ตำแหน่งคอลัมน์มาตรฐานหากค้นหาจากชื่อไม่พบ
            if (timeCol == -1) timeCol = 2;
            if (qtyCol == -1) qtyCol = 3;
            if (remarkCol == -1) remarkCol = 1;

            // อ่านแถวข้อมูลตั้งแต่ Row 2 เป็นต้นไป
            var usedRows = ws.RowsUsed().Skip(1);

            foreach (var row in usedRows)
            {
                int rowNumber = row.RowNumber();
                var cellRemark = remarkCol > 0 ? row.Cell(remarkCol).Value : default;
                var cellTime = timeCol > 0 ? row.Cell(timeCol).Value : default;
                var cellQty = qtyCol > 0 ? row.Cell(qtyCol).Value : default;

                if (cellTime.IsBlank && cellQty.IsBlank) continue;
                if (cellRemark.ToString().Trim().Equals("Total", StringComparison.OrdinalIgnoreCase)) continue;

                DateTime timeStamp = TU.ACAMP.Domain.Tools.AppTimeZone.LocalNow.Date;
                string timeStr = cellTime.ToString().Trim();

                if (cellTime.IsDateTime)
                {
                    timeStamp = cellTime.GetDateTime();
                }
                else if (System.DateTime.TryParseExact(timeStr, new[] { "yyyy-MM", "yyyy/MM", "yyyy-M", "yyyy/M", "yyyy-MM-dd", "yyyy/MM/dd" },
                         System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
                {
                    timeStamp = parsedDate;
                }
                else if (System.DateTime.TryParse(timeStr, out var parsedAny))
                {
                    timeStamp = parsedAny;
                }
                else
                {
                    errors.Add($"Row {rowNumber}: Invalid time format '{timeStr}'. Use YYYY-MM (e.g., 2026-08).");
                    continue;
                }

                // [GHG-16] Parse Quantity with NumberStyles.Number and CultureInfo.InvariantCulture
                if (!decimal.TryParse(cellQty.ToString(), System.Globalization.NumberStyles.Number | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var qty))
                {
                    errors.Add($"Row {rowNumber}: Invalid quantity '{cellQty}'. Must be a numeric value.");
                    continue;
                }

                // [GHG-08] Quantity must not be negative
                if (qty < 0)
                {
                    errors.Add($"Row {rowNumber}: Quantity ต้องไม่ติดลบ");
                    continue;
                }

                var reportingPeriod = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(timeStamp);

                // [GHG-16] Check for duplicate event in the same reporting period
                if (existingEvents != null && existingEvents.Any(e => e.TimeStamp.Year == reportingPeriod.Year && e.TimeStamp.Month == reportingPeriod.Month))
                {
                    var matchedExisting = existingEvents.First(e => e.TimeStamp.Year == reportingPeriod.Year && e.TimeStamp.Month == reportingPeriod.Month);
                    if (duplicateMode == DuplicateHandlingMode.Skip)
                    {
                        warnings.Add($"Row {rowNumber}: ข้ามรายการเดือน {reportingPeriod:yyyy-MM} เนื่องจากมีข้อมูลกิจกรรมอยู่แล้วในระบบ");
                        continue;
                    }
                    else if (duplicateMode == DuplicateHandlingMode.Replace)
                    {
                        warnings.Add($"Row {rowNumber}: แทนที่รายการเดิมของเดือน {reportingPeriod:yyyy-MM}");
                        var evReplace = new TbActivityEvent
                        {
                            Id = matchedExisting.Id,
                            TopicId = topicId,
                            SubscriptionId = subscriptionId,
                            TimeStamp = reportingPeriod,
                            Quantity = qty,
                            Remark = cellRemark.ToString()?.Trim() ?? string.Empty
                        };
                        importedEvents.Add(evReplace);
                        continue;
                    }
                    else
                    {
                        warnings.Add($"Row {rowNumber}: เพิ่มรายการซ้ำของเดือน {reportingPeriod:yyyy-MM} เป็นรายการใหม่");
                    }
                }

                var ev = new TbActivityEvent
                {
                    TopicId = topicId,
                    SubscriptionId = subscriptionId,
                    TimeStamp = reportingPeriod,
                    Quantity = qty,
                    Remark = cellRemark.ToString()?.Trim() ?? string.Empty
                };

                importedEvents.Add(ev);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to parse Excel file: {ex.Message}");
        }

        return (importedEvents, errors, warnings);
        #endregion
    }
}
