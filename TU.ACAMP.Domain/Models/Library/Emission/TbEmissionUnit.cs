using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_emission_unit")]
public class TbEmissionUnit : AuditableEntity<int>
{
    #region class field  
    private static ReadOnlyCollection<TbEmissionUnit>? _masterListCache;
    #endregion

    #region class properties 
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(500)]
    public string Remark { get; set; } = string.Empty;
    public eSourceType SourceType { get; set; } = eSourceType.Default;
    public int? SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }
    #endregion

    #region class method 
    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Unit: {Title}";
        #endregion
    }

    public static TbEmissionUnit Duplicate(TbEmissionUnit source, int targetSubscriptionId)
    {
        #region hide [2026-08-17]
        if (source is null) throw new ArgumentNullException(nameof(source));
        return new TbEmissionUnit
        {
            Title = source.Title,
            Remark = source.Remark,
            SubscriptionId = targetSubscriptionId,
            SourceType = eSourceType.Custom
        };
        #endregion
    }

    public static IReadOnlyCollection<TbEmissionUnit> GetMasterList()
    {
        #region hide [2026-08-15]
        if (_masterListCache == null)
        {
            var list = new List<TbEmissionUnit>
            {
                new() { Id = 1, Title = "kg", Remark = "Kilogram (กิโลกรัม)", SourceType = eSourceType.Default },
                new() { Id = 2, Title = "ton", Remark = "Metric Ton (ตัน)", SourceType = eSourceType.Default },
                new() { Id = 3, Title = "g", Remark = "Gram (กรัม)", SourceType = eSourceType.Default },
                new() { Id = 4, Title = "L", Remark = "Liter (ลิตร)", SourceType = eSourceType.Default },
                new() { Id = 5, Title = "m3", Remark = "Cubic Meter (ลูกบาศก์เมตร)", SourceType = eSourceType.Default },
                new() { Id = 6, Title = "kWh", Remark = "Kilowatt-hour (กิโลวัตต์-ชั่วโมง)", SourceType = eSourceType.Default },
                new() { Id = 7, Title = "MWh", Remark = "Megawatt-hour (เมกะวัตต์-ชั่วโมง)", SourceType = eSourceType.Default },
                new() { Id = 8, Title = "MJ", Remark = "Megajoule (เมกะจูล)", SourceType = eSourceType.Default },
                new() { Id = 9, Title = "GJ", Remark = "Gigajoule (จิกะจูล)", SourceType = eSourceType.Default },
                new() { Id = 10, Title = "km", Remark = "Kilometer (กิโลเมตร)", SourceType = eSourceType.Default },
                new() { Id = 11, Title = "passenger-km", Remark = "Passenger-Kilometer (คน-กิโลเมตร)", SourceType = eSourceType.Default },
                new() { Id = 12, Title = "ton-km", Remark = "Ton-Kilometer (ตัน-กิโลเมตร)", SourceType = eSourceType.Default },
                new() { Id = 13, Title = "kgCO2e", Remark = "Kilogram CO2 Equivalent", SourceType = eSourceType.Default },
                new() { Id = 14, Title = "tCO2e", Remark = "Metric Ton CO2 Equivalent", SourceType = eSourceType.Default }
            };
            _masterListCache = list.AsReadOnly();
        }
        return _masterListCache;
        #endregion
    }
    #endregion
}
