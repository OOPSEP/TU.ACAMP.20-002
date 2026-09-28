using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text.Json;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_emission_mapping", Schema = "library")]
public class TbEmissionMapping : AuditableEntity<int>
{
    #region class field  
    private static ReadOnlyCollection<TbEmissionMapping>? _masterListCache;
    #endregion

    #region class properties 
    /// <summary>
    /// ชื่อของ master source (Scope1ChildId หรือ Scope2ChildId)
    /// </summary>
    [Required]
    [StringLength(250)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// ตัวคูณแปลงหน่วยจาก master ไปยังลูกที่ใช้หน่วยต่างกัน (โดยเฉพาะคู่ WTT ที่ Scope3ChildId เป็นต่อ kg)
    /// </summary>
    public decimal? UnitConversionRatio { get; set; }

    /// <summary>
    /// ที่มาและคำอธิบายเพิ่มเติม
    /// </summary>
    [StringLength(500)]
    public string Remark { get; set; } = string.Empty;

    public int Scope1ChildId { get; set; }
    [StringLength(250)]
    public string Scope1ChildTitle { get; set; } = string.Empty;
    [StringLength(50)]
    public string Scope1ChildUnit { get; set; } = string.Empty;

    public int Scope2ChildId { get; set; }
    [StringLength(250)]
    public string Scope2ChildTitle { get; set; } = string.Empty;
    [StringLength(50)]
    public string Scope2ChildUnit { get; set; } = string.Empty;

    public int Scope3ChildId { get; set; }
    [StringLength(250)]
    public string Scope3ChildTitle { get; set; } = string.Empty;
    [StringLength(50)]
    public string Scope3ChildUnit { get; set; } = string.Empty;

    public int BiogenicChildId { get; set; }
    [StringLength(250)]
    public string BiogenicChildTitle { get; set; } = string.Empty;
    [StringLength(50)]
    public string BiogenicChildUnit { get; set; } = string.Empty;

    public eSourceType SourceType { get; set; } = eSourceType.Default;

    public int? SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    /// <summary>
    /// สัดส่วนเชื้อเพลิงชีวภาพในน้ำมันผสม (เช่น B7 = 7, E20 = 20) — ใช้เพื่อแสดงผล/อ้างอิงเท่านั้น
    /// ห้ามนำไปใส่เป็น EmissionSharePercent ของกิ่ง Biogenic เพราะ EF ของ "... Report in Biogenic CO2 emissions"
    /// คิดต่อลิตรของน้ำมันผสมและรวมสัดส่วนนี้ไว้แล้ว (เทมเพลต อบก. EF TGO AR5 V2: Diesel B7 Biogenic = EF Biodiesel x 7%)
    /// ให้ใช้ <see cref="BiogenicChildEmissionSharePercent"/> แทน
    /// </summary>
    public decimal BiogenicSharePercent { get; set; }

    /// <summary>
    /// [2026-09-24][Claude] EmissionSharePercent ของกิ่ง Biogenic ที่แตกอัตโนมัติจาก mapping = 100% เสมอ
    /// (EF ของกิ่ง Biogenic รวมสัดส่วนเชื้อเพลิงชีวภาพไว้แล้ว ถ้าคูณ BiogenicSharePercent ซ้ำจะได้ค่าต่ำเกินจริง)
    /// ตรงกับที่ DatabaseSeeder2 / DatabaseSeeder4 ใช้มาตั้งแต่ต้น
    /// </summary>
    public const float BiogenicChildEmissionSharePercent = 100f;
    #endregion

    #region class methods 
    public IEnumerable<int> GetActiveChildIds()
    {
        if (Scope1ChildId != 0) yield return Scope1ChildId;
        if (Scope2ChildId != 0) yield return Scope2ChildId;
        if (Scope3ChildId != 0) yield return Scope3ChildId;
        if (BiogenicChildId != 0) yield return BiogenicChildId;
    }

    public bool Contains(int childSourceId)
    {
        if (childSourceId == 0) return false;

        return Scope1ChildId == childSourceId
            || Scope2ChildId == childSourceId
            || Scope3ChildId == childSourceId
            || BiogenicChildId == childSourceId;
    }

    public static void ClearCache()
    {
        _masterListCache = null;
    }

    public static IReadOnlyCollection<TbEmissionMapping> GetMasterList()
    {
        if (_masterListCache == null)
        {
            try
            {
                var jsonPath = Path.Combine(AppContext.BaseDirectory, "Files", "JSON", "tb_emission_mapping.json");
                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "TU.ACAMP.WebApp", "Files", "JSON", "tb_emission_mapping.json");
                }
                if (!File.Exists(jsonPath))
                {
                    jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Files", "JSON", "tb_emission_mapping.json");
                }

                if (File.Exists(jsonPath))
                {
                    var jsonString = File.ReadAllText(jsonPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var list = JsonSerializer.Deserialize<List<TbEmissionMapping>>(jsonString, options);
                    if (list != null)
                    {
                        _masterListCache = list.AsReadOnly();
                        return _masterListCache;
                    }
                }
            }
            catch
            {
                // Fallback to empty list
            }

            _masterListCache = new List<TbEmissionMapping>().AsReadOnly();
        }
        return _masterListCache;
    }

    public static int GetActivityGroupIdForSource(TbEmissionSource source, IEnumerable<TbActivityGroup> allGroups)
    {
        if (source == null) return 0;

        var scope = source.Group?.TgoScopeNo;
        var groupCode = source.Group?.Code ?? string.Empty;

        if (scope == eTgoScopeNo.Scope1)
        {
            if (groupCode == "0101") return 1; // Stationary Combustion
            if (groupCode == "0102" || groupCode == "0103") return 2; // Mobile Combustion
            if (groupCode == "0105") return 3; // Process
            if (groupCode == "0104") return 4; // Fugitive
        }
        else if (scope == eTgoScopeNo.Scope2)
        {
            if (groupCode == "0201" || groupCode == "0202") return 5; // Purchased Electricity
            if (groupCode == "0203") return 6; // Purchased Heat, Steam
        }
        else if (scope == eTgoScopeNo.Scope3)
        {
            if (groupCode == "0303" || groupCode == "0304" || groupCode == "0328") return 9; // S3-C03 (Fuel and Energy)
            if (groupCode == "0306") return 10; // S3-C04 (Upstream Transport)
            // [GHG-11] 0316 (Municipal Waste & Wastewater), 0324 (Waste Landfills), 0329 (Waste Management) -> S3-C05 Waste.
            // 0322 (Construction Materials) is purchased goods/services, not waste, and falls through to group 7 (S3-C01 / Cat 4).
            if (groupCode == "0316" || groupCode == "0324" || groupCode == "0329") return 11; // S3-C05 (Waste operations)
            if (groupCode == "0315") return 8; // S3-C02 (Capital Goods - เครื่องจักรกลทางการเกษตร)
            return 7;
        }
        else if (scope == eTgoScopeNo.SeparateReport)
        {
            if (groupCode.StartsWith("0402")) return 22; // Biogenic CO2 - Stationary
            if (groupCode.StartsWith("0404") || groupCode.StartsWith("0405")) return 23; // Biogenic CO2 - Mobile
            if (groupCode.StartsWith("0401") || groupCode.StartsWith("0403")) return 24; // Other Non-Kyoto
        }

        var matchedGroup = allGroups.FirstOrDefault(g => g.Code == groupCode);
        if (matchedGroup != null) return matchedGroup.Id;

        return allGroups.FirstOrDefault(g => g.TgoScopeNo == scope)?.Id ?? 0;
    }
    #endregion
}
