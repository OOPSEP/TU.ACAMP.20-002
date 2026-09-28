using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models;
using TU.ACAMP.Domain.Models.Organization;

namespace TU.ACAMP.Domain.Identity;


[Table("tb_subscription")]
public class TbSubscription : AuditableEntity<int>
{
    #region class field  
    private readonly List<TbSubscriptionUser> _users = new();
    private readonly List<TbCorporate> _corporates = new();
    #endregion
    #region class properties 
    [Required(ErrorMessage = "Owner Type is required")]
    public eSubscriptionPlan SubscriptionPlan { get; set; } = eSubscriptionPlan.Pro;
    [DisplayName("Allow Corporate Count"), JsonPropertyName("allow_corporate_count")]
    public int AllowCorporateCount { get; set; } = 6;
    [DisplayName("Allow Company Count"), JsonPropertyName("allow_company_count")]
    public int AllowCompanyCount { get; set; } = 24;
    [DisplayName("Allow Facility Count"), JsonPropertyName("allow_facility_count")]
    public int AllowFacilityCount { get; set; } = 144;
    [DisplayName("Allow Corporate User Count"), JsonPropertyName("allow_corporate_user_count")]
    public int AllowCorporateUserCount { get; set; } = 9;
    [NotMapped, JsonIgnore]
    public int AllowUserCount { get => AllowCorporateUserCount; set => AllowCorporateUserCount = value; }
    [DisplayName("Allow Data Upload Count"), JsonPropertyName("allow_data_upload_count")]
    public int AllowDataUploadCount { get; set; } = 8; // Mb maximum size of data upload(files) in a single request
    [DisplayName("Allow Data Download Count"), JsonPropertyName("allow_data_download_count")]
    public int AllowDataDownloadCount { get; set; } = 16; // Mb maximum size of data download(files) in a single request
    [DisplayName("Allow Data Store Count"), JsonPropertyName("allow_data_store_count")]
    public int AllowDataStoreCount { get; set; } = 128; // Mb maximum size sum of data store(files) in server
    [DisplayName("Allow Emission Count"), JsonPropertyName("allow_emission_count")]
    public int AllowEmissionCount { get; set; } = 128;
    [DisplayName("Allow Topic Count"), JsonPropertyName("allow_topic_count")]
    public int AllowTopicCount { get; set; } = 128;
    [DisplayName("Allow Event Count"), JsonPropertyName("allow_event_count")]
    public int AllowEventCount { get; set; } = 2048;
    [Required(ErrorMessage = "Subscription Title is required")]
    [DisplayName("Subscription Title"), JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddMonths(3);
    public string Remarks { get; set; } = string.Empty;
    [NotMapped]
    public bool IsExpired => DateTime.UtcNow > ExpiryDate;
    //advanced properties
    public eSubscriptionOwnerType OwnerType { get; set; } = eSubscriptionOwnerType.Juristic;
    [StringLength(200)]
    public string OwnerName { get; set; } = string.Empty;
    [Required(ErrorMessage = "Tax ID or Identification Number is required.")]
    [StringLength(13, MinimumLength = 13, ErrorMessage = "Tax ID must be exactly 13 digits.")]
    [RegularExpression(@"^\d+$", ErrorMessage = "Tax ID must contain only numbers.")]
    public string TaxId { get; set; } = string.Empty;
    [Required(ErrorMessage = "Owner Email is required")]
    [EmailAddress]
    [StringLength(150)]
    public string OwnerEmail { get; set; } = string.Empty;
    [StringLength(50)]
    public string OwnerPhone { get; set; } = string.Empty;
    [Required(ErrorMessage = "Billing Address is required")]
    [StringLength(500)]
    public string BillingAddress { get; set; } = string.Empty;
    [StringLength(200)]
    public string ContactPerson { get; set; } = string.Empty;
    public virtual IReadOnlyCollection<TbSubscriptionUser> Users => _users.AsReadOnly();
    public virtual IReadOnlyCollection<TbCorporate> Corporates => _corporates.AsReadOnly();
    #endregion
    #region class method 
    public double GetRemainingDays()
    {
        #region hide [2026-08-15]
        if (IsExpired) return 0;
        return (ExpiryDate - DateTime.UtcNow).TotalDays;
        #endregion
    }
    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Subscription: {Title} ({SubscriptionPlan})";
        #endregion
    }
    public static TbSubscription CreateBasicSubscription(string title = "Basic Subscription")
    {
        #region hide [2026-08-15]
        var subscription = new TbSubscription
        {
            Title = title,
            SubscriptionPlan = eSubscriptionPlan.Basic
        };
        subscription.ApplyPlanQuotas(eSubscriptionPlan.Basic);
        return subscription;
        #endregion
    }

    public static TbSubscription CreateProSubscription(string title = "Pro Subscription")
    {
        #region hide [2026-08-15]
        var subscription = new TbSubscription
        {
            Title = title,
            SubscriptionPlan = eSubscriptionPlan.Pro
        };
        subscription.ApplyPlanQuotas(eSubscriptionPlan.Pro);
        return subscription;
        #endregion
    }

    public static TbSubscription CreatePremiumSubscription(string title = "Premium Subscription")
    {
        #region hide [2026-08-15]
        var subscription = new TbSubscription
        {
            Title = title,
            SubscriptionPlan = eSubscriptionPlan.Premium
        };
        subscription.ApplyPlanQuotas(eSubscriptionPlan.Premium);
        return subscription;
        #endregion
    }

    public static TbSubscription CreateEnterpriseSubscription(string title = "Enterprise Subscription")
    {
        #region hide [2026-08-15]
        var subscription = new TbSubscription
        {
            Title = title,
            SubscriptionPlan = eSubscriptionPlan.Enterprise
        };
        subscription.ApplyPlanQuotas(eSubscriptionPlan.Enterprise);
        return subscription;
        #endregion
    }

    public static TbSubscription CreateDefaultSubscription(eSubscriptionPlan plan = eSubscriptionPlan.Basic)
    {
        #region hide [2026-08-15]
        return plan switch
        {
            eSubscriptionPlan.Pro => CreateProSubscription(),
            eSubscriptionPlan.Premium => CreatePremiumSubscription(),
            eSubscriptionPlan.Enterprise => CreateEnterpriseSubscription(),
            _ => CreateBasicSubscription()
        };
        #endregion
    }

    public void ApplyPlanQuotas(eSubscriptionPlan plan)
    {
        #region hide [2026-08-15]
        SubscriptionPlan = plan;
        switch (plan)
        {
            case eSubscriptionPlan.Basic:
                AllowCorporateCount = 1;
                AllowCompanyCount = 2;
                AllowFacilityCount = 6;
                AllowUserCount = 2;
                AllowDataUploadCount = 2;
                AllowDataDownloadCount = 4;
                AllowDataStoreCount = 32;
                AllowEmissionCount = 32;
                AllowTopicCount = 32;
                AllowEventCount = 256;
                break;
            case eSubscriptionPlan.Pro:
                AllowCorporateCount = 6;
                AllowCompanyCount = 24;
                AllowFacilityCount = 144;
                AllowUserCount = 9;
                AllowDataUploadCount = 8;
                AllowDataDownloadCount = 16;
                AllowDataStoreCount = 128;
                AllowEmissionCount = 128;
                AllowTopicCount = 128;
                AllowEventCount = 2048;
                break;
            case eSubscriptionPlan.Premium:
                AllowCorporateCount = 10;
                AllowCompanyCount = 40;
                AllowFacilityCount = 240;
                AllowUserCount = 12;
                AllowDataUploadCount = 12;
                AllowDataDownloadCount = 24;
                AllowDataStoreCount = 256;
                AllowEmissionCount = 160;
                AllowTopicCount = 160;
                AllowEventCount = 4096;
                break;
            case eSubscriptionPlan.Enterprise:
                AllowCorporateCount = 15;
                AllowCompanyCount = 60;
                AllowFacilityCount = 360;
                AllowUserCount = 16;
                AllowDataUploadCount = 16;
                AllowDataDownloadCount = 32;
                AllowDataStoreCount = 512;
                AllowEmissionCount = 256;
                AllowTopicCount = 256;
                AllowEventCount = 8192;
                break;
        }
        #endregion
    }
    public void Add(TbSubscriptionUser user)
    {
        #region hide [2026-08-15]
        if (user == null) return;
        user.SubscriptionId = this.Id;
        user.Subscription = this;

        var existing = _users.FirstOrDefault(u =>
            (u.Id != 0 && u.Id == user.Id) ||
            (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)));

        if (existing == null)
        {
            _users.Add(user);
        }
        else if (!ReferenceEquals(existing, user))
        {
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;
            existing.Remarks = user.Remarks;
        }
        #endregion
    }
    public void Remove(TbSubscriptionUser user)
    {
        #region hide [2026-08-15]
        if (user == null) return;
        var existing = _users.FirstOrDefault(u =>
            ReferenceEquals(u, user) ||
            (u.Id != 0 && u.Id == user.Id) ||
            (!string.IsNullOrWhiteSpace(u.Email) && u.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)));

        if (existing != null)
        {
            _users.Remove(existing);
            existing.SubscriptionId = 0;
            existing.Subscription = null!;
        }
        #endregion
    }
    #endregion
}
