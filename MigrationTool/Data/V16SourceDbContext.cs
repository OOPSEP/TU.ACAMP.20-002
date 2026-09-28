// [2026-09-22][Antigravity] Source DbContext for reading V16 ACAMP database
using Microsoft.EntityFrameworkCore;
using MigrationTool.Models;

namespace MigrationTool.Data;

public class V16SourceDbContext : DbContext
{
    public DbSet<V16Subscription> Subscriptions => Set<V16Subscription>();
    public DbSet<V16Organization> Organizations => Set<V16Organization>();
    public DbSet<V16Subsidiary> Subsidiaries => Set<V16Subsidiary>();
    public DbSet<V16Facility> Facilities => Set<V16Facility>();
    public DbSet<V16EmissionUnit> EmissionUnits => Set<V16EmissionUnit>();
    public DbSet<V16EmissionGroup> EmissionGroups => Set<V16EmissionGroup>();
    public DbSet<V16ActivityScope> ActivityScopes => Set<V16ActivityScope>();
    public DbSet<V16ActivityGroup> ActivityGroups => Set<V16ActivityGroup>();
    public DbSet<V16EmissionSource> EmissionSources => Set<V16EmissionSource>();
    public DbSet<V16ActivityTopic> ActivityTopics => Set<V16ActivityTopic>();
    public DbSet<V16ActivityEvent> ActivityEvents => Set<V16ActivityEvent>();

    public V16SourceDbContext(DbContextOptions<V16SourceDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        #region hide [2026-09-22]
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<V16EmissionUnit>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16EmissionGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16ActivityScope>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16ActivityGroup>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16EmissionSource>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16ActivityTopic>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<V16ActivityEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasOne(e => e.Topic)
                .WithMany(t => t.Events)
                .HasForeignKey(e => e.TopicId);
        });

        modelBuilder.Entity<V16Subscription>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Organizations)
                  .WithOne(e => e.Subscription)
                  .HasForeignKey(e => e.SubscribeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<V16Organization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Subsidiaries)
                  .WithOne(e => e.Organization)
                  .HasForeignKey(e => e.OrganizationId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<V16Subsidiary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasMany(e => e.Facilities)
                  .WithOne(e => e.Subsidiary)
                  .HasForeignKey(e => e.SubsidiaryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<V16Facility>(entity =>
        {
            entity.HasKey(e => e.Id);
        });
        #endregion
    }
}
