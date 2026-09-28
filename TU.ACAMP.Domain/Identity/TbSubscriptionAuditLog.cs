using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Models;

namespace TU.ACAMP.Domain.Identity;

[Table("tb_subscription_audit_log")]
public class TbSubscriptionAuditLog : AuditableEntity<int>
{
    #region class properties
    [Required]
    public int SubscriptionId { get; set; }

    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    [Required]
    [StringLength(200)]
    public string SubscriptionTitle { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ActionType { get; set; } = string.Empty; // Create, Update, PlanChange, QuotaChange

    [Required]
    [StringLength(150)]
    public string ActorEmail { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string ActorRole { get; set; } = string.Empty;

    public DateTime ActionDate { get; set; } = DateTime.UtcNow;

    [StringLength(4000)]
    public string Details { get; set; } = string.Empty;

    [StringLength(50)]
    public string? IpAddress { get; set; }
    #endregion
}
