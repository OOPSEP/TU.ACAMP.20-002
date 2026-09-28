// [2026-09-22][Antigravity] V16 entity models for reading source data during migration
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MigrationTool.Models;

[Table("tb_identity_subscribe")]
public class V16Subscription
{
    [Key]
    [Column("Id")]
    public Guid Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("RegistrationNo")]
    public string? RegistrationNo { get; set; }

    [Column("SubscribeType")]
    public int SubscribeType { get; set; }

    [Column("SubscribeLevel")]
    public int SubscribeLevel { get; set; }

    [Column("ActivatedDate")]
    public DateTime ActivatedDate { get; set; }

    [Column("ExpireDate")]
    public DateTime ExpireDate { get; set; }

    public List<V16Organization> Organizations { get; set; } = new();
}

[Table("tb_workspace_organization")]
public class V16Organization
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [ForeignKey("SubscribeId")]
    public V16Subscription? Subscription { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    public List<V16Subsidiary> Subsidiaries { get; set; } = new();
}

[Table("tb_workspace_subsidiary")]
public class V16Subsidiary
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("OrganizationId")]
    public int OrganizationId { get; set; }

    [ForeignKey("OrganizationId")]
    public V16Organization? Organization { get; set; }

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    [Column("EquityShareApproach")]
    public float EquityShareApproach { get; set; }

    public List<V16Facility> Facilities { get; set; } = new();
}

[Table("tb_workspace_facility")]
public class V16Facility
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("SubsidiaryId")]
    public int SubsidiaryId { get; set; }

    [ForeignKey("SubsidiaryId")]
    public V16Subsidiary? Subsidiary { get; set; }

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }
}

// [2026-09-22][Antigravity] V16 Emission Unit model mapped to tb_emission_unit
[Table("tb_emission_unit")]
public class V16EmissionUnit
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("Memo")]
    public string? Memo { get; set; }

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    [Column("IsReadOnly")]
    public bool IsReadOnly { get; set; }
}

// [2026-09-22][Antigravity] V16 Emission Group model mapped to tb_emission_group
[Table("tb_emission_group")]
public class V16EmissionGroup
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("Memo")]
    public string? Memo { get; set; }

    [Column("ScopeId")]
    public int ScopeId { get; set; }

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    [Column("IsReadOnly")]
    public bool IsReadOnly { get; set; }

    [Column("Category")]
    public string? Category { get; set; }

    [Column("JsonDataString")]
    public string? JsonDataString { get; set; }
}

// [2026-09-22][Antigravity] V16 Activity Scope model mapped to tb_activities_scope
[Table("tb_activities_scope")]
public class V16ActivityScope
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("Memo")]
    public string? Memo { get; set; }
}

// [2026-09-22][Antigravity] V16 Activity Group model mapped to tb_activities_group
[Table("tb_activities_group")]
public class V16ActivityGroup
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("ScopeId")]
    public int ScopeId { get; set; }

    [ForeignKey("ScopeId")]
    public V16ActivityScope? Scope { get; set; }
}

// [2026-09-22][Antigravity] V16 Emission Source model mapped to tb_emission_source
[Table("tb_emission_source")]
public class V16EmissionSource
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("GroupId")]
    public int GroupId { get; set; }

    [ForeignKey("GroupId")]
    public V16EmissionGroup? Group { get; set; }

    [Column("UnitId")]
    public int UnitId { get; set; }

    [ForeignKey("UnitId")]
    public V16EmissionUnit? Unit { get; set; }

    // [2026-09-23][Claude] Owner subscription of custom source (null/other = system default)
    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    // [2026-09-23][Claude] Reference text of emission factor
    [Column("Ref")]
    public string? Ref { get; set; }

    [Column("CO2")]
    public float CO2 { get; set; }

    [Column("FossilCH4")]
    public float FossilCH4 { get; set; }

    [Column("CH4")]
    public float CH4 { get; set; }

    [Column("N2O")]
    public float N2O { get; set; }

    [Column("SF6")]
    public float SF6 { get; set; }

    [Column("NF3")]
    public float NF3 { get; set; }

    [Column("HFCs")]
    public float HFCs { get; set; }

    [Column("PFCs")]
    public float PFCs { get; set; }

    [Column("gwpCO2")]
    public float gwpCO2 { get; set; }

    [Column("gwpFossilCH4")]
    public float gwpFossilCH4 { get; set; }

    [Column("gwpCH4")]
    public float gwpCH4 { get; set; }

    [Column("gwpN2O")]
    public float gwpN2O { get; set; }

    [Column("gwpSF6")]
    public float gwpSF6 { get; set; }

    [Column("gwpNF3")]
    public float gwpNF3 { get; set; }

    [Column("gwpHFCs")]
    public float gwpHFCs { get; set; }

    [Column("gwpPFCs")]
    public float gwpPFCs { get; set; }

    public decimal CalculateTotalEf()
    {
        float gCO2 = gwpCO2 > 0 ? gwpCO2 : 1f;
        float gFCH4 = gwpFossilCH4 > 0 ? gwpFossilCH4 : 28f;
        float gCH4 = gwpCH4 > 0 ? gwpCH4 : 28f;
        float gN2O = gwpN2O > 0 ? gwpN2O : 265f;
        float gSF6 = gwpSF6 > 0 ? gwpSF6 : 23500f;
        float gNF3 = gwpNF3 > 0 ? gwpNF3 : 16100f;
        float gHFCs = gwpHFCs > 0 ? gwpHFCs : 1f;
        float gPFCs = gwpPFCs > 0 ? gwpPFCs : 1f;

        double total = (CO2 * gCO2) + (FossilCH4 * gFCH4) + (CH4 * gCH4) + (N2O * gN2O) +
                       (SF6 * gSF6) + (NF3 * gNF3) + (HFCs * gHFCs) + (PFCs * gPFCs);
        return (decimal)Math.Round(total, 6);
    }
}

// [2026-09-22][Antigravity] V16 Activity Topic model mapped to tb_activities_topic
[Table("tb_activities_topic")]
public class V16ActivityTopic
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("Title")]
    public string Title { get; set; } = string.Empty;

    [Column("Memo")]
    public string? Memo { get; set; }

    [Column("ActivityGroupId")]
    public int ActivityGroupId { get; set; }

    [ForeignKey("ActivityGroupId")]
    public V16ActivityGroup? ActivityGroup { get; set; }

    [Column("SourceId")]
    public int SourceId { get; set; }

    [ForeignKey("SourceId")]
    public V16EmissionSource? Source { get; set; }

    [Column("FacilityId")]
    public int FacilityId { get; set; }

    [Column("EmissionSharePercent")]
    public float EmissionSharePercent { get; set; } = 100f;

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    public List<V16ActivityEvent> Events { get; set; } = new();
}

// [2026-09-22][Antigravity] V16 Activity Event model mapped to tb_activities_event
[Table("tb_activities_event")]
public class V16ActivityEvent
{
    [Key]
    [Column("Id")]
    public int Id { get; set; }

    [Column("TopicId")]
    public int TopicId { get; set; }

    [ForeignKey("TopicId")]
    public V16ActivityTopic? Topic { get; set; }

    [Column("TimeStamp")]
    public DateTime TimeStamp { get; set; }

    [Column("Quantity")]
    public double Quantity { get; set; }

    [Column("Title")]
    public string? Title { get; set; }

    [Column("Memo")]
    public string? Memo { get; set; }

    [Column("SubscribeId")]
    public Guid? SubscribeId { get; set; }
}
