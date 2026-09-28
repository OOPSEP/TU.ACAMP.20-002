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

public enum eExistingSourceAction
{
    Skip = 0,
    Overwrite = 1,
    ImportAsNew = 2
}

public enum eRowAction
{
    Default = 0,
    Skip = 1,
    Remove = 2
}

public class EmissionSourceImportItemDto
{
    public int RowNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string GroupText { get; set; } = string.Empty;
    public string UnitText { get; set; } = string.Empty;
    public string ScopeText { get; set; } = string.Empty;
    public decimal EF { get; set; }

    // GHG Breakdowns
    public decimal CO2 { get; set; }
    public decimal FossilCH4 { get; set; }
    public decimal CH4 { get; set; }
    public decimal N2O { get; set; }
    public decimal SF6 { get; set; }
    public decimal NF3 { get; set; }
    public decimal HFCs { get; set; }
    public decimal PFCs { get; set; }

    // GWP Values
    public decimal gwpCO2 { get; set; } = 1m;
    public decimal gwpFossilCH4 { get; set; } = 30m;
    public decimal gwpCH4 { get; set; } = 28m;
    public decimal gwpN2O { get; set; } = 265m;
    public decimal gwpSF6 { get; set; } = 23500m;
    public decimal gwpNF3 { get; set; } = 16100m;
    public decimal gwpHFCs { get; set; } = 1m;
    public decimal gwpPFCs { get; set; } = 1m;

    public eGwpVersion GwpVersion { get; set; } = eGwpVersion.AR5;
    public string Ref { get; set; } = string.Empty;

    // Resolved / Matched properties
    public int? GroupId { get; set; }
    public string MatchedGroupTitle { get; set; } = string.Empty;
    public eTgoScopeNo Scope { get; set; } = eTgoScopeNo.Scope1;
    public int? UnitId { get; set; }
    public string MatchedUnitTitle { get; set; } = string.Empty;

    /// <summary>true เมื่อกลุ่มมาจากค่าตั้งต้นที่ผู้ใช้เลือก ไม่ได้มาจากไฟล์</summary>
    public bool IsGroupFromDefault { get; set; } = false;
    /// <summary>true เมื่อหน่วยมาจากค่าตั้งต้นที่ผู้ใช้เลือก ไม่ได้มาจากไฟล์</summary>
    public bool IsUnitFromDefault { get; set; } = false;

    // Existing / Duplicate Detection & Resolution
    public bool IsExistingMatched { get; set; } = false;
    public int? MatchedExistingSourceId { get; set; }
    public bool IsExistingDefaultMaster { get; set; } = false;
    public eExistingSourceAction ExistingAction { get; set; } = eExistingSourceAction.Skip;

    public bool IsValid { get; set; } = true;
    public bool IsDuplicated { get; set; } = false;
    public eRowAction RowAction { get; set; } = eRowAction.Default;
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// นำเข้า/ส่งออกข้อมูล Emission Source ผ่านไฟล์ Excel (.xlsx)
/// นำเข้าใช้ 4 ฟิลด์หลัก Title, Group, Unit, EF โดยเก็บค่า EF ลง CO2 และตั้ง gwpCO2 = 1
/// มีคอลัมน์ Scope เป็นตัวเลือกเสริม ใช้เฉพาะกรณีชื่อกลุ่มซ้ำกันข้ามขอบเขต
/// ส่งออกครบทุกก๊าซและค่า GWP ตามที่หน้าจอแสดง พร้อมกลไก Dynamic Column Ordering (ไทย/อังกฤษ)
/// </summary>
public class EmissionSourceExcelService
{
    #region class field
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private const int MaxTitleLength = 200;
    #endregion

    #region class constructor
    public EmissionSourceExcelService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        #region hide [2026-09-06]
        _dbFactory = dbFactory;
        #endregion
    }
    #endregion

    #region class methods - Export
    /// <summary>
    /// ส่งออกรายการแหล่งกำเนิดที่กำลังแสดงอยู่เป็นไฟล์ Excel (ครบทุกก๊าซและค่า GWP ตามหน้าจอ)
    /// </summary>
    public Task<byte[]> ExportSourcesToExcelAsync(List<TbEmissionSource> sources, string contextName = "")
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Emission Sources");

        string[] headers = new[]
        {
            "#", "Title", "Group", "Scope", "Unit", "EF (kgCO2e)",
            "CO2", "Fossil CH4", "CH4", "N2O", "SF6", "NF3", "HFCs", "PFCs",
            "GWP-CO2", "GWP-Fossil CH4", "GWP-CH4", "GWP-N2O", "GWP-SF6", "GWP-NF3", "GWP-HFCs", "GWP-PFCs",
            "GWP Version", "Data Source", "Ref", "Date Approved", "Date Expire", "Remark", "Type"
        };
        WriteHeaderRow(worksheet, headers);

        int rowIdx = 2;
        int seq = 1;

        foreach (var src in (sources ?? new List<TbEmissionSource>())
            .OrderBy(s => s.Group?.TgoScopeNo)
            .ThenBy(s => s.Title))
        {
            int col = 1;
            worksheet.Cell(rowIdx, col++).Value = seq++;
            worksheet.Cell(rowIdx, col++).Value = src.Title ?? string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.Group?.Title ?? string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.Group != null ? FormatScope(src.Group.TgoScopeNo) : "-";
            worksheet.Cell(rowIdx, col++).Value = src.Unit?.Title ?? string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.EF;

            worksheet.Cell(rowIdx, col++).Value = src.CO2;
            worksheet.Cell(rowIdx, col++).Value = src.FossilCH4;
            worksheet.Cell(rowIdx, col++).Value = src.CH4;
            worksheet.Cell(rowIdx, col++).Value = src.N2O;
            worksheet.Cell(rowIdx, col++).Value = src.SF6;
            worksheet.Cell(rowIdx, col++).Value = src.NF3;
            worksheet.Cell(rowIdx, col++).Value = src.HFCs;
            worksheet.Cell(rowIdx, col++).Value = src.PFCs;

            worksheet.Cell(rowIdx, col++).Value = src.gwpCO2;
            worksheet.Cell(rowIdx, col++).Value = src.gwpFossilCH4;
            worksheet.Cell(rowIdx, col++).Value = src.gwpCH4;
            worksheet.Cell(rowIdx, col++).Value = src.gwpN2O;
            worksheet.Cell(rowIdx, col++).Value = src.gwpSF6;
            worksheet.Cell(rowIdx, col++).Value = src.gwpNF3;
            worksheet.Cell(rowIdx, col++).Value = src.gwpHFCs;
            worksheet.Cell(rowIdx, col++).Value = src.gwpPFCs;

            worksheet.Cell(rowIdx, col++).Value = src.GwpVersion.ToString();
            worksheet.Cell(rowIdx, col++).Value = FormatDataSources(src);
            worksheet.Cell(rowIdx, col++).Value = src.Ref ?? string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.DateApproved.HasValue ? src.DateApproved.Value.ToString("yyyy-MM-dd") : string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.DateExpire.HasValue ? src.DateExpire.Value.ToString("yyyy-MM-dd") : string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.Remark ?? string.Empty;
            worksheet.Cell(rowIdx, col++).Value = src.SourceType == eSourceType.Default ? "System" : "Custom";

            // EF และค่าก๊าซแสดงทศนิยม 4 ตำแหน่ง
            worksheet.Range(rowIdx, 6, rowIdx, 22).Style.NumberFormat.Format = "0.0000";
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
    /// สร้างแม่แบบไฟล์นำเข้า (Title, Group, Unit, EF) พร้อมข้อมูลตัวอย่างและรายการกลุ่ม/หน่วยที่เลือกได้
    /// </summary>
    public async Task<byte[]> GenerateTemplateExcelAsync(int? subscriptionId = null)
    {
        #region hide [2026-09-06]
        using var workbook = new XLWorkbook();

        // Sheet 1: Template
        var wsTemplate = workbook.Worksheets.Add("Import Emission Sources");

        // [2026-09-19][Antigravity] ตัด Total EF ออกจากแม่แบบเนื่องจากไม่ได้ใช้ตอน import และระบบคำนวณ EF รวมให้อัตโนมัติจากก๊าซ GHG
        string[] headers = new[]
        {
            "#", "Title*", "Emission", "Scope", "Unit*",
            "CO2", "Fossil CH4", "CH4", "N2O", "SF6", "NF3", "HFCs", "PFCs",
            "Reference"
        };
        WriteHeaderRow(wsTemplate, headers);

        // ตัวอย่างข้อมูลอ้างอิงตามมาตรฐาน TGO (อบก.) ที่มีแยกก๊าซ GHG ชัดเจน (ไม่รวม Total EF)
        object[][] sampleRows = new object[][]
        {
            new object[] { 1, "ก๊าซธรรมชาติ (Natural Gas)", "Stationary Combustion", "Scope 1", "scf", 0.057222, 0.0, 0.00000102, 0.000000102, 0.0, 0.0, 0.0, 0.0, "IPCC 2006" },
            new object[] { 2, "น้ำมันดีเซล (Diesel Oil)", "Mobile Combustion", "Scope 1", "L", 2.7080, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "TGO 2024" },
            new object[] { 3, "ไฟฟ้า grid mix", "Purchased Electricity", "Scope 2", "kWh", 0.4750, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "EPPO / TGO" },
            new object[] { 4, "น้ำประปา (Tap Water)", "Purchased Goods and Services", "Scope 3", "m3", 0.7200, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "TGO" }
        };

        for (int r = 0; r < sampleRows.Length; r++)
        {
            for (int c = 0; c < sampleRows[r].Length; c++)
            {
                wsTemplate.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(sampleRows[r][c]);
            }
            // จัดรูปแบบทศนิยม 4-6 ตำแหน่งสำหรับคอลัมน์ก๊าซ (คอลัมน์ 6 ถึง 13)
            wsTemplate.Range(r + 2, 6, r + 2, 13).Style.NumberFormat.Format = "0.0000##";
        }

        // [2026-09-19][Antigravity] ปรับความกว้างคอลัมน์ก่อนใส่ hint เพื่อไม่ให้คอลัมน์ Title ขยายกว้างเกินไปตามข้อความหมายเหตุ และล็อกความกว้างคอลัมน์ Title ให้กระชับเหมาะสม
        wsTemplate.Columns().AdjustToContents();
        wsTemplate.Column(2).Width = 32;
        wsTemplate.Row(1).Height = 25;
        wsTemplate.SheetView.FreezeRows(1);

        var hint = wsTemplate.Cell(sampleRows.Length + 3, 1);
        hint.Value = "Note: Total EF is automatically calculated by the system from GHG sources (CO2, Fossil CH4, CH4, N2O, SF6, NF3, HFCs, PFCs) based on GWP AR5 standard. Emission Group, Scope, and Unit can be selected in the import dialog if left blank (see valid values in Reference sheet).";
        hint.Style.Font.Italic = true;
        hint.Style.Font.FontColor = XLColor.Gray;

        // Sheet 2: Reference (กลุ่มและหน่วยที่ใช้ได้จริงในระบบ)
        using var context = await _dbFactory.CreateDbContextAsync();
        var groups = await BuildGroupQuery(context, subscriptionId)
            .Select(g => new { g.Title, g.TgoScopeNo })
            .ToListAsync();
        var units = await BuildUnitQuery(context, subscriptionId)
            .Select(u => u.Title)
            .ToListAsync();

        var wsRef = workbook.Worksheets.Add("Reference");
        WriteHeaderRow(wsRef, new[] { "Emission", "Scope", "Unit" });

        int refRow = 2;
        foreach (var g in groups.OrderBy(g => g.TgoScopeNo).ThenBy(g => g.Title))
        {
            wsRef.Cell(refRow, 1).Value = g.Title ?? string.Empty;
            wsRef.Cell(refRow, 2).Value = FormatScope(g.TgoScopeNo);
            refRow++;
        }

        int unitRow = 2;
        foreach (var u in units.OrderBy(u => u))
        {
            wsRef.Cell(unitRow, 3).Value = u ?? string.Empty;
            unitRow++;
        }

        wsRef.Columns().AdjustToContents();
        wsRef.Row(1).Height = 25;

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
        #endregion
    }
    #endregion

    #region class methods - Import
    /// <summary>
    /// อ่านไฟล์ Excel นำเข้า ตรวจจับตำแหน่งคอลัมน์อัตโนมัติ จับคู่กลุ่ม/หน่วยกับฐานข้อมูล และตรวจสอบความถูกต้อง
    /// </summary>
    public async Task<List<EmissionSourceImportItemDto>> ParseImportExcelAsync(
        Stream stream,
        int? subscriptionId = null,
        eTgoScopeNo? defaultScope = null,
        int? defaultGroupId = null,
        int? defaultUnitId = null)
    {
        #region hide [2026-09-06]
        var results = new List<EmissionSourceImportItemDto>();

        using var context = await _dbFactory.CreateDbContextAsync();

        var groups = await BuildGroupQuery(context, subscriptionId)
            .Select(g => new { g.Id, g.Title, g.TitleTh, g.Code, g.TgoScopeNo })
            .ToListAsync();

        var units = await BuildUnitQuery(context, subscriptionId)
            .Select(u => new { u.Id, u.Title })
            .ToListAsync();

        var existingSourceQuery = context.EmissionSources.Where(s => !s.IsDeleted);
        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var targetSubId = subscriptionId.Value;
            existingSourceQuery = existingSourceQuery.Where(s => s.SourceType == eSourceType.Default || s.SubscriptionId == targetSubId);
        }
        else
        {
            existingSourceQuery = existingSourceQuery.Where(s => s.SourceType == eSourceType.Default);
        }

        var existingSources = await existingSourceQuery
            .Select(s => new { s.Id, s.Title, s.SourceType })
            .ToListAsync();

        var existingDict = new Dictionary<string, (int Id, eSourceType SourceType)>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingSources.Where(s => !string.IsNullOrWhiteSpace(s.Title)))
        {
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวและท้ายชื่อก่อนทำ indexing ใน dictionary
            var trimmed = TrimTitle(s.Title);
            if (!existingDict.ContainsKey(trimmed))
            {
                existingDict[trimmed] = (s.Id, s.SourceType);
            }
        }

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet == null) return results;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow < 1 || lastCol < 1) return results;

        // Dynamic Column Ordering: สแกนหัวตารางเพื่อหาตำแหน่งคอลัมน์ Title / Group / Unit / EF
        var header = DetectHeader(worksheet, lastRow, lastCol);
        if (header.ColTitle == -1) return results;

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int row = header.HeaderRow + 1; row <= lastRow; row++)
        {
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้าย (" ", "  ", หรือมากกว่า รวมถึง NBSP / whitespace)
            var title = TrimTitle(GetCellString(worksheet.Cell(row, header.ColTitle)));
            var groupText = header.ColGroup > 0 ? GetCellString(worksheet.Cell(row, header.ColGroup)) : string.Empty;
            var unitText = header.ColUnit > 0 ? GetCellString(worksheet.Cell(row, header.ColUnit)) : string.Empty;
            var efText = header.ColEf > 0 ? GetCellString(worksheet.Cell(row, header.ColEf)) : string.Empty;
            var scopeText = header.ColScope > 0 ? GetCellString(worksheet.Cell(row, header.ColScope)) : string.Empty;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(groupText) &&
                string.IsNullOrWhiteSpace(unitText) && string.IsNullOrWhiteSpace(efText)) continue;

            // [2026-09-19][Antigravity] วิเคราะห์ค่า Scope จากเซลล์ ("ขอบเขต 2", "2", "scope 2" ฯลฯ) หรือจากหัวคอลัมน์ หรือค่าตั้งต้น
            var requestedScope = ParseScope(scopeText) ?? header.HeaderScope ?? defaultScope;

            var item = new EmissionSourceImportItemDto
            {
                RowNumber = row,
                Title = title,
                GroupText = groupText,
                UnitText = unitText,
                ScopeText = scopeText,
                Scope = requestedScope ?? eTgoScopeNo.Scope1
            };

            // --- Title ---
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
                else if (existingDict.TryGetValue(title, out var existingSource))
                {
                    item.IsExistingMatched = true;
                    item.MatchedExistingSourceId = existingSource.Id;
                    item.IsExistingDefaultMaster = existingSource.SourceType == eSourceType.Default;
                    item.ExistingAction = eExistingSourceAction.Skip;
                }
            }

            // --- Group (จับคู่ด้วยชื่อกลุ่มหรือรหัสกลุ่ม ถ้าไฟล์ไม่ระบุใช้ค่าตั้งต้นที่ผู้ใช้เลือกในหน้าจอ) ---
            // [2026-09-19][Antigravity] ตรวจสอบกรณีผู้ใช้ระบุ Scope ("ขอบเขต 2", "2", "scope 2") ในช่อง Group
            var groupScope = ParseScope(groupText);
            if (!string.IsNullOrWhiteSpace(groupText) && groupScope.HasValue)
            {
                if (!requestedScope.HasValue)
                {
                    requestedScope = groupScope;
                    item.Scope = groupScope.Value;
                }
                groupText = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(groupText))
            {
                var fallbackGroup = defaultGroupId.HasValue ? groups.FirstOrDefault(g => g.Id == defaultGroupId.Value) : null;
                if (fallbackGroup != null)
                {
                    item.GroupId = fallbackGroup.Id;
                    item.MatchedGroupTitle = fallbackGroup.Title ?? string.Empty;
                    item.Scope = fallbackGroup.TgoScopeNo;
                    item.IsGroupFromDefault = true;
                }
                else if (requestedScope.HasValue)
                {
                    // หากระบุ Scope (เช่น Scope 2) และในระบบมีกลุ่มใน Scope นั้นกลุ่มเดียว ให้ auto-match ได้เลย
                    var scopeGroups = groups.Where(g => g.TgoScopeNo == requestedScope.Value).ToList();
                    if (scopeGroups.Count == 1)
                    {
                        var autoGroup = scopeGroups[0];
                        item.GroupId = autoGroup.Id;
                        item.MatchedGroupTitle = autoGroup.Title ?? string.Empty;
                        item.Scope = autoGroup.TgoScopeNo;
                        item.IsGroupFromDefault = true;
                    }
                    else
                    {
                        item.IsValid = false;
                        item.ValidationErrors.Add("Group is required.");
                    }
                }
                else
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add("Group is required.");
                }
            }
            else
            {
                var candidates = groups
                    .Where(g => string.Equals(g.Title, groupText, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrWhiteSpace(g.TitleTh) && string.Equals(g.TitleTh, groupText, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (candidates.Count == 0)
                {
                    candidates = groups
                        .Where(g => string.Equals(g.Code, groupText, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                // [2026-09-19][Antigravity] รองรับ alias กลุ่มยอดนิยม เช่น Template ใส่ "Purchased Electricity" แต่ DB คือ "Electricity, grid mix"
                if (candidates.Count == 0)
                {
                    if (groupText.Contains("electric", StringComparison.OrdinalIgnoreCase) ||
                        groupText.Contains("ไฟฟ้า", StringComparison.OrdinalIgnoreCase))
                    {
                        candidates = groups.Where(g => g.TgoScopeNo == eTgoScopeNo.Scope2).ToList();
                    }
                }

                // ชื่อกลุ่มซ้ำข้ามขอบเขตได้ ถ้าไฟล์ระบุคอลัมน์ Scope มาด้วยให้ใช้กรองก่อน
                if (candidates.Count > 1 && requestedScope.HasValue)
                {
                    candidates = candidates.Where(g => g.TgoScopeNo == requestedScope.Value).ToList();
                }

                if (candidates.Count == 0)
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add(requestedScope.HasValue
                        ? $"Emission group '{groupText}' was not found in {FormatScope(requestedScope.Value)}."
                        : $"Emission group '{groupText}' was not found in the library.");
                }
                else if (candidates.Count > 1)
                {
                    item.IsValid = false;
                    var scopeList = string.Join(", ", candidates.Select(g => FormatScope(g.TgoScopeNo)).Distinct());
                    item.ValidationErrors.Add($"Emission group '{groupText}' is ambiguous ({scopeList}). Add a 'Scope' column to specify which one.");
                }
                else
                {
                    var matchedGroup = candidates[0];
                    item.GroupId = matchedGroup.Id;
                    item.MatchedGroupTitle = matchedGroup.Title ?? string.Empty;
                    item.Scope = matchedGroup.TgoScopeNo;

                    if (requestedScope.HasValue && matchedGroup.TgoScopeNo != requestedScope.Value)
                    {
                        item.IsValid = false;
                        item.ValidationErrors.Add($"Emission group '{groupText}' belongs to {FormatScope(matchedGroup.TgoScopeNo)} but {FormatScope(requestedScope.Value)} was specified.");
                    }
                }
            }

            // --- Unit ---
            if (string.IsNullOrWhiteSpace(unitText))
            {
                var fallbackUnit = defaultUnitId.HasValue ? units.FirstOrDefault(u => u.Id == defaultUnitId.Value) : null;
                if (fallbackUnit != null)
                {
                    item.UnitId = fallbackUnit.Id;
                    item.MatchedUnitTitle = fallbackUnit.Title ?? string.Empty;
                    item.IsUnitFromDefault = true;
                }
                else
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add("Unit is required.");
                }
            }
            else
            {
                var matchedUnit = units.FirstOrDefault(u => string.Equals(u.Title, unitText, StringComparison.OrdinalIgnoreCase));
                if (matchedUnit == null)
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add($"Emission unit '{unitText}' was not found in the library.");
                }
                else
                {
                    item.UnitId = matchedUnit.Id;
                    item.MatchedUnitTitle = matchedUnit.Title ?? string.Empty;
                }
            }

            // --- Reference ---
            if (header.ColRef > 0)
            {
                item.Ref = GetCellString(worksheet.Cell(row, header.ColRef));
            }

            // --- Individual GHG Breakdowns ---
            item.CO2 = header.ColCO2 > 0 ? ParseGasCell(worksheet.Cell(row, header.ColCO2)) : 0m;
            item.FossilCH4 = header.ColFossilCH4 > 0 ? ParseGasCell(worksheet.Cell(row, header.ColFossilCH4)) : 0m;
            item.CH4 = header.ColCH4 > 0 ? ParseGasCell(worksheet.Cell(row, header.ColCH4)) : 0m;
            item.N2O = header.ColN2O > 0 ? ParseGasCell(worksheet.Cell(row, header.ColN2O)) : 0m;
            item.SF6 = header.ColSF6 > 0 ? ParseGasCell(worksheet.Cell(row, header.ColSF6)) : 0m;
            item.NF3 = header.ColNF3 > 0 ? ParseGasCell(worksheet.Cell(row, header.ColNF3)) : 0m;
            item.HFCs = header.ColHFCs > 0 ? ParseGasCell(worksheet.Cell(row, header.ColHFCs)) : 0m;
            item.PFCs = header.ColPFCs > 0 ? ParseGasCell(worksheet.Cell(row, header.ColPFCs)) : 0m;

            // Default AR5 GWP Values
            item.gwpCO2 = 1m;
            item.gwpFossilCH4 = 30m;
            item.gwpCH4 = 28m;
            item.gwpN2O = 265m;
            item.gwpSF6 = 23500m;
            item.gwpNF3 = 16100m;
            item.gwpHFCs = 1m;
            item.gwpPFCs = 1m;
            item.GwpVersion = eGwpVersion.AR5;

            // คำนวณ EF รวมจากแต่ละก๊าซ GHG ตามมาตรฐาน TGO / IPCC AR5
            var calculatedGhgEf = (item.CO2 * item.gwpCO2) +
                                  (item.FossilCH4 * item.gwpFossilCH4) +
                                  (item.CH4 * item.gwpCH4) +
                                  (item.N2O * item.gwpN2O) +
                                  (item.SF6 * item.gwpSF6) +
                                  (item.NF3 * item.gwpNF3) +
                                  (item.HFCs * item.gwpHFCs) +
                                  (item.PFCs * item.gwpPFCs);

            // --- EF / Total EF ---
            if (!string.IsNullOrWhiteSpace(efText))
            {
                if (!decimal.TryParse(efText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ef))
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add($"EF value '{efText}' is not a valid number.");
                }
                else if (ef < 0)
                {
                    item.IsValid = false;
                    item.ValidationErrors.Add("EF must not be negative.");
                }
                else
                {
                    item.EF = ef;
                    // หากไฟล์ระบุเฉพาะ EF แต่ไม่ได้ใส่แยกก๊าซมาเลย ให้กำหนดเข้า CO2 เพื่อรักษาความเข้ากันได้
                    if (calculatedGhgEf == 0m && ef > 0m)
                    {
                        item.CO2 = ef;
                    }
                }
            }
            else
            {
                // หากไม่ได้ใส่คอลัมน์ EF แต่มีก๊าซแยก ให้ใช้ผลรวมจากการคำนวณก๊าซ
                item.EF = calculatedGhgEf;
            }

            results.Add(item);
        }

        return results;
        #endregion
    }
    #endregion

    #region class methods - Private helpers
    private static IQueryable<TbEmissionGroup> BuildGroupQuery(ApplicationDbContext context, int? subscriptionId)
    {
        #region hide [2026-09-06]
        var query = context.EmissionGroups.Where(g => !g.IsDeleted && g.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var targetSubId = subscriptionId.Value;
            return query.Where(g => g.SourceType == eSourceType.Default || g.SubscriptionId == targetSubId);
        }

        return query.Where(g => g.SourceType == eSourceType.Default);
        #endregion
    }

    private static IQueryable<TbEmissionUnit> BuildUnitQuery(ApplicationDbContext context, int? subscriptionId)
    {
        #region hide [2026-09-06]
        var query = context.Units.Where(u => !u.IsDeleted && u.IsActive);

        if (subscriptionId.HasValue && subscriptionId.Value > 0)
        {
            var targetSubId = subscriptionId.Value;
            return query.Where(u => u.SourceType == eSourceType.Default || u.SubscriptionId == targetSubId);
        }

        return query.Where(u => u.SourceType == eSourceType.Default);
        #endregion
    }

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
    /// สแกนหาแถวหัวตาราง (5 แถวแรก) และตำแหน่งคอลัมน์ Title / Group / Unit / EF / GHG Breakdowns / Ref รองรับไทย-อังกฤษ และสลับตำแหน่งได้อิสระ
    /// </summary>
    private static (int HeaderRow, int ColTitle, int ColGroup, int ColUnit, int ColEf, int ColScope, eTgoScopeNo? HeaderScope,
        int ColCO2, int ColFossilCH4, int ColCH4, int ColN2O, int ColSF6, int ColNF3, int ColHFCs, int ColPFCs, int ColRef)
        DetectHeader(IXLWorksheet worksheet, int lastRow, int lastCol)
    {
        #region hide [2026-09-06]
        int scanLimit = Math.Min(lastRow, 5);

        for (int row = 1; row <= scanLimit; row++)
        {
            int colTitle = -1, colGroup = -1, colUnit = -1, colEf = -1, colScope = -1;
            eTgoScopeNo? headerScope = null;
            int colCO2 = -1, colFossilCH4 = -1, colCH4 = -1, colN2O = -1;
            int colSF6 = -1, colNF3 = -1, colHFCs = -1, colPFCs = -1, colRef = -1;

            for (int col = 1; col <= lastCol; col++)
            {
                var hText = GetCellString(worksheet.Cell(row, col)).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(hText)) continue;

                // ตรวจจับ GHG Breakdowns ก่อน (ไม่รวมคอลัมน์ GWP)
                if (!hText.Contains("gwp"))
                {
                    if (colFossilCH4 == -1 && (hText.Contains("fossil ch4") || hText.Contains("fossil_ch4") || hText.Contains("fossilch4") || hText.Contains("ฟอสซิลมีเทน")))
                    {
                        colFossilCH4 = col;
                        continue;
                    }
                    if (colCH4 == -1 && (hText == "ch4" || hText.StartsWith("ch4 ") || hText.StartsWith("ch4(") || hText.Contains("มีเทน") || hText.Contains("methane")))
                    {
                        colCH4 = col;
                        continue;
                    }
                    if (colCO2 == -1 && (hText == "co2" || hText.StartsWith("co2 ") || hText.StartsWith("co2(") || hText.Contains("คาร์บอนไดออกไซด์")))
                    {
                        colCO2 = col;
                        continue;
                    }
                    if (colN2O == -1 && (hText == "n2o" || hText.StartsWith("n2o ") || hText.StartsWith("n2o(") || hText.Contains("ไนตรัสออกไซด์")))
                    {
                        colN2O = col;
                        continue;
                    }
                    if (colSF6 == -1 && (hText == "sf6" || hText.StartsWith("sf6 ") || hText.StartsWith("sf6(")))
                    {
                        colSF6 = col;
                        continue;
                    }
                    if (colNF3 == -1 && (hText == "nf3" || hText.StartsWith("nf3 ") || hText.StartsWith("nf3(")))
                    {
                        colNF3 = col;
                        continue;
                    }
                    if (colHFCs == -1 && (hText == "hfcs" || hText.StartsWith("hfc") || hText.Contains("hfcs")))
                    {
                        colHFCs = col;
                        continue;
                    }
                    if (colPFCs == -1 && (hText == "pfcs" || hText.StartsWith("pfc") || hText.Contains("pfcs")))
                    {
                        colPFCs = col;
                        continue;
                    }
                }

                if (colGroup == -1 && (hText.Contains("group") || hText.Contains("กลุ่ม") || hText == "emission" || hText.Contains("emission group") || hText.Contains("การปล่อย")))
                {
                    colGroup = col;
                    continue;
                }

                // คอลัมน์ทางเลือก ใช้แยกกรณีชื่อกลุ่มซ้ำกันข้ามขอบเขต
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
                    headerScope = ParseScope(hText);
                    continue;
                }

                if (colUnit == -1 && (hText.Contains("unit") || hText.Contains("หน่วย")))
                {
                    colUnit = col;
                    continue;
                }

                if (colRef == -1 && (hText == "ref" || hText.StartsWith("ref") || hText.Contains("reference") || hText.Contains("อ้างอิง") || hText.Contains("แหล่งข้อมูล")))
                {
                    colRef = col;
                    continue;
                }

                // ตัดคอลัมน์ค่า GWP ออกก่อน แล้วจึงจับ EF / ค่าการปล่อย / Total EF
                if (colEf == -1 && !hText.Contains("gwp") &&
                    (hText == "ef" || hText.StartsWith("ef") || hText.Contains("total ef") || hText.Contains("total (") ||
                     hText.Contains("total ef (") || hText == "total" || hText.Contains("emission factor") ||
                     hText.Contains("factor") || hText.Contains("ค่าการปล่อย") || hText.Contains("แฟกเตอร์")))
                {
                    colEf = col;
                    continue;
                }

                if (colTitle == -1 && (hText.Contains("title") || hText.Contains("source") || hText.Contains("name") ||
                                       hText.Contains("ชื่อ") || hText.Contains("แหล่ง")))
                {
                    colTitle = col;
                }
            }

            if (colTitle != -1)
            {
                return (row, colTitle, colGroup, colUnit, colEf, colScope, headerScope,
                    colCO2, colFossilCH4, colCH4, colN2O, colSF6, colNF3, colHFCs, colPFCs, colRef);
            }
        }

        return (0, -1, -1, -1, -1, -1, null, -1, -1, -1, -1, -1, -1, -1, -1, -1);
        #endregion
    }

    /// <summary>
    /// แปลงข้อความขอบเขตการรายงานเป็น enum รองรับทั้งตัวเลข ภาษาไทย และภาษาอังกฤษ ครบทุกขอบเขต (Scope 1, 2, 3, 4)
    /// </summary>
    public static eTgoScopeNo? ParseScope(string? scopeText)
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

    private static string FormatDataSources(TbEmissionSource src)
    {
        #region hide [2026-09-06]
        var flags = new List<string>();
        if (src.SelfCollect) flags.Add("Self Collect");
        if (src.Supplier) flags.Add("Supplier");
        if (src.THLCIDB) flags.Add("TH LCI DB");
        if (src.TGOEF) flags.Add("TGO EF");
        if (src.ThaiRes) flags.Add("Thai Research");
        if (src.IntDB) flags.Add("International DB");
        if (src.Other) flags.Add("Other");
        if (src.Substitute) flags.Add("Substitute");
        return string.Join(", ", flags);
        #endregion
    }

    private static decimal ParseGasCell(IXLCell cell)
    {
        if (cell == null || cell.IsEmpty()) return 0m;
        var text = GetCellString(cell);
        if (string.IsNullOrWhiteSpace(text)) return 0m;
        if (decimal.TryParse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            return val >= 0 ? val : 0m;
        }
        return 0m;
    }
    #endregion
}
