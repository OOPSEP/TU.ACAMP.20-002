using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_event_staging", Schema = "library")]
public class TbEventStaging : AuditableEntity<long>
{
    [Required]
    public int SubscriptionId { get; set; }

    [Required]
    [StringLength(32)]
    public string BatchId { get; set; } = string.Empty;

    [StringLength(100)]
    public string? IdempotencyKey { get; set; }

    [StringLength(100)]
    public string? ExternalRefId { get; set; }

    [Required]
    [StringLength(64)]
    public string PayloadHash { get; set; } = string.Empty;

    [Required]
    [StringLength(64)]
    public string TopicKeyRaw { get; set; } = string.Empty;

    public int? ResolvedTopicId { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal Quantity { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public int OccurredAtOffsetMinutes { get; set; }

    [StringLength(250)]
    public string Title { get; set; } = string.Empty;

    [StringLength(500)]
    public string Remark { get; set; } = string.Empty;

    [Column(TypeName = "json")]
    public JsonNode PayloadJson { get; set; } = JsonNode.Parse("{}")!;

    [StringLength(500)]
    public string FailureReason { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 0;

    public int? PromotedEventId { get; set; }

    [StringLength(20)]
    public string SourceChannel { get; set; } = "api-batch";
}
