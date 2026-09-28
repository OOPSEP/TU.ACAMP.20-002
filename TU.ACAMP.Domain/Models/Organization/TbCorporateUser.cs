using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Enums;

namespace TU.ACAMP.Domain.Models.Organization;

[Table("tb_organization_corporate_user", Schema = "organization")]
public class TbCorporateUser : AuditableEntity<int>
{
    #region class field  

    #endregion
    #region class properties 
    [Required]
    public int CorporateId { get; set; }
    [ForeignKey(nameof(CorporateId))]
    public virtual TbCorporate Corporate { get; set; } = null!;
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress]
    [StringLength(150)]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "Role is required")]
    public eCorporateRole Role { get; set; } = eCorporateRole.Viewer;

    public string Remarks { get; set; } = string.Empty;
    #endregion
    #region class method 
    public override string ToString()
    {
        return $"{Email} ({Role})";
    }
    #endregion
}
