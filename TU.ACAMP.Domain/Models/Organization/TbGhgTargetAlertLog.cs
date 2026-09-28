// [2026-09-25][Antigravity] Entity บันทึกประวัติการส่งอีเมลแจ้งเตือนเมื่อยอดปล่อย GHG เข้าใกล้หรือเกินเป้าหมายประจำปี
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_ghg_target_alert_log", Schema = "organization")]
public class TbGhgTargetAlertLog : AuditableEntity<int>
{
    #region class properties
    public int SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    public int? CorporateId { get; set; }
    [ForeignKey(nameof(CorporateId))]
    public virtual TbCorporate? Corporate { get; set; }

    public int ReportingYear { get; set; }

    public decimal CurrentEmissionsTon { get; set; }
    public decimal TargetCapTon { get; set; }
    public decimal ConsumptionPercent { get; set; }

    [StringLength(1000)]
    public string RecipientEmails { get; set; } = string.Empty;

    [StringLength(50)]
    public string AlertTier { get; set; } = "Warning_80";

    [StringLength(50)]
    public string DeliveryStatus { get; set; } = "Sent";

    [StringLength(1000)]
    public string? ErrorMessage { get; set; }
    #endregion
}
