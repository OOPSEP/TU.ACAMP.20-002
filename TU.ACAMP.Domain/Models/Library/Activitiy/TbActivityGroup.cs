using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using TU.ACAMP.Domain.Enums;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_activity_group")]
public class TbActivityGroup : AuditableEntity<int>
{
    #region class field  
    private static IReadOnlyCollection<TbActivityGroup>? _masterListCache;
    #endregion

    #region class properties 
    public eTgoScopeNo TgoScopeNo { get; set; }
    public eIsoCategoryNo IsoCategoryNo { get; set; }
    [Required]
    public string Title { get; set; } = string.Empty;
    [StringLength(250)]
    public string TitleTh { get; set; } = string.Empty;
    [StringLength(250)]
    public string Remark { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    #endregion

    #region class method 
    public override string ToString()
    {
        return $"[{Code}] {Title}";
    }

    public static IReadOnlyCollection<TbActivityGroup> GetMasterList()
    {
        #region hide [2026-07-04]
        if (_masterListCache == null)
        {
            var list = new List<TbActivityGroup>
            {
                #region ==================== SCOPE 1 ====================
                new()
                {
                    Id = 1,
                    Code = "0101",
                    TgoScopeNo = eTgoScopeNo.Scope1,
                    IsoCategoryNo = eIsoCategoryNo.Category1,
                    Title = "Stationary Combustion",
                    TitleTh = "การเผาไหม้เชื้อเพลิงที่อยู่กับที่",
                    Remark = "การเผาไหม้เชื้อเพลิงในเครื่องจักร อุปกรณ์ หรือหม้อต้มไอน้ำ (Boiler) ที่ติดตั้งอยู่กับที่ภายในองค์กร"
                },
                new()
                {
                    Id = 2,
                    Code = "0102",
                    TgoScopeNo = eTgoScopeNo.Scope1,
                    IsoCategoryNo = eIsoCategoryNo.Category1,
                    Title = "Mobile Combustion",
                    TitleTh = "การเผาไหม้เชื้อเพลิงที่เคลื่อนที่",
                    Remark = "การเผาไหม้เชื้อเพลิงของยานพาหนะที่องค์กรเป็นเจ้าของหรือควบคุมการดำเนินงาน"
                },
                new()
                {
                    Id = 3,
                    Code = "0103",
                    TgoScopeNo = eTgoScopeNo.Scope1,
                    IsoCategoryNo = eIsoCategoryNo.Category1,
                    Title = "Process Emissions",
                    TitleTh = "การปล่อยก๊าซเรือนกระจกจากกระบวนการผลิต",
                    Remark = "การปล่อยก๊าซเรือนกระจกจากปฏิกิริยาเคมีทางกายภาพในกระบวนการผลิต (ไม่รวมการเผาไหม้เชื้อเพลิง)"
                },
                new()
                {
                    Id = 4,
                    Code = "0104",
                    TgoScopeNo = eTgoScopeNo.Scope1,
                    IsoCategoryNo = eIsoCategoryNo.Category1,
                    Title = "Fugitive Emissions",
                    TitleTh = "การปล่อยก๊าซเรือนกระจกแบบรั่วไหล",
                    Remark = "การรั่วไหลของสารทำความเย็นจากระบบปรับอากาศ เครื่องทำความเย็น หรือการระเหยจากระบบบำบัดน้ำเสีย"
                },
                #endregion
            };
            _masterListCache = new ReadOnlyCollection<TbActivityGroup>(list);
        }
        return _masterListCache;
        #endregion
    }
    #endregion
}
