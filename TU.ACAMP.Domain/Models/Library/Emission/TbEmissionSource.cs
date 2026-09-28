using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_emission_source", Schema = "library")]
public class TbEmissionSource : AuditableEntity<int>
{
    #region class field  

    #endregion
    #region class properties 
    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [StringLength(500)]
    public string Remark { get; set; } = string.Empty;
    public DateTime? DateApproved { get; set; }
    public DateTime? DateExpire { get; set; }
    public int? SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }
    public decimal CO2 { get; set; }
    public decimal FossilCH4 { get; set; }
    public decimal CH4 { get; set; }
    public decimal N2O { get; set; }
    public decimal SF6 { get; set; }
    public decimal NF3 { get; set; }
    public decimal HFCs { get; set; }
    public decimal PFCs { get; set; }
    public decimal gwpCO2 { get; set; }
    public decimal gwpFossilCH4 { get; set; }
    public decimal gwpCH4 { get; set; }
    public decimal gwpN2O { get; set; }
    public decimal gwpSF6 { get; set; }
    public decimal gwpNF3 { get; set; }
    public decimal gwpHFCs { get; set; }
    public decimal gwpPFCs { get; set; }
    /// <summary>
    /// [GHG-07] IPCC GWP Metric Version (ISO 14064-1:2018 §8.2)
    /// </summary>
    public eGwpVersion GwpVersion { get; set; } = eGwpVersion.AR5;
    [NotMapped]
    public decimal EF => (CO2 * gwpCO2) + (FossilCH4 * gwpFossilCH4) + (CH4 * gwpCH4) + (N2O * gwpN2O) + (SF6 * gwpSF6) + (NF3 * gwpNF3) + (HFCs * gwpHFCs) + (PFCs * gwpPFCs);
    public bool SelfCollect { get; set; }
    public bool Supplier { get; set; }
    public bool THLCIDB { get; set; }
    public bool TGOEF { get; set; }
    public bool ThaiRes { get; set; }
    public bool IntDB { get; set; }
    public bool Other { get; set; }
    public bool Substitute { get; set; }
    [NotMapped]
    public bool IsExpire => DateExpire.HasValue && DateExpire.Value.Date < AppTimeZone.LocalNow.Date;
    public bool IsReadOnly { get; set; } = false;
    [Description("Reference of the emission factor, e.g., source document or URL")]
    public string Ref { get; set; } = string.Empty;
    [Column(TypeName = "json")]
    public JsonNode JsonDataFiles { get; set; } = JsonNode.Parse("[]")!;
    public eSourceType SourceType { get; set; } = eSourceType.Custom;
    [Required(ErrorMessage = "Group is required")]
    public int GroupId { get; set; }
    [ForeignKey(nameof(GroupId))]
    public virtual TbEmissionGroup? Group { get; set; }
    [NotMapped]
    public eTgoScopeNo TgoScopeNo => Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
    [NotMapped]
    public eIsoCategoryNo IsoCategoryNo => Group?.IsoCategoryNo ?? eIsoCategoryNo.Category1;
    [Required(ErrorMessage = "Unit is required")]
    public int UnitId { get; set; }
    [ForeignKey("UnitId")]
    public virtual TbEmissionUnit? Unit { get; set; }
    #endregion
    #region class method 
    /// <summary>
    /// [GHG-12] Checks whether the emission factor is expired as of a specific reporting period end date.
    /// </summary>
    public bool IsExpiredAsOf(DateTime reportingPeriodEnd)
    {
        return DateExpire.HasValue && DateExpire.Value.Date < reportingPeriodEnd.Date;
    }

    public override string ToString()
    {
        #region hide [2026-07-06]
        return $"{Title} ({EF:N6} kgCO2e)";
        #endregion
    }

    public static TbEmissionSource Duplicate(TbEmissionSource source, int targetSubscriptionId)
    {
        #region hide [2026-08-17]
        if (source is null) throw new ArgumentNullException(nameof(source));
        return new TbEmissionSource
        {
            Title = $"{source.Title} (Copy)",
            Remark = source.Remark,
            DateApproved = source.DateApproved,
            DateExpire = source.DateExpire,
            SubscriptionId = targetSubscriptionId,
            CO2 = source.CO2,
            FossilCH4 = source.FossilCH4,
            CH4 = source.CH4,
            N2O = source.N2O,
            SF6 = source.SF6,
            NF3 = source.NF3,
            HFCs = source.HFCs,
            PFCs = source.PFCs,
            gwpCO2 = source.gwpCO2,
            gwpFossilCH4 = source.gwpFossilCH4,
            gwpCH4 = source.gwpCH4,
            gwpN2O = source.gwpN2O,
            gwpSF6 = source.gwpSF6,
            gwpNF3 = source.gwpNF3,
            gwpHFCs = source.gwpHFCs,
            gwpPFCs = source.gwpPFCs,
            GwpVersion = source.GwpVersion,
            SelfCollect = source.SelfCollect,
            Supplier = source.Supplier,
            THLCIDB = source.THLCIDB,
            TGOEF = source.TGOEF,
            ThaiRes = source.ThaiRes,
            IntDB = source.IntDB,
            Other = source.Other,
            Substitute = source.Substitute,
            IsReadOnly = false,
            Ref = source.Ref,
            JsonDataFiles = source.JsonDataFiles?.DeepClone() ?? JsonNode.Parse("[]")!,
            SourceType = eSourceType.Custom,
            GroupId = source.GroupId,
            UnitId = source.UnitId
        };
        #endregion
    }
    #endregion
}
