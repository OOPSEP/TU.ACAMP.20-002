using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Emissions;
using Xunit;

namespace TU.ACAMP.Tests;

public class EmissionSourceDeletionTests
{
    private (IDbContextFactory<ApplicationDbContext> dbFactory, ApplicationDbContext context, AuthenticationStateProvider authProvider) CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options);
        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        var mockAuthProvider = new Mock<AuthenticationStateProvider>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Email, "tester@acamp.com"),
            new Claim(ClaimTypes.Role, "Administrator")
        }, "TestAuth"));

        mockAuthProvider.Setup(a => a.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(user));

        return (mockFactory.Object, context, mockAuthProvider.Object);
    }

    private EmissionSourceService CreateService(IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider authProvider)
    {
        return new EmissionSourceService(dbFactory, authProvider, NullLogger<EmissionSourceService>.Instance);
    }

    [Fact]
    public async Task GetUsingTopicTitlesAsync_WhenSourceUsedByActiveTopics_ReturnsDistinctTopicTitles()
    {
        // [2026-09-20][Antigravity] ทดสอบดึงรายชื่อหัวข้อที่กำลังใช้งาน Source
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var source = new TbEmissionSource
        {
            Title = "Custom Source 101",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);

        var topicA = new TbActivityTopic { Title = "Topic A", IsActive = true };
        var topicB = new TbActivityTopic { Title = "Topic B", IsActive = true };
        var topicDeleted = new TbActivityTopic { Title = "Topic Deleted", IsActive = true };
        context.Topics.AddRange(topicA, topicB, topicDeleted);
        await context.SaveChangesAsync();

        var childA = new TbActivityTopicChild { TopicId = topicA.Id, Topic = topicA, SourceId = source.Id };
        var childB = new TbActivityTopicChild { TopicId = topicB.Id, Topic = topicB, SourceId = source.Id };
        var childDeletedTopic = new TbActivityTopicChild { TopicId = topicDeleted.Id, Topic = topicDeleted, SourceId = source.Id };
        context.TopicChilds.AddRange(childA, childB, childDeletedTopic);
        await context.SaveChangesAsync();

        // Soft delete topicDeleted
        topicDeleted.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        var usingTopics = await service.GetUsingTopicTitlesAsync(source.Id);

        Assert.Equal(2, usingTopics.Count);
        Assert.Contains("Topic A", usingTopics);
        Assert.Contains("Topic B", usingTopics);
        Assert.DoesNotContain("Topic Deleted", usingTopics);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceInUseByTopic_ThrowsInvalidOperationException()
    {
        // [2026-09-20][Antigravity] ทดสอบว่า DeleteAsync ปฏิเสธการลบและโยน Exception หากมี Topic ใช้งานอยู่
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var source = new TbEmissionSource
        {
            Title = "Active Custom Source",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);

        var topic = new TbActivityTopic { Title = "Boiler Operation", IsActive = true };
        context.Topics.Add(topic);
        await context.SaveChangesAsync();

        context.TopicChilds.Add(new TbActivityTopicChild
        {
            TopicId = topic.Id,
            Topic = topic,
            SourceId = source.Id
        });
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(source.Id));
        // [2026-09-24] ข้อความรูปแบบใหม่ของ DeleteGuard: แสดงชื่อต้นทางแรก และแจ้งให้ปลดที่ต้นทางก่อน
        Assert.Contains("currently used by topic 'Boiler Operation'", ex.Message);
        Assert.Contains("unassign it at the source first", ex.Message);
        Assert.DoesNotContain("and others", ex.Message);

        // ยืนยันว่า source ยังไม่ถูกลบ
        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var sourceAfter = await verifyContext.EmissionSources.FindAsync(source.Id);
        Assert.NotNull(sourceAfter);
        Assert.False(sourceAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceNotInUse_SoftDeletesSuccessfully()
    {
        // [2026-09-20][Antigravity] ทดสอบว่า DeleteAsync ทำงานสำเร็จเมื่อไม่มี Topic ใดใช้งาน Source
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var source = new TbEmissionSource
        {
            Title = "Unused Custom Source",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(source.Id);
        Assert.True(result);

        // ยืนยันว่า source ถูก soft delete จริง
        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var sourceAfter = await verifyContext.EmissionSources.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == source.Id);
        Assert.NotNull(sourceAfter);
        Assert.True(sourceAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenTopicChildIsDeleted_SoftDeletesSuccessfully()
    {
        // [2026-09-20][Antigravity] ทดสอบว่าหาก TopicChild ถูกลบแล้ว (unselected/soft-deleted) Source สามารถลบได้
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var source = new TbEmissionSource
        {
            Title = "Source Unselected from Topic",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);

        var topic = new TbActivityTopic { Title = "Previous Topic", IsActive = true };
        context.Topics.Add(topic);
        await context.SaveChangesAsync();

        var child = new TbActivityTopicChild
        {
            TopicId = topic.Id,
            Topic = topic,
            SourceId = source.Id
        };
        context.TopicChilds.Add(child);
        await context.SaveChangesAsync();

        // จำลองการ unselect / soft delete ออกจาก topic
        child.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(source.Id);
        Assert.True(result);

        // ยืนยันว่า source ถูก soft delete จริง
        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var sourceAfter = await verifyContext.EmissionSources.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == source.Id);
        Assert.NotNull(sourceAfter);
        Assert.True(sourceAfter.IsDeleted);
    }
}
