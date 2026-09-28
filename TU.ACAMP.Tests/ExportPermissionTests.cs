using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Identity;
using TU.ACAMP.WebApp.Services.Reports;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>
/// [2026-09-19][Antigravity] Unit tests verifying export permissions across roles:
/// Only users with edit permissions (Owner, Admin, Editor) can export data across pages,
/// while Viewer users cannot export (except Reports which remains accessible for export and print).
/// </summary>
public class ExportPermissionTests
{
    private class DummyDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public DummyDbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ApplicationDbContext(_options));
    }

    private class MockAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _principal;
        public MockAuthStateProvider(ClaimsPrincipal principal) => _principal = principal;
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_principal));
    }

    private ApplicationDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(eSubscriptionRole.Owner, true)]
    [InlineData(eSubscriptionRole.Admin, true)]
    [InlineData(eSubscriptionRole.Editor, true)]
    [InlineData(eSubscriptionRole.Consultant, false)]
    [InlineData(eSubscriptionRole.Viewer, false)]
    public void SubscriptionRole_CanEdit_FollowsPermissionHierarchy(eSubscriptionRole role, bool expectedCanEdit)
    {
        // [2026-09-19][Antigravity] Verify SubscriptionRole CanEdit extension method
        Assert.Equal(expectedCanEdit, role.CanEdit());
    }

    [Theory]
    [InlineData(eCorporateRole.Manager, true)]
    [InlineData(eCorporateRole.Editor, true)]
    [InlineData(eCorporateRole.Consultant, false)]
    [InlineData(eCorporateRole.Viewer, false)]
    public void CorporateRole_CanEdit_FollowsPermissionHierarchy(eCorporateRole role, bool expectedCanEdit)
    {
        // [2026-09-19][Antigravity] Verify CorporateRole CanEdit extension method
        Assert.Equal(expectedCanEdit, role.CanEdit());
    }

    [Fact]
    public async Task SubscriptionService_GetEditableSubscriptionIdsAsync_ViewerHasNoEditableSubscriptions()
    {
        // [2026-09-19][Antigravity] Verify a Viewer user has empty editable subscription IDs
        var dbName = $"ExportPerm_Viewer_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var db = new ApplicationDbContext(options))
        {
            var sub = new TbSubscription
            {
                Id = 101,
                Title = "Viewer Workspace",
                OwnerEmail = "owner@test.com",
                IsActive = true
            };
            db.Subscriptions.Add(sub);

            var subUser = new TbSubscriptionUser
            {
                Id = 201,
                SubscriptionId = 101,
                Email = "viewer@test.com",
                Role = eSubscriptionRole.Viewer,
                IsActive = true
            };
            db.SubscriptionUsers.Add(subUser);
            await db.SaveChangesAsync();
        }

        var factory = new DummyDbContextFactory(options);
        var authProvider = new MockAuthStateProvider(new ClaimsPrincipal());
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        var emailSender = new EmailSender(config, NullLogger<EmailSender>.Instance);
        var service = new SubscriptionService(factory, authProvider, emailSender, NullLogger<SubscriptionService>.Instance);

        var editableIds = await service.GetEditableSubscriptionIdsAsync("viewer@test.com", isAdmin: false);

        Assert.Empty(editableIds);
    }

    [Fact]
    public async Task SubscriptionService_GetEditableSubscriptionIdsAsync_EditorHasEditableSubscriptions()
    {
        // [2026-09-19][Antigravity] Verify an Editor user has editable subscription IDs
        var dbName = $"ExportPerm_Editor_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var db = new ApplicationDbContext(options))
        {
            var sub1 = new TbSubscription
            {
                Id = 301,
                Title = "Editable Workspace",
                OwnerEmail = "owner@test.com",
                IsActive = true
            };
            var sub2 = new TbSubscription
            {
                Id = 302,
                Title = "Viewer Workspace",
                OwnerEmail = "owner@test.com",
                IsActive = true
            };
            db.Subscriptions.AddRange(sub1, sub2);

            var editorUser = new TbSubscriptionUser
            {
                Id = 401,
                SubscriptionId = 301,
                Email = "user@test.com",
                Role = eSubscriptionRole.Editor,
                IsActive = true
            };
            var viewerUser = new TbSubscriptionUser
            {
                Id = 402,
                SubscriptionId = 302,
                Email = "user@test.com",
                Role = eSubscriptionRole.Viewer,
                IsActive = true
            };
            db.SubscriptionUsers.AddRange(editorUser, viewerUser);
            await db.SaveChangesAsync();
        }

        var factory = new DummyDbContextFactory(options);
        var authProvider = new MockAuthStateProvider(new ClaimsPrincipal());
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        var emailSender = new EmailSender(config, NullLogger<EmailSender>.Instance);
        var service = new SubscriptionService(factory, authProvider, emailSender, NullLogger<SubscriptionService>.Instance);

        var editableIds = await service.GetEditableSubscriptionIdsAsync("user@test.com", isAdmin: false);

        Assert.Single(editableIds);
        Assert.Contains(301, editableIds);
        Assert.DoesNotContain(302, editableIds);
    }

    [Fact]
    public async Task SubscriptionService_GetEditableSubscriptionIdsAsync_AdminHasAllSubscriptions()
    {
        // [2026-09-19][Antigravity] Verify an Administrator has edit permissions across all subscriptions
        var dbName = $"ExportPerm_Admin_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var db = new ApplicationDbContext(options))
        {
            db.Subscriptions.AddRange(
                new TbSubscription { Id = 501, Title = "Sub 1", OwnerEmail = "o1@test.com", IsActive = true },
                new TbSubscription { Id = 502, Title = "Sub 2", OwnerEmail = "o2@test.com", IsActive = true }
            );
            await db.SaveChangesAsync();
        }

        var factory = new DummyDbContextFactory(options);
        var authProvider = new MockAuthStateProvider(new ClaimsPrincipal());
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        var emailSender = new EmailSender(config, NullLogger<EmailSender>.Instance);
        var service = new SubscriptionService(factory, authProvider, emailSender, NullLogger<SubscriptionService>.Instance);

        var editableIds = await service.GetEditableSubscriptionIdsAsync("admin@test.com", isAdmin: true);

        Assert.Equal(2, editableIds.Count);
        Assert.Contains(501, editableIds);
        Assert.Contains(502, editableIds);
    }

    [Fact]
    public async Task SubscriptionService_GetSubscriptionsForUserAsync_ExcludesDeletedSubscriptions()
    {
        // [2026-09-28][Gemini] ตรวจสอบว่า GetSubscriptionsForUserAsync ไม่ดึง Subscription ที่ IsDeleted == true ทั้งฝั่ง Admin และ User ทั่วไป
        var dbName = $"Sub_DeletedCheck_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var db = new ApplicationDbContext(options))
        {
            db.Subscriptions.AddRange(
                new TbSubscription { Id = 601, Title = "Active Sub 1", OwnerEmail = "user1@example.com", IsActive = true, IsDeleted = false, ExpiryDate = DateTime.UtcNow.AddMonths(1) },
                new TbSubscription { Id = 602, Title = "Deleted Sub 2", OwnerEmail = "user1@example.com", IsActive = true, IsDeleted = true, ExpiryDate = DateTime.UtcNow.AddMonths(1) },
                new TbSubscription { Id = 603, Title = "Deleted Sub 3 (Other Owner)", OwnerEmail = "other@example.com", IsActive = true, IsDeleted = true, ExpiryDate = DateTime.UtcNow.AddMonths(1) }
            );
            await db.SaveChangesAsync();
        }

        var factory = new DummyDbContextFactory(options);
        var authProvider = new MockAuthStateProvider(new ClaimsPrincipal());
        var config = new Microsoft.Extensions.Configuration.ConfigurationManager();
        var emailSender = new EmailSender(config, NullLogger<EmailSender>.Instance);
        var service = new SubscriptionService(factory, authProvider, emailSender, NullLogger<SubscriptionService>.Instance);

        // 1. ตรวจสอบฝั่ง Admin (ผ่าน GetAllAsync)
        var adminSubs = await service.GetSubscriptionsForUserAsync("admin@example.com", isAdmin: true, includeInactive: true);
        Assert.Single(adminSubs);
        Assert.Equal(601, adminSubs[0].Id);
        Assert.DoesNotContain(adminSubs, s => s.IsDeleted);

        // 2. ตรวจสอบฝั่ง User ทั่วไป (ผ่าน subIds & OwnerEmail)
        var userSubs = await service.GetSubscriptionsForUserAsync("user1@example.com", isAdmin: false, includeInactive: true);
        Assert.Single(userSubs);
        Assert.Equal(601, userSubs[0].Id);
        Assert.DoesNotContain(userSubs, s => s.IsDeleted);
    }
}
