using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;

namespace TU.ACAMP.WebApp.Data;

// [2026-09-17][Gemini] สร้าง DatabaseSeeder4 สำหรับทดสอบการ Export CFO Report to Excel
// โดยนำเข้าข้อมูลพื้นฐานจาก DatabaseSeeder2 และสร้างหัวข้อกิจกรรมครอบคลุมทุกขอบเขต 1-4
// โดยเฉพาะขอบเขตที่ 3 (Scope 3) ประกอบด้วย Category 1, 5, 6 พร้อมบันทึก Monthly Events ปี 2024-2025
public static class DatabaseSeeder4
{
    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Starting DatabaseSeeder4 seeding process (CFO Report Export Test - Scopes 1-4)...");
        try
        {
            // 1. Seed ข้อมูลหลักและโครงสร้างองค์กรมาตรฐานจาก DatabaseSeeder2
            await DatabaseSeeder2.SeedAsync(context, logger);

            // 2. Seed หัวข้อกิจกรรม (Topics) และ Monthly Events (2024-2025) ครบทุก Scope 1-4 (Scope 3 มี Cat 1, 5, 6)
            await SeedCfoReportTopicsAndEventsAsync(context, logger);

            logger.LogInformation("DatabaseSeeder4 completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during DatabaseSeeder4 seeding.");
            throw;
        }
    }

    // [2026-09-17][Gemini] สร้างหัวข้อกิจกรรมและ Monthly Events (2024-2025) ครบทุกขอบเขต 1-4 (ขอบเขต 3 มี Cat 1, 5, 6) สำหรับทดสอบ Export CFO Report To Excel
    public static async Task SeedCfoReportTopicsAndEventsAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Seeding CFO Report Topics & Monthly Events (Scope 1, 2, 3 [Cat 1,5,6], 4)...");
        try
        {
            var facility = await context.Facilities.FirstOrDefaultAsync(f => f.Code == "FAC-001" && !f.IsDeleted)
                           ?? await context.Facilities.FirstOrDefaultAsync(f => !f.IsDeleted);

            if (facility == null)
            {
                logger.LogWarning("No active Facility found to seed CFO Report Topics.");
                return;
            }

            int subId = 1;
            var company = await context.Companies.Include(c => c.Corporate).FirstOrDefaultAsync(c => c.Id == facility.CompanyId);
            if (company?.Corporate != null && company.Corporate.SubscriptionId > 0)
            {
                subId = company.Corporate.SubscriptionId;
            }

            // 1. ค้นหา Activity Groups สำหรับแต่ละ Scope และ Category
            var ag0101 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && ag.Code == "0101" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope1 && !ag.IsDeleted);

            var ag0201 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && ag.Code == "0201" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope2 && !ag.IsDeleted);

            var ag0301 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0301" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted);

            var ag0305 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0305" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted);

            var ag0306 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0306" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted);

            var ag0401 = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && ag.Code == "0401" && !ag.IsDeleted)
                         ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted);

            // 2. ค้นหา Emission Sources
            // Scope 1: Diesel B7 (Id: 19 หรือ Id: 15)
            var srcScope1 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 19 && !s.IsDeleted)
                            ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 15 && !s.IsDeleted)
                            ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("Diesel") && !s.IsDeleted);

            // Scope 2: ไฟฟ้า Grid Mix (Id: 131 หรือ Id: 128)
            var srcScope2 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 131 && !s.IsDeleted)
                            ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 128 && !s.IsDeleted)
                            ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("ไฟฟ้าแบบ grid mix") && !s.IsDeleted);

            // Scope 3 Cat 1: กระดาษพิมพ์เขียน (Id: 643) หรือ น้ำประปา (Id: 189)
            var srcScope3Cat1 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 643 && !s.IsDeleted)
                               ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 189 && !s.IsDeleted)
                               ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("กระดาษ") && !s.IsDeleted);

            // Scope 3 Cat 5: การฝังกลบขยะมูลฝอย (Id: 626)
            var srcScope3Cat5 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 626 && !s.IsDeleted)
                               ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("ขยะมูลฝอย") && !s.IsDeleted);

            // Scope 3 Cat 6: น้ำมันก๊าดหรือน้ำมันเครื่องบิน (Id: 184)
            var srcScope3Cat6 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 184 && !s.IsDeleted)
                               ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("เครื่องบิน") && !s.IsDeleted);

            // Scope 4: Biogenic CO2 - Diesel B7 (Id: 21)
            var srcScope4 = await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Id == 21 && !s.IsDeleted)
                            ?? await context.EmissionSources.Include(s => s.Group).FirstOrDefaultAsync(s => s.Title.Contains("Biogenic CO2") && !s.IsDeleted);

            // [2026-09-17][Gemini] กำหนดโครงสร้างนิยาม Topic สำหรับ CFO Export
            var topicDefinitions = new[]
            {
                new
                {
                    Title = "การใช้น้ำมันดีเซลสำหรับเครื่องกำเนิดไฟฟ้าสำรอง (Emergency Generator Diesel)",
                    Scope = eTgoScopeNo.Scope1,
                    ActGroup = ag0101,
                    Source = srcScope1,
                    DefaultGroupId = 1,
                    Qty2024 = 200m,
                    Qty2025 = 250m,
                    Remark = "Scope 1 - Stationary Combustion (Diesel B7)",
                    ApplyMapping = true
                },
                new
                {
                    Title = "การใช้พลังงานไฟฟ้าจากโครงข่าย (Purchased Grid Electricity)",
                    Scope = eTgoScopeNo.Scope2,
                    ActGroup = ag0201,
                    Source = srcScope2,
                    DefaultGroupId = 7,
                    Qty2024 = 4000m,
                    Qty2025 = 4500m,
                    Remark = "Scope 2 - Purchased Electricity (Grid Mix)",
                    ApplyMapping = false
                },
                new
                {
                    Title = "การจัดซื้อกระดาษสำนักงานและวัสดุสิ้นเปลือง (Office Paper Consumables)",
                    Scope = eTgoScopeNo.Scope3,
                    ActGroup = ag0301,
                    Source = srcScope3Cat1,
                    DefaultGroupId = 30,
                    Qty2024 = 80m,
                    Qty2025 = 100m,
                    Remark = "Scope 3 Category 1 - Purchased Goods and Services",
                    ApplyMapping = false
                },
                new
                {
                    Title = "การกำจัดขยะมูลฝอยทั่วไปแบบฝังกลบ (Municipal Solid Waste Landfill)",
                    Scope = eTgoScopeNo.Scope3,
                    ActGroup = ag0305,
                    Source = srcScope3Cat5,
                    DefaultGroupId = 28,
                    Qty2024 = 150m,
                    Qty2025 = 180m,
                    Remark = "Scope 3 Category 5 - Waste Generated in Operations",
                    ApplyMapping = false
                },
                new
                {
                    Title = "การเดินทางเพื่อธุรกิจโดยสารการบิน (Commercial Business Flights)",
                    Scope = eTgoScopeNo.Scope3,
                    ActGroup = ag0306,
                    Source = srcScope3Cat6,
                    DefaultGroupId = 14,
                    Qty2024 = 60m,
                    Qty2025 = 75m,
                    Remark = "Scope 3 Category 6 - Business Travel",
                    ApplyMapping = false
                },
                new
                {
                    Title = "การปล่อยก๊าซ CO₂ ชีวภาพจากการใช้น้ำมันไบโอดีเซล B7 (Biogenic CO₂ - Diesel B7)",
                    Scope = eTgoScopeNo.SeparateReport,
                    ActGroup = ag0401,
                    Source = srcScope4,
                    DefaultGroupId = 8,
                    Qty2024 = 50m,
                    Qty2025 = 60m,
                    Remark = "Scope 4 - Separate Report (Biogenic CO2)",
                    ApplyMapping = false
                }
            };

            int orderId = 1;
            foreach (var def in topicDefinitions)
            {
                if (def.Source == null || def.ActGroup == null)
                {
                    logger.LogWarning("Skipping topic '{Title}' due to missing Source or ActivityGroup.", def.Title);
                    continue;
                }

                int egId = def.Source.GroupId > 0 ? def.Source.GroupId : def.DefaultGroupId;

                var topic = await context.Topics
                    .Include(t => t.Childs)
                    .FirstOrDefaultAsync(t => t.Level == eTopicLevel.Facility
                                           && t.TargetId == facility.Id
                                           && t.Title == def.Title
                                           && !t.IsDeleted);

                if (topic == null)
                {
                    topic = new TbActivityTopic
                    {
                        SubscriptionId = subId,
                        Level = eTopicLevel.Facility,
                        TargetId = facility.Id,
                        EmissionGroupId = egId,
                        Title = def.Title,
                        Remark = $"{def.Remark} - {facility.Title}",
                        OrderId = orderId++,
                        IsActive = true,
                        IsDeleted = false
                    };

                    await context.Topics.AddAsync(topic);
                    var entry = context.Entry(topic);
                    if (entry.Metadata.FindProperty("TbFacilityId") != null)
                    {
                        entry.Property("TbFacilityId").CurrentValue = facility.Id;
                    }
                    await context.SaveChangesAsync("System");

                    // ผูก Master Child
                    var masterChild = new TbActivityTopicChild
                    {
                        TopicId = topic.Id,
                        ActivityGroupId = def.ActGroup.Id,
                        SourceId = def.Source.Id,
                        ActivityUnitId = def.Source.UnitId > 0 ? def.Source.UnitId : null,
                        UnitConversionRatio = 1.0m,
                        EmissionSharePercent = 100.0f,
                        IsActive = true,
                        IsDeleted = false
                    };
                    await context.TopicChilds.AddAsync(masterChild);

                    // หากมี Mapping (เช่น Scope 1 Diesel B7) ให้ผูก Scope 3 WTT และ Biogenic CO2 อัตโนมัติตามกฎ Emission Mapping Architecture
                    if (def.ApplyMapping)
                    {
                        var mapping = await context.EmissionMappings
                            .FirstOrDefaultAsync(m => m.Scope1ChildId == def.Source.Id && !m.IsDeleted);

                        if (mapping != null)
                        {
                            if (mapping.Scope3ChildId > 0)
                            {
                                var s3Group = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && ag.Code == "0303" && !ag.IsDeleted)
                                              ?? await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.Scope3 && !ag.IsDeleted);

                                if (s3Group != null)
                                {
                                    await context.TopicChilds.AddAsync(new TbActivityTopicChild
                                    {
                                        TopicId = topic.Id,
                                        ActivityGroupId = s3Group.Id,
                                        SourceId = mapping.Scope3ChildId,
                                        UnitConversionRatio = mapping.UnitConversionRatio ?? 1.0m,
                                        EmissionSharePercent = 100.0f,
                                        IsActive = true,
                                        IsDeleted = false
                                    });
                                }
                            }

                            if (mapping.BiogenicChildId > 0)
                            {
                                var bioGroup = await context.ActivityGroups.FirstOrDefaultAsync(ag => ag.TgoScopeNo == eTgoScopeNo.SeparateReport && !ag.IsDeleted);
                                if (bioGroup != null)
                                {
                                    await context.TopicChilds.AddAsync(new TbActivityTopicChild
                                    {
                                        TopicId = topic.Id,
                                        ActivityGroupId = bioGroup.Id,
                                        SourceId = mapping.BiogenicChildId,
                                        UnitConversionRatio = 1.0m,
                                        EmissionSharePercent = 100.0f,
                                        IsActive = true,
                                        IsDeleted = false
                                    });
                                }
                            }
                        }
                    }

                    await context.SaveChangesAsync("System");
                    logger.LogInformation("Seeded Topic: '{Title}' (ID: {TopicId}) for Facility: '{FacTitle}'.", topic.Title, topic.Id, facility.Title);
                }

                // 3. สร้าง Monthly Events สำหรับปี 2024 (Base Year) และ 2025 (Calculate Year)
                int eventsCreated = 0;
                for (int y = 2024; y <= 2025; y++)
                {
                    decimal qty = (y == 2024) ? def.Qty2024 : def.Qty2025;

                    for (int m = 1; m <= 12; m++)
                    {
                        var eventDate = new DateTime(y, m, 15, 0, 0, 0, DateTimeKind.Utc);
                        var monthStart = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Utc);
                        var monthEnd = monthStart.AddMonths(1);

                        var exists = await context.Events.AnyAsync(e =>
                            e.TopicId == topic.Id &&
                            !e.IsDeleted &&
                            e.TimeStamp >= monthStart &&
                            e.TimeStamp < monthEnd);

                        if (!exists)
                        {
                            var evt = new TbActivityEvent
                            {
                                TopicId = topic.Id,
                                SubscriptionId = subId,
                                Quantity = qty,
                                TimeStamp = eventDate,
                                Remark = $"Monthly Activity Event {y}/{m:D2} (Qty: {qty:N0})",
                                IsActive = true,
                                IsDeleted = false
                            };
                            await context.Events.AddAsync(evt);
                            eventsCreated++;
                        }
                    }
                }

                if (eventsCreated > 0)
                {
                    await context.SaveChangesAsync("System");
                    logger.LogInformation("Seeded {Count} monthly activity events for Topic ID {TopicId}.", eventsCreated, topic.Id);
                }
            }

            logger.LogInformation("Successfully completed seeding all Scopes 1-4 topics and monthly events.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding CFO Report Topics and Events in DatabaseSeeder4.");
            throw;
        }
    }
}
