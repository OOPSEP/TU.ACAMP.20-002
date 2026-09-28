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
using TU.ACAMP.WebApp.Services;
using TU.ACAMP.WebApp.Services.Emissions;
using Xunit;

namespace TU.ACAMP.Tests;

/// <summary>
/// [2026-09-24] ทดสอบกลไกป้องกันการลบข้อมูลที่ยังถูกอ้างอิง (Delete Protection) ของ Unit / Source / Group
/// </summary>
public class DeleteProtectionTests
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

    #region DeleteGuard message format

    [Fact]
    public void BuildBlockMessage_WhenNoReferences_ReturnsNull()
    {
        Assert.Null(DeleteGuard.BuildBlockMessage("emission unit", new List<DeleteReference>()));
    }

    [Fact]
    public void BuildBlockMessage_WhenSingleReference_ShowsNameWithoutOthers()
    {
        var refs = DeleteGuard.Normalize(new[] { new DeleteReference("emission source", "Diesel") });

        var message = DeleteGuard.BuildBlockMessage("emission unit", refs);

        Assert.NotNull(message);
        Assert.Contains("emission source 'Diesel'", message);
        Assert.DoesNotContain("and others", message);
        Assert.Contains("unassign it at the source first", message);
    }

    [Fact]
    public void BuildBlockMessage_WhenMultipleReferences_ShowsFirstNameAndOthers()
    {
        var refs = DeleteGuard.Normalize(new[]
        {
            new DeleteReference("emission source", "Diesel"),
            new DeleteReference("emission source", "Diesel"), // ซ้ำ ต้องถูกตัดออก
            new DeleteReference("topic", "Boiler")
        });

        var message = DeleteGuard.BuildBlockMessage("emission unit", refs);

        Assert.Equal(2, refs.Count);
        Assert.NotNull(message);
        Assert.Contains("emission source 'Diesel' and others", message);
        Assert.DoesNotContain("Boiler", message);
    }

    [Fact]
    public void Normalize_WhenDuplicateIsOnlyReference_DoesNotShowOthers()
    {
        var refs = DeleteGuard.Normalize(new[]
        {
            new DeleteReference("topic", "Boiler"),
            new DeleteReference("topic", "Boiler")
        });

        var message = DeleteGuard.BuildBlockMessage("emission source", refs);

        Assert.Single(refs);
        Assert.DoesNotContain("and others", message);
    }

    #endregion

    #region Emission Unit

    [Fact]
    public async Task UnitDelete_WhenUsedOnlyByTopicChildActivityUnit_ThrowsAndKeepsUnit()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionUnitService(dbFactory, authProvider, NullLogger<EmissionUnitService>.Instance);

        var unit = new TbEmissionUnit { Title = "litre", SourceType = eSourceType.Custom, IsActive = true };
        context.Units.Add(unit);
        var topic = new TbActivityTopic { Title = "Generator Fuel", IsActive = true };
        context.Topics.Add(topic);
        await context.SaveChangesAsync();

        context.TopicChilds.Add(new TbActivityTopicChild { TopicId = topic.Id, Topic = topic, SourceId = 999, ActivityUnitId = unit.Id });
        await context.SaveChangesAsync();

        var blockMessage = await service.GetDeleteBlockMessageAsync(unit.Id);
        Assert.NotNull(blockMessage);
        Assert.Contains("topic 'Generator Fuel'", blockMessage);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(unit.Id));
        Assert.Contains("topic 'Generator Fuel'", ex.Message);

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var unitAfter = await verifyContext.Units.IgnoreQueryFilters().FirstAsync(u => u.Id == unit.Id);
        Assert.False(unitAfter.IsDeleted);
    }

    [Fact]
    public async Task UnitDelete_WhenUsedBySourceAndTopic_ShowsFirstSourceAndOthers()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionUnitService(dbFactory, authProvider, NullLogger<EmissionUnitService>.Instance);

        var unit = new TbEmissionUnit { Title = "kg", SourceType = eSourceType.Custom, IsActive = true };
        context.Units.Add(unit);
        var topic = new TbActivityTopic { Title = "Waste", IsActive = true };
        context.Topics.Add(topic);
        await context.SaveChangesAsync();

        context.EmissionSources.Add(new TbEmissionSource { Title = "Gasoline", UnitId = unit.Id, SourceType = eSourceType.Custom, IsActive = true });
        context.EmissionSources.Add(new TbEmissionSource { Title = "Diesel", UnitId = unit.Id, SourceType = eSourceType.Custom, IsActive = true });
        context.TopicChilds.Add(new TbActivityTopicChild { TopicId = topic.Id, Topic = topic, SourceId = 999, ActivityUnitId = unit.Id });
        await context.SaveChangesAsync();

        var references = await service.GetDeleteReferencesAsync(unit.Id);
        Assert.Equal(3, references.Count);

        var blockMessage = await service.GetDeleteBlockMessageAsync(unit.Id);
        Assert.Contains("emission source 'Diesel' and others", blockMessage);
    }

    [Fact]
    public async Task UnitDelete_WhenTopicOfChildIsDeleted_SoftDeletesSuccessfully()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionUnitService(dbFactory, authProvider, NullLogger<EmissionUnitService>.Instance);

        var unit = new TbEmissionUnit { Title = "m3", SourceType = eSourceType.Custom, IsActive = true };
        context.Units.Add(unit);
        var topic = new TbActivityTopic { Title = "Old Topic", IsActive = true };
        context.Topics.Add(topic);
        await context.SaveChangesAsync();

        context.TopicChilds.Add(new TbActivityTopicChild { TopicId = topic.Id, Topic = topic, SourceId = 999, ActivityUnitId = unit.Id });
        await context.SaveChangesAsync();

        topic.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        Assert.Null(await service.GetDeleteBlockMessageAsync(unit.Id));
        Assert.True(await service.DeleteAsync(unit.Id));
    }

    #endregion

    #region Emission Source

    [Fact]
    public async Task SourceDelete_WhenUsedOnlyByEmissionMapping_ThrowsAndKeepsSource()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionSourceService(dbFactory, authProvider, NullLogger<EmissionSourceService>.Instance);

        var source = new TbEmissionSource { Title = "Custom Diesel", SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionSources.Add(source);
        await context.SaveChangesAsync();

        context.EmissionMappings.Add(new TbEmissionMapping { Title = "Diesel Mapping", Scope3ChildId = source.Id, IsActive = true });
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(source.Id));
        Assert.Contains("emission mapping 'Diesel Mapping'", ex.Message);

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var sourceAfter = await verifyContext.EmissionSources.IgnoreQueryFilters().FirstAsync(s => s.Id == source.Id);
        Assert.False(sourceAfter.IsDeleted);
    }

    [Fact]
    public async Task SourceDelete_WhenMappingIsDeleted_SoftDeletesSuccessfully()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionSourceService(dbFactory, authProvider, NullLogger<EmissionSourceService>.Instance);

        var source = new TbEmissionSource { Title = "Custom LPG", SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionSources.Add(source);
        await context.SaveChangesAsync();

        var mapping = new TbEmissionMapping { Title = "LPG Mapping", Scope1ChildId = source.Id, IsActive = true };
        context.EmissionMappings.Add(mapping);
        await context.SaveChangesAsync();

        mapping.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        Assert.Null(await service.GetDeleteBlockMessageAsync(source.Id));
        Assert.True(await service.DeleteAsync(source.Id));
    }

    #endregion

    #region Emission Group

    [Fact]
    public async Task GroupDelete_WhenUsedByMultipleSources_ShowsFirstSourceAndOthers()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionGroupService(dbFactory, authProvider, NullLogger<EmissionGroupService>.Instance);

        var group = new TbEmissionGroup { Title = "Custom Combustion", Code = "9901", SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionGroups.Add(group);
        await context.SaveChangesAsync();

        context.EmissionSources.Add(new TbEmissionSource { Title = "Gasoline", GroupId = group.Id, SourceType = eSourceType.Custom, IsActive = true });
        context.EmissionSources.Add(new TbEmissionSource { Title = "Diesel", GroupId = group.Id, SourceType = eSourceType.Custom, IsActive = true });
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(group.Id));
        Assert.Contains("emission source 'Diesel' and others", ex.Message);

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var groupAfter = await verifyContext.EmissionGroups.IgnoreQueryFilters().FirstAsync(g => g.Id == group.Id);
        Assert.False(groupAfter.IsDeleted);
    }

    [Fact]
    public async Task GroupDelete_WhenUsedOnlyByTopic_Throws()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionGroupService(dbFactory, authProvider, NullLogger<EmissionGroupService>.Instance);

        var group = new TbEmissionGroup { Title = "Custom Refrigerant", Code = "9902", SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionGroups.Add(group);
        await context.SaveChangesAsync();

        context.Topics.Add(new TbActivityTopic { Title = "Chiller Refill", EmissionGroupId = group.Id, IsActive = true });
        await context.SaveChangesAsync();

        var blockMessage = await service.GetDeleteBlockMessageAsync(group.Id);
        Assert.Contains("topic 'Chiller Refill'", blockMessage);
        Assert.DoesNotContain("and others", blockMessage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(group.Id));
    }

    [Fact]
    public async Task GroupDelete_WhenNotInUse_SoftDeletesSuccessfully()
    {
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = new EmissionGroupService(dbFactory, authProvider, NullLogger<EmissionGroupService>.Instance);

        var group = new TbEmissionGroup { Title = "Unused Group", Code = "9903", SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionGroups.Add(group);
        await context.SaveChangesAsync();

        Assert.Null(await service.GetDeleteBlockMessageAsync(group.Id));
        Assert.True(await service.DeleteAsync(group.Id));

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var groupAfter = await verifyContext.EmissionGroups.IgnoreQueryFilters().FirstAsync(g => g.Id == group.Id);
        Assert.True(groupAfter.IsDeleted);
    }

    #endregion
}
