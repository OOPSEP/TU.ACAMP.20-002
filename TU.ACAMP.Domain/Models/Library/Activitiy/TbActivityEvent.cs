using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Library
{
    [Table("tb_activity_event", Schema = "library")]
    public class TbActivityEvent : AuditableEntity<int>
    {
        #region class field  

        #endregion
        #region class properties 
        public int SubscriptionId { get; set; }
        [ForeignKey(nameof(SubscriptionId))]
        public virtual TbSubscription? Subscription { get; set; }

        [Required]
        public int TopicId { get; set; }
        [ForeignKey(nameof(TopicId))]
        public virtual TbActivityTopic? Topic { get; set; }

        [Required]
        // [GHG-17] Use AppTimeZone reporting period instead of DateTime.UtcNow default
        public DateTime TimeStamp { get; set; } = TU.ACAMP.Domain.Tools.AppTimeZone.ToReportingPeriod(TU.ACAMP.Domain.Tools.AppTimeZone.LocalNow);

        // [GHG-08] ISO 14064-1:2018 §5.2.3: Removals must be reported separately; offsets cannot reduce gross emissions.
        // Quantity must be non-negative.
        [Range(0, (double)decimal.MaxValue, ErrorMessage = "Quantity must be greater than or equal to 0 (Quantity ต้องไม่ติดลบ)")]
        [JsonPropertyName("Quantity")]
        public decimal Quantity { get; set; }
        [StringLength(250)]
        public string Remark { get; set; } = string.Empty;

        public JsonNode JsonDataFiles { get; set; } = JsonNode.Parse("[]")!;

        [StringLength(100)]
        public string? ExternalRefId { get; set; }

        [StringLength(20)]
        public string? SourceChannel { get; set; }

        public long? StagingId { get; set; }
        #endregion
        #region class method 
        public override string ToString()
        {
            #region hide [2026-08-15]
            return $"Activity Event: {Quantity} ({TimeStamp:yyyy-MM-dd})";
            #endregion
        }
        #endregion
    }
}
