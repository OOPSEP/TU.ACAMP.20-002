using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models;

namespace TU.ACAMP.Domain.Identity;

[Table("tb_subscription_users")]
public class TbSubscriptionUser : AuditableEntity<int>
{
    #region class field  

    #endregion
    #region class properties 
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Role is required")]
    public eSubscriptionRole Role { get; set; } = eSubscriptionRole.Viewer;
    [Required]
    public int SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription Subscription { get; set; } = null!;
    public string Remarks { get; set; } = string.Empty;
    #endregion
    #region class method 
    public override string ToString()
    {
        return $"{Email} ({Role})";
    }
    #endregion
}
