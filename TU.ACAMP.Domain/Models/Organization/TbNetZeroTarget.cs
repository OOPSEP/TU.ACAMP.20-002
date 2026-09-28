using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_netzero_target", Schema = "organization")]
public class TbNetZeroTarget : AuditableEntity<int>
{
    #region class properties
    public int SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    public int? CorporateId { get; set; }
    [ForeignKey(nameof(CorporateId))]
    public virtual TbCorporate? Corporate { get; set; }

    public int BaseYear { get; set; } = 2023;
    public int TargetYear { get; set; } = 2030;
    public decimal TargetReductionPercent { get; set; } = 50.0m;

    // [2026-09-25][Antigravity] ฟิลด์ควบคุมและเกณฑ์การแจ้งเตือนค่า GHG ประจำปีเข้าใกล้เป้าหมาย (>80%)
    public bool EnableAlert { get; set; } = true;
    public decimal AlertThresholdPercent { get; set; } = 80.0m;
    public string? AlertEmails { get; set; }
    public DateTime? LastAlertSentAt { get; set; }
    public int? LastAlertYear { get; set; }
    public decimal? LastAlertRatio { get; set; }
    #endregion
}
