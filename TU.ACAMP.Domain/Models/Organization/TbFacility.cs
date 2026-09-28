using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_facility", Schema = "organization")]
public class TbFacility : AuditableEntity<int>
{
    #region class field  
    private readonly List<TbActivityTopic> _topics = new();
    #endregion
    #region class properties 
    [Required]
    public int CompanyId { get; set; }
    [ForeignKey(nameof(CompanyId))]
    public virtual TbCompany Company { get; set; } = null!;
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
    [Required]
    public string Title { get; set; } = string.Empty;
    [StringLength(250)]
    public string Remark { get; set; } = string.Empty;
    [DisplayName("Allow Share Data")]
    public bool AllowShare { get; set; } = false;
    [StringLength(100)]
    [DisplayName("Sharing Key ID")]
    public string KeyId { get; set; } = string.Empty;
    public virtual IReadOnlyCollection<TbActivityTopic> Topics => _topics.AsReadOnly();
    // [2026-09-15][Claude] Property สำหรับแสดงผลและจัดเรียง (Sort) ปริมาณคาร์บอนฟุตพริ้นท์ tCO2e บน DataGrid
    [NotMapped]
    public decimal TotalGhg { get; set; } = 0m;
    #endregion
    #region class method 
    public string EnsureKeyIdGenerated()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
        {
            KeyId = $"FAC-{Guid.NewGuid():N}";
        }
        return KeyId;
    }

    public static TbFacility Duplicate(TbFacility source, int targetCompanyId)
    {
        #region hide [2026-08-18]
        var copy = new TbFacility
        {
            CompanyId = targetCompanyId,
            Title = $"{source.Title} (Copy)",
            Remark = source.Remark,
            AllowShare = source.AllowShare,
            KeyId = $"FAC-{Guid.NewGuid():N}"
        };
        return copy;
        #endregion
    }

    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Facility: {Title}";
        #endregion
    }
    public void Add(TbActivityTopic topic)
    {
        #region hide [2026-08-15]
        if (topic == null) return;
        topic.Level = eTopicLevel.Facility;
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
