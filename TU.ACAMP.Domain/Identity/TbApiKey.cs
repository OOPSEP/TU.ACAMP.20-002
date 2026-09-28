using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models;

namespace TU.ACAMP.Domain.Identity;

[Table("tb_api_key", Schema = "identity")]
public class TbApiKey : AuditableEntity<int>
{
    [Required]
    public int SubscriptionId { get; set; }

    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    /// <summary>
    /// Optional Scope filtering for Topic level (Corporate = 1, Company = 2, Facility = 3).
    /// If null, API Key can access all Topics across the Subscription workspace.
    /// </summary>
    public eTopicLevel? ScopeLevel { get; set; }
    public int? ScopeTargetId { get; set; }

    [Required]
    [StringLength(100)]
    public string KeyName { get; set; } = string.Empty;

    [Required]
    [StringLength(16)]
    public string KeyPrefix { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    public string KeyHash { get; set; } = string.Empty;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public int? CustomRateLimitPerMin { get; set; }

    public long TotalIngestedEventsCount { get; set; } = 0;
}
