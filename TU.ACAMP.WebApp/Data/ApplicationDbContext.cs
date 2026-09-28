using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.WebApp.Data.Converters;

namespace TU.ACAMP.WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    #region class field  
    #endregion

    #region class properties 
    public DbSet<TbSubscription> Subscriptions => Set<TbSubscription>();
    public DbSet<TbSubscriptionUser> SubscriptionUsers => Set<TbSubscriptionUser>();
    public DbSet<TbCorporate> Corporates => Set<TbCorporate>();
    public DbSet<TbCorporateUser> CorporateUsers => Set<TbCorporateUser>();
    public DbSet<TbCompany> Companies => Set<TbCompany>();
    public DbSet<TbFacility> Facilities => Set<TbFacility>();
    public DbSet<TbEmissionGroup> EmissionGroups => Set<TbEmissionGroup>();
    public DbSet<TbEmissionUnit> Units => Set<TbEmissionUnit>();
    public DbSet<TbEmissionSource> EmissionSources => Set<TbEmissionSource>();
    public DbSet<TbEmissionMapping> EmissionMappings => Set<TbEmissionMapping>();
    public DbSet<TbActivityGroup> ActivityGroups => Set<TbActivityGroup>();
    public DbSet<TbActivityTopic> Topics => Set<TbActivityTopic>();
    public DbSet<TbActivityTopicChild> TopicChilds => Set<TbActivityTopicChild>();
    public DbSet<TbActivityEvent> Events => Set<TbActivityEvent>();
    public DbSet<TbNetZeroTarget> NetZeroTargets => Set<TbNetZeroTarget>();
    public DbSet<TbApiKey> ApiKeys => Set<TbApiKey>();
    public DbSet<TbEventStaging> EventStagings => Set<TbEventStaging>();
    public DbSet<TbSubscriptionAuditLog> SubscriptionAuditLogs => Set<TbSubscriptionAuditLog>();
    // [2026-09-25][Antigravity] DbSet สำหรับบันทึกประวัติการส่งอีเมลแจ้งเตือนเมื่อยอดปล่อย GHG เข้าใกล้หรือเกินเป้าหมาย
    public DbSet<TbGhgTargetAlertLog> GhgTargetAlertLogs => Set<TbGhgTargetAlertLog>();

    public string CurrentUsername { get; set; } = "System";
    #endregion

    #region class method 
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<JsonNode>()
            .HaveConversion<JsonNodeValueConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Ignore unmapped collection navigation properties for polymorphic Level + TargetId mapping
        builder.Entity<TbCorporate>().Ignore(c => c.Topics);
        builder.Entity<TbCompany>().Ignore(c => c.Topics);
        builder.Entity<TbFacility>().Ignore(f => f.Topics);

        // Topic navigation relationships
        builder.Entity<TbActivityTopic>()
            .HasMany(t => t.Childs)
            .WithOne(c => c.Topic)
            .HasForeignKey(c => c.TopicId);

        builder.Entity<TbActivityTopic>()
            .HasMany(t => t.Events)
            .WithOne(e => e.Topic)
            .HasForeignKey(e => e.TopicId);

        // Indexes for TbApiKey
        builder.Entity<TbApiKey>(e =>
        {
            e.HasIndex(k => k.KeyPrefix).IsUnique().HasDatabaseName("ux_api_key_prefix");
            e.HasIndex(k => new { k.SubscriptionId, k.IsActive }).HasDatabaseName("ix_api_key_subscription");
        });

        // Indexes for TbEventStaging
        builder.Entity<TbEventStaging>(e =>
        {
            e.HasIndex(s => new { s.SubscriptionId, s.IdempotencyKey, s.PayloadHash }).IsUnique().HasDatabaseName("ux_staging_idem");
            e.HasIndex(s => new { s.SubscriptionId, s.ExternalRefId }).IsUnique().HasDatabaseName("ux_staging_extref");
            e.HasIndex(s => new { s.Status, s.CreatedAt }).HasDatabaseName("ix_staging_status");
            e.HasIndex(s => s.BatchId).HasDatabaseName("ix_staging_batch");
            e.HasIndex(s => new { s.SubscriptionId, s.CreatedAt }).HasDatabaseName("ix_staging_sub_date");
        });

        // [2026-09-25][Antigravity] Database Provider Agnostic configurations for Net-Zero targets and alert logs
        builder.Entity<TbNetZeroTarget>(e =>
        {
            e.Property(t => t.TargetReductionPercent).HasPrecision(5, 2);
            e.Property(t => t.AlertThresholdPercent).HasPrecision(5, 2);
            e.Property(t => t.LastAlertRatio).HasPrecision(18, 4);
        });

        builder.Entity<TbGhgTargetAlertLog>(e =>
        {
            e.Property(l => l.CurrentEmissionsTon).HasPrecision(18, 4);
            e.Property(l => l.TargetCapTon).HasPrecision(18, 4);
            e.Property(l => l.ConsumptionPercent).HasPrecision(7, 2);
            e.HasIndex(l => new { l.SubscriptionId, l.ReportingYear, l.AlertTier }).HasDatabaseName("ix_alert_sub_year_tier");
        });

        // Apply global query filter for soft delete on all entities implementing ISoftDelete
        // and apply Utc DateTime converters for Auditable INSTANT properties
        var dateTimeUtcConverter = new DateTimeUtcValueConverter();
        var nullableDateTimeUtcConverter = new NullableDateTimeUtcValueConverter();

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Property(nameof(IAuditableEntity.CreatedAt)).HasConversion(dateTimeUtcConverter);
                builder.Entity(entityType.ClrType).Property(nameof(IAuditableEntity.UpdatedAt)).HasConversion(nullableDateTimeUtcConverter);
            }

            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Property(nameof(ISoftDelete.DeletedAt)).HasConversion(nullableDateTimeUtcConverter);

                if (entityType.BaseType == null)
                {
                    var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                    var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
                    var notExpression = System.Linq.Expressions.Expression.Not(property);
                    var lambda = System.Linq.Expressions.Expression.Lambda(notExpression, parameter);

                    builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                }
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditAndSoftDelete(CurrentUsername);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditAndSoftDelete(CurrentUsername);
        return base.SaveChanges();
    }

    public int SaveChanges(string currentUsername)
    {
        ApplyAuditAndSoftDelete(currentUsername);
        return base.SaveChanges();
    }

    public async Task<int> SaveChangesAsync(string currentUsername, CancellationToken cancellationToken = default)
    {
        ApplyAuditAndSoftDelete(currentUsername);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditAndSoftDelete(string currentUsername)
    {
        var username = string.IsNullOrEmpty(currentUsername) ? "System" : currentUsername;
        var now = TU.ACAMP.Domain.Tools.AppTimeZone.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditableEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.CurrentValues["CreatedAt"] = now;
                    entry.CurrentValues["CreatedBy"] = username;
                    entry.CurrentValues["IsActive"] = true;
                    entry.CurrentValues["IsDeleted"] = false;
                    entry.CurrentValues["Status"] = "Active";
                    entry.CurrentValues["UpdatedBy"] = string.Empty;
                    entry.CurrentValues["DeletedBy"] = string.Empty;
                }
                else if (entry.State == EntityState.Modified)
                {
                    entry.CurrentValues["UpdatedAt"] = now;
                    entry.CurrentValues["UpdatedBy"] = username;

                    entry.Property("CreatedAt").IsModified = false;
                    entry.Property("CreatedBy").IsModified = false;

                    // If entity was marked as soft-deleted by setting IsDeleted = true
                    if (entry.Entity is ISoftDelete)
                    {
                        var isDeletedProp = entry.Property("IsDeleted");
                        if (isDeletedProp.IsModified && (bool)entry.CurrentValues["IsDeleted"]!)
                        {
                            entry.CurrentValues["IsActive"] = false;
                            entry.CurrentValues["Status"] = "Deleted";
                            if (entry.CurrentValues["DeletedAt"] == null)
                            {
                                entry.CurrentValues["DeletedAt"] = now;
                            }
                            if (string.IsNullOrEmpty((string?)entry.CurrentValues["DeletedBy"]))
                            {
                                entry.CurrentValues["DeletedBy"] = username;
                            }
                        }
                    }
                }
            }

            if (entry.Entity is ISoftDelete && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.CurrentValues["IsDeleted"] = true;
                entry.CurrentValues["IsActive"] = false;
                entry.CurrentValues["Status"] = "Deleted";
                entry.CurrentValues["DeletedAt"] = now;
                entry.CurrentValues["DeletedBy"] = username;
                entry.CurrentValues["UpdatedAt"] = now;
                entry.CurrentValues["UpdatedBy"] = username;
            }
        }
    }
    #endregion
}
