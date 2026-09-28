using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Helper;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_company", Schema = "organization")]
public class TbCompany : AuditableEntity<int>
{
    #region class field  
    private readonly List<TbFacility> _facilities = new();
    private readonly List<TbActivityTopic> _topics = new();
    #endregion

    #region class properties 
    [Required]
    public int CorporateId { get; set; }
    [ForeignKey(nameof(CorporateId))]
    public virtual TbCorporate Corporate { get; set; } = null!;
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
    [Required]
    public string Title { get; set; } = string.Empty;
    [StringLength(250)]
    public string Remark { get; set; } = string.Empty;
    public virtual IReadOnlyCollection<TbFacility> Facilities => _facilities.AsReadOnly();
    public virtual IReadOnlyCollection<TbActivityTopic> Topics => _topics.AsReadOnly();
    [Percent0To100]
    public float EquityShare { get; set; } = 100;
    [DisplayName("Allow Share Data")]
    public bool AllowShare { get; set; } = false;
    [StringLength(100)]
    [DisplayName("Sharing Key ID")]
    public string KeyId { get; set; } = string.Empty;
    // [2026-09-14][Gemini] Property สำหรับแสดงผลและจัดเรียง (Sort) ปริมาณคาร์บอนฟุตพริ้นท์ tCO2e บน DataGrid
    [NotMapped]
    public decimal TotalGhg { get; set; } = 0m;
    #endregion

    #region class method 
    public string EnsureKeyIdGenerated()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
        {
            KeyId = $"COMP-{Guid.NewGuid():N}";
        }
        return KeyId;
    }

    public static TbCompany Duplicate(TbCompany source, int targetCorporateId)
    {
        #region hide [2026-08-17]
        var copy = new TbCompany
        {
            CorporateId = targetCorporateId,
            Code = source.Code,
            Title = $"{source.Title} (Copy)",
            EquityShare = source.EquityShare,
            Remark = source.Remark,
            AllowShare = source.AllowShare,
            KeyId = $"COMP-{Guid.NewGuid():N}"
        };
        return copy;
        #endregion
    }

    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Company: {Title} ({Code})";
        #endregion
    }
    public void Add(TbFacility item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        item.CompanyId = this.Id;
        item.Company = this;
        if (!_facilities.Contains(item)) _facilities.Add(item);
        #endregion
    }
    public void Remove(TbFacility item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        if (_facilities.Contains(item))
        {
            _facilities.Remove(item);
            item.CompanyId = 0;
            item.Company = null!;
        }
        #endregion
    }
    public void Add(TbActivityTopic topic)
    {
        #region hide [2026-08-15]
        if (topic == null) return;
        topic.Level = eTopicLevel.Company;
        topic.TargetId = this.Id;
        if (!_topics.Contains(topic)) _topics.Add(topic);
        #endregion
    }

    public void Remove(TbActivityTopic topic)
    {
        #region hide [2026-08-15]
        if (topic == null) return;
        if (_topics.Contains(topic))
        {
            _topics.Remove(topic);
            topic.TargetId = 0;
        }
        #endregion
    }
    #endregion
}
