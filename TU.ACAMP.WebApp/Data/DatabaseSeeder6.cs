using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Organization;

namespace TU.ACAMP.WebApp.Data;

// [2026-09-19][Antigravity] สร้าง DatabaseSeeder6 สำหรับเตรียมข้อมูลองค์กรทดสอบการ Migrate จากระบบเก่า (ACAMP V16)
// องค์กรหลักคือ "บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)"
// โครงสร้างองค์กรประกอบด้วย:
//   - 1 Subscription: บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด
//   - 1 Corporate: บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM) (Code: CRP-001)
//   - 1 Company: บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (Code: COM-001)
//   - 6 Facilities: เดชอุดม (FAC-001), ศรีสะเกษ (FAC-002), นนทบุรี (FAC-003), ร้อยเอ็ด (FAC-004), สุพรรณบุรี (FAC-005), พระประแดง (FAC-006)
// หมายเหตุ: Seeder ชุดนี้ "ไม่มีการ Seed Topic ใดๆ" เพื่อให้เป็นสถานะว่างเปล่า 100% พร้อมทดลองนำเข้าข้อมูลจริงจาก V16
public static class DatabaseSeeder6
{
    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Starting DatabaseSeeder6 (BSCM - Bangsue Chia Meng Rice Mill, Clean Topics)...");
        try
        {
            // 1. Seed Identity (Roles, Users, UserRoles) และ Master Library Data (Units, Groups, Sources, Mappings)
            // [2026-09-23][Claude] Seed เฉพาะตารางที่ยังว่าง (!Any) ทีละตาราง — ตารางที่มีข้อมูลแล้วจะไม่ถูกแตะ/เขียนทับ
            //     (เดิม SeedEmissionUnitsAsync เขียนทับ Unit ตาม Id ทุกครั้งที่บูต เสี่ยงยึด Custom Unit ที่ migrate เข้ามา)
            await SeedIdentityAndLibraryIfEmptyAsync(context, logger);

            // 2. Seed Subscription สำหรับ บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด
            var sub = await SeedSubscriptionAsync(context, logger);
            if (sub == null)
            {
                logger.LogWarning("DatabaseSeeder6: No subscription found or created. Aborting organization seeding.");
                return;
            }

            // 3. Seed Subscription Users
            await SeedSubscriptionUsersAsync(context, sub.Id, logger);

            // 4. Seed Organization Structure (Corporate -> Company -> 6 Facilities)
            await SeedOrganizationStructureAsync(context, sub.Id, logger);

            // 5. เคลียร์ Topics และ Events เก่า (หากมีตกค้างจาก Seeder อื่น) เพื่อให้มีสถานะว่างเปล่า 100% ตามข้อกำหนด
            await EnsureCleanTopicsAsync(context, sub.Id, logger);

            // 6. Seed Default API Keys
            // [2026-09-23][Claude] Seed เฉพาะเมื่อตาราง ApiKeys ยังว่าง
            if (!await context.ApiKeys.IgnoreQueryFilters().AnyAsync())
            {
                await DatabaseSeeder2.SeedDefaultApiKeysAsync(context, logger);
            }
            else
            {
                logger.LogInformation("DatabaseSeeder6: ApiKeys table contains records. Skipped seeding.");
            }

            logger.LogInformation("DatabaseSeeder6 completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during DatabaseSeeder6 seeding.");
            throw;
        }
    }

    private static async Task<TbSubscription?> SeedSubscriptionAsync(ApplicationDbContext context, ILogger logger)
    {
        const string subTitle = "บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด";

        // [2026-09-21][Gemini] ปรับให้ Seed Data เฉพาะกรณีตาราง Subscriptions ว่างเปล่า
        if (!await context.Subscriptions.IgnoreQueryFilters().AnyAsync())
        {
            var sub = new TbSubscription
            {
                Id = 1,
                Title = subTitle,
                TaxId = "0105558000001",
                OwnerName = subTitle,
                OwnerEmail = "corp001@acamp-demo.com",
                OwnerPhone = "08-221-6234",
                ContactPerson = "ผู้ดูแลระบบ บางซื่อโรงสีไฟเจียเม้ง",
                BillingAddress = "เลขที่ 1 ปูนซิเมนต์ไทย บางซื่อ บางซื่อ กรุงเทพฯ 10800",
                SubscriptionPlan = eSubscriptionPlan.Pro,
                OwnerType = eSubscriptionOwnerType.Juristic,
                StartDate = DateTime.UtcNow.AddMonths(-12),
                ExpiryDate = DateTime.UtcNow.AddMonths(24),
                IsActive = true,
                IsDeleted = false,
                Remarks = "BSCM Tenant (Old System Migration Dataset - V16)"
            };
            sub.ApplyPlanQuotas(eSubscriptionPlan.Pro);
            await context.Subscriptions.AddAsync(sub);
            await context.SaveChangesAsync("System");
            logger.LogInformation("DatabaseSeeder6: Created new Subscription '{Title}' because Subscriptions table was empty.", sub.Title);
            return sub;
        }

        // หากมี Subscription อยู่แล้วในตาราง ให้ดึงมาใช้โดยไม่เขียนทับข้อมูลเดิม
        var existingSub = await context.Subscriptions
            .FirstOrDefaultAsync(s => s.Title.ToLower() == subTitle.ToLower() && !s.IsDeleted)
            ?? await context.Subscriptions.FirstOrDefaultAsync(s => !s.IsDeleted)
            ?? await context.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync();

        logger.LogInformation("DatabaseSeeder6: Subscriptions table is not empty. Using existing Subscription ID {Id}.", existingSub?.Id);
        return existingSub;
    }

    private static async Task SeedSubscriptionUsersAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        // [2026-09-21][Gemini] ปรับให้ Seed Data เฉพาะเมื่อตาราง SubscriptionUsers ว่างเปล่า
        if (await context.SubscriptionUsers.IgnoreQueryFilters().AnyAsync())
        {
            logger.LogInformation("DatabaseSeeder6: SubscriptionUsers table is not empty. Skipped seeding.");
            return;
        }

        var usersToMap = new[]
        {
            ("corp001@acamp-demo.com", eSubscriptionRole.Owner),
            ("patipath.eia@nectec.or.th", eSubscriptionRole.Admin)
        };

        foreach (var (email, role) in usersToMap)
        {
            await context.SubscriptionUsers.AddAsync(new TbSubscriptionUser
            {
                SubscriptionId = subId,
                Email = email,
                Role = role,
                IsActive = true
            });
            logger.LogInformation("DatabaseSeeder6: Mapped user '{Email}' to Subscription ID {SubId}.", email, subId);
        }

        await context.SaveChangesAsync("System");
    }

    private static async Task SeedOrganizationStructureAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        // 1. Corporate: บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)
        TbCorporate? corp;
        // [2026-09-21][Gemini] Seed เฉพาะเมื่อตาราง Corporates ว่างเปล่า
        if (!await context.Corporates.IgnoreQueryFilters().AnyAsync())
        {
            corp = new TbCorporate
            {
                SubscriptionId = subId,
                Code = "CRP-001",
                Title = "บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)",
                Remark = "องค์กรระดับรวมงบ (Corporate Level - บางซื่อโรงสีไฟเจียเม้ง)",
                MaterialityThreshold = 5.0m,
                IsActive = true,
                IsDeleted = false
            };
            corp.EnsureKeyIdGenerated();
            await context.Corporates.AddAsync(corp);
            await context.SaveChangesAsync("System");
            logger.LogInformation("DatabaseSeeder6: Created Corporate '{Title}' ({Code}) because Corporates table was empty.", corp.Title, corp.Code);
        }
        else
        {
            corp = await context.Corporates.FirstOrDefaultAsync(c => c.SubscriptionId == subId && !c.IsDeleted)
                   ?? await context.Corporates.FirstOrDefaultAsync(c => !c.IsDeleted)
                   ?? await context.Corporates.IgnoreQueryFilters().FirstOrDefaultAsync();
            logger.LogInformation("DatabaseSeeder6: Corporates table is not empty. Skipped Corporate seeding.");
        }

        if (corp == null)
        {
            logger.LogWarning("DatabaseSeeder6: No Corporate found. Aborting organization structure seeding.");
            return;
        }

        // 2. Company: บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด
        TbCompany? comp;
        // [2026-09-21][Gemini] Seed เฉพาะเมื่อตาราง Companies ว่างเปล่า
        if (!await context.Companies.IgnoreQueryFilters().AnyAsync())
        {
            comp = new TbCompany
            {
                CorporateId = corp.Id,
                Code = "COM-001",
                Title = "บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด",
                EquityShare = 100,
                Remark = "นิติบุคคลผู้รายงาน (Company Level - บางซื่อโรงสีไฟเจียเม้ง)",
                IsActive = true,
                IsDeleted = false
            };
            comp.EnsureKeyIdGenerated();
            await context.Companies.AddAsync(comp);
            await context.SaveChangesAsync("System");
            logger.LogInformation("DatabaseSeeder6: Created Company '{Title}' ({Code}) because Companies table was empty.", comp.Title, comp.Code);
        }
        else
        {
            comp = await context.Companies.FirstOrDefaultAsync(c => c.CorporateId == corp.Id && !c.IsDeleted)
                   ?? await context.Companies.FirstOrDefaultAsync(c => !c.IsDeleted)
                   ?? await context.Companies.IgnoreQueryFilters().FirstOrDefaultAsync();
            logger.LogInformation("DatabaseSeeder6: Companies table is not empty. Skipped Company seeding.");
        }

        if (comp == null)
        {
            logger.LogWarning("DatabaseSeeder6: No Company found. Aborting facility seeding.");
            return;
        }

        // 3. Facilities: 6 สาขา
        // [2026-09-21][Gemini] Seed เฉพาะเมื่อตาราง Facilities ว่างเปล่าเท่านั้น
        // หากตารางมีข้อมูลอยู่แล้ว (ไม่ว่าจะแก้ไขหรือลบแบบ Soft Delete) จะไม่สร้างข้อมูลซ้ำ และไม่เขียนทับค่าเดิม
        if (await context.Facilities.IgnoreQueryFilters().AnyAsync())
        {
            logger.LogInformation("DatabaseSeeder6: Facilities table is not empty. Skipped Facilities seeding.");
            return;
        }

        var facilityDefs = new List<(string Code, string Title, string Remark)>
        {
            ("00007", "สาขาอุบลราชธานี (เดชอุดม)", "โรงสีและคลังสินค้า อุบลราชธานี (เดชอุดม)"),
            ("00001", "สาขาศรีสะเกษ", "โรงสีและคลังสินค้า ศรีสะเกษ"),
            ("00003", "สาขานนทบุรี", "สำนักงานและคลังสินค้า นนทบุรี"),
            ("00005", "สาขาร้อยเอ็ด", "โรงสีและคลังสินค้า ร้อยเอ็ด"),
            ("00006", "สาขาสุพรรณบุรี", "โรงสีและคลังสินค้า สุพรรณบุรี")
           
        };

        foreach (var def in facilityDefs)
        {
            var fac = new TbFacility
            {
                CompanyId = comp.Id,
                Code = def.Code,
                Title = def.Title,
                Remark = def.Remark,
                IsActive = true,
                IsDeleted = false
            };
            fac.EnsureKeyIdGenerated();
            await context.Facilities.AddAsync(fac);
            logger.LogInformation("DatabaseSeeder6: Created Facility '{Title}' ({Code}).", fac.Title, fac.Code);
        }

        await context.SaveChangesAsync("System");
        logger.LogInformation("DatabaseSeeder6: Seeded {Count} facilities under Company ID {CompId}.", facilityDefs.Count, comp.Id);
    }

    /// <summary>
    /// [2026-09-23][Claude] Seed Identity และ Master Library ทีละตาราง เฉพาะตารางที่ยังไม่มีข้อมูล (!Any รวม Soft Delete)
    /// </summary>
    // [2026-09-24][Claude] private -> internal เพื่อให้ DatabaseSeeder7 เรียกใช้ซ้ำได้ (พฤติกรรมเดิมไม่เปลี่ยน)
    internal static async Task SeedIdentityAndLibraryIfEmptyAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-23]
        if (!await context.Roles.AnyAsync()) await DatabaseSeeder2.SeedRolesAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: Roles table contains records. Skipped seeding.");

        if (!await context.Users.AnyAsync()) await DatabaseSeeder2.SeedUsersAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: Users table contains records. Skipped seeding.");

        if (!await context.UserRoles.AnyAsync()) await DatabaseSeeder2.SeedUserRolesAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: UserRoles table contains records. Skipped seeding.");

        if (!await context.Units.IgnoreQueryFilters().AnyAsync()) await DatabaseSeeder2.SeedEmissionUnitsAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: Units table contains records. Skipped seeding.");

        if (!await context.EmissionGroups.IgnoreQueryFilters().AnyAsync()) await DatabaseSeeder2.SeedEmissionGroupsAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: EmissionGroups table contains records. Skipped seeding.");

        if (!await context.ActivityGroups.IgnoreQueryFilters().AnyAsync()) await DatabaseSeeder2.SeedActivityGroupsAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: ActivityGroups table contains records. Skipped seeding.");

        if (!await context.EmissionSources.IgnoreQueryFilters().AnyAsync()) await DatabaseSeeder2.SeedEmissionSourcesAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: EmissionSources table contains records. Skipped seeding.");

        if (!await context.EmissionMappings.IgnoreQueryFilters().AnyAsync()) await DatabaseSeeder2.SeedEmissionMappingsAsync(context, logger);
        else logger.LogInformation("DatabaseSeeder6: EmissionMappings table contains records. Skipped seeding.");
        #endregion
    }

    /// <summary>
    /// [2026-09-19][Antigravity] ตรวจสอบและเคลียร์ Topics / Events ตกค้างใน Subscription เพื่อให้มีสถานะเป็นองค์กรใหม่ที่ยังไม่มี Topic เริ่มต้น
    /// </summary>
    private static async Task EnsureCleanTopicsAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        // [2026-09-21][Gemini] หากมี Topics หรือ Events อยู่แล้ว ไม่ต้องเคลียร์ทิ้ง เพื่อป้องกันข้อมูลที่ผู้ใช้นำเข้าหรือสร้างไว้สูญหายเมื่อ restart
        var hasTopics = await context.Topics.IgnoreQueryFilters().AnyAsync(t => t.SubscriptionId == subId);
        if (hasTopics)
        {
            logger.LogInformation("DatabaseSeeder6: Topics table contains records for Subscription ID {SubId}. Skipped clearing topics.", subId);
            return;
        }

        logger.LogInformation("DatabaseSeeder6: Verified 0 topics exist (Clean slate ready for import).");
    }
}
