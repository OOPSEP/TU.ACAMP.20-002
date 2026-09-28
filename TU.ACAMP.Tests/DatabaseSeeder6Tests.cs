using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TU.ACAMP.WebApp.Data;
using Xunit;

namespace TU.ACAMP.Tests;

// [2026-09-19][Antigravity] ชุดทดสอบการทำงานของ DatabaseSeeder6 (บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด - Clean Topics)
public class DatabaseSeeder6Tests
{
    private ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_CreatesBscmOrganizationWithSixFacilitiesAndZeroTopics()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        var logger = NullLogger.Instance;

        // รัน Seeder 6
        await DatabaseSeeder6.SeedAsync(context, logger);

        // 1. ตรวจสอบ Subscription
        var sub = await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted);
        Assert.NotNull(sub);
        Assert.Equal("บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด", sub.Title);

        // 2. ตรวจสอบ Corporate
        var corp = await context.Corporates.FirstOrDefaultAsync(c => c.SubscriptionId == sub.Id && !c.IsDeleted);
        Assert.NotNull(corp);
        Assert.Equal("บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)", corp.Title);
        Assert.Equal("CRP-001", corp.Code);

        // 3. ตรวจสอบ Company
        var comp = await context.Companies.FirstOrDefaultAsync(c => c.CorporateId == corp.Id && !c.IsDeleted);
        Assert.NotNull(comp);
        Assert.Equal("บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด", comp.Title);
        Assert.Equal("COM-001", comp.Code);

        // 4. ตรวจสอบ Facilities ทั้ง 6 สาขา
        var facilities = await context.Facilities.Where(f => f.CompanyId == comp.Id && !f.IsDeleted).ToListAsync();
        Assert.Equal(6, facilities.Count);

        var facilityTitles = facilities.Select(f => f.Title).ToList();
        Assert.Contains("สาขาอุบลราชธานี (เดชอุดม)", facilityTitles);
        Assert.Contains("สาขาศรีสะเกษ", facilityTitles);
        Assert.Contains("สาขานนทบุรี", facilityTitles);
        Assert.Contains("สาขาร้อยเอ็ด", facilityTitles);
        Assert.Contains("สาขาสุพรรณบุรี", facilityTitles);
        Assert.Contains("สาขาพระประแดง", facilityTitles);

        // 5. ข้อกำหนดสำคัญ: ต้อง "ยังไม่ต้องมี topic อะไรเลย" (0 Topics & 0 Events)
        var activeTopics = await context.Topics.Where(t => t.SubscriptionId == sub.Id && !t.IsDeleted).ToListAsync();
        Assert.Empty(activeTopics);

        var activeEvents = await context.Events.Where(e => !e.IsDeleted).ToListAsync();
        Assert.Empty(activeEvents);
    }

    // [2026-09-21][Gemini] ทดสอบว่าเมื่อแก้ไขหรือลบ Facility แล้วเรียก Seed ซ้ำ ข้อมูลที่ลบจะไม่ฟื้นกลับมา และข้อมูลที่แก้จะไม่ถูกเขียนทับ
    [Fact]
    public async Task SeedAsync_WhenFacilityDeletedOrModified_DoesNotRecreateOrOverwriteOnRestart()
    {
        var dbName = Guid.NewGuid().ToString();
        using var context = CreateInMemoryContext(dbName);
        var logger = NullLogger.Instance;

        // รัน Seeder 6 ครั้งแรก
        await DatabaseSeeder6.SeedAsync(context, logger);

        var facilities = await context.Facilities.ToListAsync();
        Assert.Equal(6, facilities.Count);

        // จำลองการลบ (Soft Delete) สาขาพระประแดง
        var facToDelete = facilities.First(f => f.Title == "สาขาพระประแดง");
        facToDelete.IsDeleted = true;

        // จำลองการแก้ไขชื่อสาขาอุบลราชธานี
        var facToEdit = facilities.First(f => f.Title == "สาขาอุบลราชธานี (เดชอุดม)");
        facToEdit.Title = "โรงงานหลักอุบลราชธานี (Customized)";

        await context.SaveChangesAsync();

        // จำลองการ Restart App โดยเรียก DatabaseSeeder6.SeedAsync ซ้ำ
        await DatabaseSeeder6.SeedAsync(context, logger);

        // ตรวจสอบ active facilities หลัง re-seed: ต้องมี 5 สาขา (พระประแดงต้องไม่กลับมา)
        var activeFacilitiesAfter = await context.Facilities.Where(f => !f.IsDeleted).ToListAsync();
        Assert.Equal(5, activeFacilitiesAfter.Count);
        Assert.DoesNotContain(activeFacilitiesAfter, f => f.Title == "สาขาพระประแดง");

        // ตรวจสอบชื่อสาขาที่แก้ไข: ต้องไม่ถูกเขียนทับกลับเป็นชื่อเดิม
        var editedFacAfter = activeFacilitiesAfter.First(f => f.Code == facToEdit.Code);
        Assert.Equal("โรงงานหลักอุบลราชธานี (Customized)", editedFacAfter.Title);
    }
}
