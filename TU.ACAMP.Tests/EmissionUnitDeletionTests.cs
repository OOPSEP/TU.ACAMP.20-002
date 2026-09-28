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

public class EmissionUnitDeletionTests
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

    private EmissionUnitService CreateService(IDbContextFactory<ApplicationDbContext> dbFactory, AuthenticationStateProvider authProvider)
    {
        return new EmissionUnitService(dbFactory, authProvider, NullLogger<EmissionUnitService>.Instance);
    }

    [Fact]
    public async Task GetUsingSourceTitlesAsync_WhenUnitUsedByActiveSources_ReturnsDistinctSourceTitles()
    {
        // [2026-09-20][Antigravity] ทดสอบดึงรายชื่อ Emission Source ที่กำลังใช้งาน Unit
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var unit = new TbEmissionUnit
        {
            Title = "kg",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var sourceA = new TbEmissionSource { Title = "Diesel", UnitId = unit.Id, SourceType = eSourceType.Custom, IsActive = true };
        var sourceB = new TbEmissionSource { Title = "Gasoline", UnitId = unit.Id, SourceType = eSourceType.Custom, IsActive = true };
        var sourceDeleted = new TbEmissionSource { Title = "LPG", UnitId = unit.Id, SourceType = eSourceType.Custom, IsActive = true };
        context.EmissionSources.AddRange(sourceA, sourceB, sourceDeleted);
        await context.SaveChangesAsync();

        // Soft delete sourceDeleted
        sourceDeleted.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        var usingSources = await service.GetUsingSourceTitlesAsync(unit.Id);

        Assert.Equal(2, usingSources.Count);
        Assert.Contains("Diesel", usingSources);
        Assert.Contains("Gasoline", usingSources);
        Assert.DoesNotContain("LPG", usingSources);
    }

    [Fact]
    public async Task DeleteAsync_WhenUnitInUseBySource_ThrowsInvalidOperationException()
    {
        // [2026-09-20][Antigravity] ทดสอบว่า DeleteAsync ปฏิเสธการลบและโยน Exception หากมี Source อ้างอิงใช้งานอยู่
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var unit = new TbEmissionUnit
        {
            Title = "kWh",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var source = new TbEmissionSource
        {
            Title = "Grid Electricity",
            UnitId = unit.Id,
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(unit.Id));
        // [2026-09-24] ข้อความรูปแบบใหม่ของ DeleteGuard: แสดงชื่อต้นทางแรก และไม่มี "and others" เมื่อมีรายการเดียว
        Assert.Contains("currently used by emission source 'Grid Electricity'", ex.Message);
        Assert.DoesNotContain("and others", ex.Message);

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var reloadedUnit = await verifyContext.Units.FindAsync(unit.Id);
        Assert.NotNull(reloadedUnit);
        Assert.False(reloadedUnit.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenUnitNotInUse_SoftDeletesSuccessfully()
    {
        // [2026-09-20][Antigravity] ทดสอบว่า DeleteAsync อนุญาตให้ลบได้สำเร็จเมื่อไม่มี Source ใช้งาน
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var unit = new TbEmissionUnit
        {
            Title = "Unused Unit",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(unit.Id);
        Assert.True(result);

        // ยืนยันว่า unit ถูก soft delete จริง
        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var unitAfter = await verifyContext.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == unit.Id);
        Assert.NotNull(unitAfter);
        Assert.True(unitAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceIsDeleted_SoftDeletesSuccessfully()
    {
        // [2026-09-20][Antigravity] ทดสอบว่าเมื่อ Source ที่เคยใช้ Unit ถูกลบไปแล้ว สามารถลบ Unit ได้
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var unit = new TbEmissionUnit
        {
            Title = "Old Unit",
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var source = new TbEmissionSource
        {
            Title = "Deleted Source",
            UnitId = unit.Id,
            SourceType = eSourceType.Custom,
            IsActive = true
        };
        context.EmissionSources.Add(source);
        await context.SaveChangesAsync();

        // Soft delete the source
        source.SoftDelete("tester@acamp.com");
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(unit.Id);
        Assert.True(result);

        // ยืนยันว่า unit ถูก soft delete จริง
        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var unitAfter = await verifyContext.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == unit.Id);
        Assert.NotNull(unitAfter);
        Assert.True(unitAfter.IsDeleted);
    }

    [Fact]
    public async Task DeleteAsync_WhenUnitIsDefaultMaster_ReturnsFalseWithoutDeleting()
    {
        // [2026-09-20][Antigravity] ทดสอบว่า Unit ที่เป็น System Default ห้ามลบและคืนค่า false
        var (dbFactory, context, authProvider) = CreateInMemoryContext(Guid.NewGuid().ToString());
        var service = CreateService(dbFactory, authProvider);

        var unit = new TbEmissionUnit
        {
            Title = "System kg",
            SourceType = eSourceType.Default,
            IsActive = true
        };
        context.Units.Add(unit);
        await context.SaveChangesAsync();

        var result = await service.DeleteAsync(unit.Id);
        Assert.False(result);

        using var verifyContext = await dbFactory.CreateDbContextAsync();
        var unitAfter = await verifyContext.Units.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == unit.Id);
        Assert.NotNull(unitAfter);
        Assert.False(unitAfter.IsDeleted);
    }
}
