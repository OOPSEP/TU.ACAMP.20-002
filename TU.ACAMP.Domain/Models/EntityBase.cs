using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json.Serialization;

namespace TU.ACAMP.Domain.Models;

public abstract class EntityBase<TKey>
{
    #region class properties 
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("Id", Order = 0), JsonPropertyName("Id")]
    public TKey Id { get; set; } = default!;
    #endregion
}
public interface IAuditableEntity
{
    #region class properties
    int OrderId { get; set; }
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
    string UpdatedBy { get; set; }
    bool IsActive { get; set; }
    #endregion
}
public interface ISoftDelete
{
    #region class properties
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string DeletedBy { get; set; }
    #endregion
}

public abstract class AuditableEntity<TKey> : EntityBase<TKey>, IAuditableEntity, ISoftDelete
{
    #region class field
    #endregion

    #region class properties
    /// <summary>
    /// ลำดับการจัดเรียง (Display / Presentation Sequence)
    /// </summary>
    public int OrderId { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)]
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    [MaxLength(100)]
    public string UpdatedBy { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    [MaxLength(100)]
    public string DeletedBy { get; set; } = string.Empty;
    [MaxLength(20)]
    public string Status { get; set; } = "Active";
    #endregion

    #region class constructor
    protected AuditableEntity()
    {
        #region hide [2026-06-03]
        // Base constructor
        #endregion
    }
    #endregion

    #region class methods
    public virtual void SoftDelete(string deletedBy)
    {
        #region hide [2026-06-03]
        IsDeleted = true;
        IsActive = false;
        DeletedAt = DateTime.UtcNow;
        DeletedBy = deletedBy;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = deletedBy;
        Status = "Deleted";
        #endregion
    }

    public virtual void Restore()
    {
        #region hide [2026-06-03]
        IsDeleted = false;
        IsActive = true;
        DeletedAt = null;
        DeletedBy = string.Empty;
        Status = "Active";
        #endregion
    }
    #endregion
}