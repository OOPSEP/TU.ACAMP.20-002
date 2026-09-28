using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json.Serialization;
using TU.ACAMP.Domain.Helper;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_activity_topic_child", Schema = "library")]
public class TbActivityTopicChild : AuditableEntity<int>
{
    #region class field  

    #endregion
    #region class properties 
    [Required]
    [Column("TopicId")]
    public int TopicId { get; set; }

    [ForeignKey(nameof(TopicId))]
    [JsonIgnore]
    public virtual TbActivityTopic? Topic { get; set; }
 
    public decimal? UnitConversionRatio { get; set; } = 1;
    [Column("ActivityUnitId")]
    [JsonPropertyName("ActivityUnitId")]
    public int? ActivityUnitId { get; set; }

    [ForeignKey(nameof(ActivityUnitId))]
    [JsonPropertyName("ActivityUnit")]
    public virtual TbEmissionUnit? ActivityUnit { get; set; }

    [Required]
    [Column("ActivityGroupId")]
    [JsonPropertyName("ActivityGroupId")]
    public int ActivityGroupId { get; set; }

    [ForeignKey(nameof(ActivityGroupId))]
    [JsonPropertyName("ActivityGroup")]
    public virtual TbActivityGroup? ActivityGroup { get; set; }

    [Required]
    [Column("SourceId")]
    [JsonPropertyName("SourceId")]
    public int SourceId { get; set; }

    [ForeignKey(nameof(SourceId))]
    [JsonPropertyName("Source")]
    public virtual TbEmissionSource? Source { get; set; }

    [Column("EmissionSharePercent")]
    [Description("Emission Share %")]
    [Percent0To100]
    public float EmissionSharePercent { get; set; } = 100;
    #endregion
    #region class method 

    #endregion
}
