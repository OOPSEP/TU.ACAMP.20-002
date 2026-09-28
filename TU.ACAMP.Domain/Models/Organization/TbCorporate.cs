using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_corporate", Schema = "organization")]
public class TbCorporate : AuditableEntity<int>
{
    #region class field  
    private readonly List<TbCompany> _companies = new();
    private readonly List<TbCorporateUser> _users = new();
    private readonly List<TbActivityTopic> _topics = new();
    #endregion
    #region class properties 
    [Required]
    public int SubscriptionId { get; set; }  
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;
    [Required]
    public string Title {  get; set; }= string.Empty;
    [StringLength(250)]
    public string Remark { get; set; } = string.Empty;
    [DisplayName("Allow Share Data")]
    public bool AllowShare { get; set; } = false;
    [StringLength(100)]
    [DisplayName("Sharing Key ID")]
    public string KeyId { get; set; } = string.Empty;
    public virtual IReadOnlyCollection<TbCompany> Companies => _companies.AsReadOnly();
    public virtual IReadOnlyCollection<TbCorporateUser> Users => _users.AsReadOnly();
    public virtual IReadOnlyCollection<TbActivityTopic> Topics => _topics.AsReadOnly();
    public decimal MaterialityThreshold { get; set; } = 5;
    // [2026-09-15][Claude] Property สำหรับแสดงผลและจัดเรียง (Sort) ปริมาณคาร์บอนฟุตพริ้นท์ tCO2e บน DataGrid
    [NotMapped]
    public decimal TotalGhg { get; set; } = 0m;
    #endregion
    #region class method 
    public string EnsureKeyIdGenerated()
    {
        if (string.IsNullOrWhiteSpace(KeyId))
        {
            KeyId = $"CORP-{Guid.NewGuid():N}";
        }
        return KeyId;
    }

    public static TbCorporate Duplicate(TbCorporate source, int targetSubscriptionId)
    {
        #region hide [2026-08-17]
        var copy = new TbCorporate
        {
            SubscriptionId = targetSubscriptionId,
            Code = source.Code,
            Title = $"{source.Title} (Copy)",
            Remark = source.Remark,
            AllowShare = source.AllowShare,
            KeyId = $"CORP-{Guid.NewGuid():N}"
        };

        if (source.Users != null)
        {
            foreach (var u in source.Users.Where(u => !u.IsDeleted))
            {
                copy.Add(new TbCorporateUser
                {
                    Email = u.Email.Trim().ToLowerInvariant(),
                    Role = u.Role,
                    Remarks = u.Remarks,
                    IsActive = u.IsActive
                });
            }
        }

        return copy;
        #endregion
    }

    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Corporate : {Title} ({Code})";
        #endregion
    }
    public void Add(TbCorporateUser user)
    {
        #region hide [2026-08-15]
        if (user == null) return;
        user.CorporateId = this.Id;
        user.Corporate = this;
        if (!_users.Contains(user)) _users.Add(user);
        #endregion
    }
    public void Remove(TbCorporateUser user)
    {
        #region hide [2026-08-15]
        if (user == null) return;
        if (_users.Contains(user))
        {
            _users.Remove(user);
            user.CorporateId = 0;
            user.Corporate = null!;
        }
        #endregion
    }
    public void Add(TbCompany company)
    {
        #region hide [2026-08-15]
        if (company == null) return;
        company.CorporateId = this.Id;
        company.Corporate = this;
        if (!_companies.Contains(company)) _companies.Add(company);
        #endregion
    }

    public void Remove(TbCompany company)
    {
        #region hide [2026-08-15]
        if (company == null) return;
        if (_companies.Contains(company))
        {
            _companies.Remove(company);
            company.CorporateId = 0;
            company.Corporate = null!;
        }
        #endregion
    }
    public void Add(TbActivityTopic topic)
    {
        #region hide [2026-08-15]
        if (topic == null) return;
        topic.Level = eTopicLevel.Corporate;
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
