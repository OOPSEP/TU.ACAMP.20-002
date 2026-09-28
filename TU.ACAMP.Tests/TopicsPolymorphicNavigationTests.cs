using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Organization;
using Xunit;

namespace TU.ACAMP.Tests;

public class TopicsPolymorphicNavigationTests
{
    private (IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider authProvider) CreateInMemoryContextFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var mockFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(default))
            .ReturnsAsync(() => new ApplicationDbContext(options));

        var mockAuthProvider = new Mock<AuthenticationStateProvider>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Email, "auditor@acamp.com"),
            new Claim(ClaimTypes.Role, "Administrator")
        }, "TestAuth"));

        mockAuthProvider.Setup(a => a.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(user));

        return (mockFactory.Object, mockAuthProvider.Object);
    }

    private async Task<(TbSubscription sub, TbCorporate corp, TbCompany comp)> SeedBaseHierarchyAsync(ApplicationDbContext context, string prefix = "Test")
    {
        var sub = new TbSubscription { Title = $"{prefix} Sub" };
        context.Subscriptions.Add(sub);
        await context.SaveChangesAsync();

        var corp = new TbCorporate { SubscriptionId = sub.Id, Title = $"{prefix} Corp" };
        context.Corporates.Add(corp);
        await context.SaveChangesAsync();

        var comp = new TbCompany { CorporateId = corp.Id, Title = $"{prefix} Company" };
        context.Companies.Add(comp);
        await context.SaveChangesAsync();

        return (sub, corp, comp);
    }

    [Fact]
    public async Task GetByIdAsync_IncludeTopics_ReturnsCompanyTopics()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(dbName);
        var service = new CompanyService(dbFactory, authProvider, NullLogger<CompanyService>.Instance);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var (sub, corp, comp) = await SeedBaseHierarchyAsync(context, "GetById1");

            var topic1 = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Company,
                TargetId = comp.Id,
                Title = "Topic 1",
                IsDeleted = false
            };
            var topic2 = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Company,
                TargetId = comp.Id,
                Title = "Topic 2",
                IsDeleted = false
            };
            context.Topics.AddRange(topic1, topic2);
            await context.SaveChangesAsync();
        }

        // Test GetByIdAsync with includeTopics = true
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var comp = await context.Companies.FirstAsync();
            var result = await service.GetByIdAsync(comp.Id, includeFacilities: false, includeTopics: true);

            Assert.NotNull(result);
            Assert.Equal(2, result.Topics.Count);
            Assert.Contains(result.Topics, t => t.Title == "Topic 1");
            Assert.Contains(result.Topics, t => t.Title == "Topic 2");
        }
    }

    [Fact]
    public async Task GetByIdAsync_IncludeTopics_ExcludesDeletedAndOtherTargets()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(dbName);
        var service = new CompanyService(dbFactory, authProvider, NullLogger<CompanyService>.Instance);

        int targetCompanyId;
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var (sub, corp, comp1) = await SeedBaseHierarchyAsync(context, "Target");
            targetCompanyId = comp1.Id;

            var comp2 = new TbCompany { CorporateId = corp.Id, Title = "Other Company" };
            context.Companies.Add(comp2);
            await context.SaveChangesAsync();

            // Valid Topic
            var validTopic = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Company,
                TargetId = targetCompanyId,
                Title = "Valid Topic",
                IsDeleted = false
            };
            // Deleted Topic
            var deletedTopic = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Company,
                TargetId = targetCompanyId,
                Title = "Deleted Topic",
                IsDeleted = true
            };
            // Other Company Topic
            var otherCompanyTopic = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Company,
                TargetId = comp2.Id,
                Title = "Other Company Topic",
                IsDeleted = false
            };
            // Facility Level Topic with same TargetId number
            var facilityLevelTopic = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Facility,
                TargetId = targetCompanyId,
                Title = "Facility Level Topic",
                IsDeleted = false
            };

            context.Topics.AddRange(validTopic, deletedTopic, otherCompanyTopic, facilityLevelTopic);
            await context.SaveChangesAsync();

            deletedTopic.SoftDelete("test-user-id");
            await context.SaveChangesAsync();
        }

        var result = await service.GetByIdAsync(targetCompanyId, includeFacilities: false, includeTopics: true);

        Assert.NotNull(result);
        Assert.Single(result.Topics);
        Assert.Equal("Valid Topic", result.Topics.First().Title);
    }

    [Fact]
    public async Task DeleteAsync_FacilityWithoutEvents_SoftDeletesFacilityAndItsTopics()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(dbName);
        var facilityService = new FacilityService(dbFactory, authProvider, NullLogger<FacilityService>.Instance);

        int facilityId;
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var (sub, corp, comp) = await SeedBaseHierarchyAsync(context, "FacDel");
            var facility = new TbFacility { CompanyId = comp.Id, Title = "Facility Clean" };
            context.Facilities.Add(facility);
            await context.SaveChangesAsync();
            facilityId = facility.Id;

            var topic1 = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Facility,
                TargetId = facilityId,
                Title = "Facility Topic 1",
                IsDeleted = false
            };
            var topic2 = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Facility,
                TargetId = facilityId,
                Title = "Facility Topic 2",
                IsDeleted = false
            };
            context.Topics.AddRange(topic1, topic2);
            await context.SaveChangesAsync();
        }

        var deleteResult = await facilityService.DeleteAsync(facilityId);
        Assert.True(deleteResult);

        // Verify tracked soft delete in DB
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var facility = await context.Facilities.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == facilityId);
            Assert.NotNull(facility);
            Assert.True(facility.IsDeleted);

            var topics = await context.Topics.IgnoreQueryFilters().Where(t => t.Level == eTopicLevel.Facility && t.TargetId == facilityId).ToListAsync();
            Assert.Equal(2, topics.Count);
            Assert.All(topics, t => Assert.True(t.IsDeleted));
        }
    }

    [Fact]
    public async Task DeleteAsync_FacilityWithEvents_ThrowsAndChangesNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(dbName);
        var facilityService = new FacilityService(dbFactory, authProvider, NullLogger<FacilityService>.Instance);

        int facilityId;
        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var (sub, corp, comp) = await SeedBaseHierarchyAsync(context, "FacWithEvents");
            var facility = new TbFacility { CompanyId = comp.Id, Title = "Facility With Events" };
            context.Facilities.Add(facility);
            await context.SaveChangesAsync();
            facilityId = facility.Id;

            var topic = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Facility,
                TargetId = facilityId,
                Title = "Facility Topic With Event",
                IsDeleted = false
            };
            context.Topics.Add(topic);
            await context.SaveChangesAsync();

            var ev = new TbActivityEvent
            {
                SubscriptionId = sub.Id,
                TopicId = topic.Id,
                TimeStamp = DateTime.UtcNow,
                Quantity = 100m,
                IsDeleted = false
            };
            context.Events.Add(ev);
            await context.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => facilityService.DeleteAsync(facilityId));
        Assert.Contains("Cannot delete facility", ex.Message);
        Assert.Contains("contains recorded carbon emission activity data", ex.Message);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var facility = await context.Facilities.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == facilityId);
            Assert.NotNull(facility);
            Assert.False(facility.IsDeleted);

            var topics = await context.Topics.IgnoreQueryFilters().Where(t => t.Level == eTopicLevel.Facility && t.TargetId == facilityId).ToListAsync();
            Assert.Single(topics);
            Assert.False(topics.First().IsDeleted);
        }
    }

    [Fact]
    public async Task ClearChildFacilitiesAsync_Guard_WorksBothPaths()
    {
        var dbName = Guid.NewGuid().ToString();
        var (dbFactory, authProvider) = CreateInMemoryContextFactory(dbName);
        var companyService = new CompanyService(dbFactory, authProvider, NullLogger<CompanyService>.Instance);

        int companyWithEventsId;
        int companyCleanId;

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var (sub, corp, comp1) = await SeedBaseHierarchyAsync(context, "Comp1");
            companyWithEventsId = comp1.Id;

            var fac1 = new TbFacility { CompanyId = comp1.Id, Title = "Facility 1 With Event" };
            var fac2 = new TbFacility { CompanyId = comp1.Id, Title = "Facility 2 Clean" };
            context.Facilities.AddRange(fac1, fac2);
            await context.SaveChangesAsync();

            var topic1 = new TbActivityTopic
            {
                SubscriptionId = sub.Id,
                Level = eTopicLevel.Facility,
                TargetId = fac1.Id,
                Title = "Topic 1",
                IsDeleted = false
            };
            context.Topics.Add(topic1);
            await context.SaveChangesAsync();

            var ev = new TbActivityEvent
            {
                SubscriptionId = sub.Id,
                TopicId = topic1.Id,
                TimeStamp = DateTime.UtcNow,
                Quantity = 50m,
                IsDeleted = false
            };
            context.Events.Add(ev);
            await context.SaveChangesAsync();

            // Clean Company setup
            var comp2 = new TbCompany { CorporateId = corp.Id, Title = "Comp 2 Clean" };
            context.Companies.Add(comp2);
            await context.SaveChangesAsync();
            companyCleanId = comp2.Id;

            var fac3 = new TbFacility { CompanyId = comp2.Id, Title = "Facility 3 Clean" };
            var fac4 = new TbFacility { CompanyId = comp2.Id, Title = "Facility 4 Clean" };
            context.Facilities.AddRange(fac3, fac4);
            await context.SaveChangesAsync();
        }

        // Path 1: Guard throws when facilities contain activity emission data
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => companyService.ClearChildFacilitiesAsync(companyWithEventsId));
        Assert.Contains("Cannot clear facilities", ex.Message);
        Assert.Contains("Facility 1 With Event", ex.Message);

        // Path 2: Guard passes and facilities are cleared when clean
        var clearedCount = await companyService.ClearChildFacilitiesAsync(companyCleanId);
        Assert.Equal(2, clearedCount);

        using (var context = await dbFactory.CreateDbContextAsync())
        {
            var facilities = await context.Facilities.IgnoreQueryFilters().Where(f => f.CompanyId == companyCleanId).ToListAsync();
            Assert.Equal(2, facilities.Count);
            Assert.All(facilities, f => Assert.True(f.IsDeleted));
        }
    }
}
