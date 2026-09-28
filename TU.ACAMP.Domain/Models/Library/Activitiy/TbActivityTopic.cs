using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Nodes;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_activity_topic", Schema = "library")]
public class TbActivityTopic : AuditableEntity<int>
{
    #region class field  
    private readonly List<TbActivityEvent> _events = new();
    private readonly List<TbActivityTopicChild> _childs = new();
    #endregion

    #region class properties 
    [Required]
    public int SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }

    /// <summary>
    /// ระดับและ ID ของขอบเขตองค์กรที่หัวข้อนี้ถูกใช้งาน (Corporate = 1, Company = 2, Facility = 3)
    /// </summary>
    public eTopicLevel Level { get; set; } = eTopicLevel.Corporate;
    public int TargetId { get; set; }

    public int EmissionGroupId { get; set; }
    [ForeignKey(nameof(EmissionGroupId))]
    public virtual TbEmissionGroup? EmissionGroup { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;



    [StringLength(500)]
    public string Remark { get; set; } = string.Empty;

    public JsonNode JsonDataFiles { get; set; } = JsonNode.Parse("[]")!;
    public virtual IReadOnlyCollection<TbActivityEvent> Events => _events.AsReadOnly();
    public virtual IReadOnlyCollection<TbActivityTopicChild> Childs => _childs.AsReadOnly();
    #endregion

    #region class method 
    public override string ToString()
    {
        #region hide [2026-08-15]
        return $"Activity Topic ({Level}): {Title}";
        #endregion
    }
    public void Add(TbActivityEvent item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        item.TopicId = this.Id;
        item.Topic = this;
        if (!_events.Contains(item)) _events.Add(item);
        #endregion
    }
    public void Remove(TbActivityEvent item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        if (_events.Contains(item))
        {
            _events.Remove(item);
            item.TopicId = 0;
            item.Topic = null!;
        }
        #endregion
    }
    public void Add(TbActivityTopicChild item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        item.TopicId = this.Id;
        item.Topic = this;
        if (!_childs.Contains(item)) _childs.Add(item);
        #endregion
    }
    public void Remove(TbActivityTopicChild item)
    {
        #region hide [2026-08-15]
        if (item == null) return;
        if (_childs.Contains(item))
        {
            _childs.Remove(item);
            item.TopicId = 0;
            item.Topic = null!;
        }
        #endregion
    }
    #endregion
}
