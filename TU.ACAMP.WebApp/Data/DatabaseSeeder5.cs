using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.WebApp.Data;

// [2026-09-18][Claude] ชุดข้อมูลทดสอบสำหรับ "การออกรายงาน CFO (TCFO_R_01 v07)" โดยเฉพาะ
//
// ต่างจาก DatabaseSeeder4 ที่เป็น happy path (6 หัวข้อ 2 ปี ปริมาณคงที่ทุกเดือน) — ชุดนี้ออกแบบให้
// เดินผ่านเส้นทางที่เคยหลุดการทดสอบมาแล้วทั้งหมด:
//
//   1. ข้อมูล 3 ปี (2023 / 2024 / 2025) ปริมาณต่างกันทุกปีและผันตามเดือน
//      -> ทดสอบ dropdown ปีฐาน (ต้องมี 2023, 2024 ให้เลือกเมื่อคำนวณปี 2025)
//   2. หัวข้อที่เริ่มมีข้อมูลปี 2025 เท่านั้น  -> คอลัมน์ AS ของ Fr-04.2 ต้องสรุปว่าเป็น "รายการที่มีเฉพาะปีรายงาน"
//   3. หัวข้อที่หยุดหลังปี 2024             -> คอลัมน์ AS ต้องขึ้น "ไม่มีรายการนี้ในปีรายงาน"
//   4. Scope 3 ครบ 9 หมวด (0301..0309)     -> เกินโควตา 7 แถวของฟอร์ม ทดสอบการยุบรวมแถวส่วนเกิน
//                                             และทำให้ Fr-03.2 มีทั้งหมวดที่มีนัยสำคัญและไม่มีนัยสำคัญ
//   5. Scope 1 จำนวน 16 รายการ             -> เกินโควตา 14 แถว ทดสอบการยุบรวมของ Scope 1
//   6. UnitConversionRatio != 1 และ EmissionSharePercent < 100 -> ทดสอบสูตรคำนวณและการแสดงผลใน Fr-04.1
//   7. สถานประกอบการที่ 2 มีข้อมูล และที่ 3 ไม่มีข้อมูลเลย      -> ทดสอบตัวกรอง In-Scope Organizational Boundary
//
// หมายเหตุที่ต้องรู้ก่อนใช้ชุดนี้:
//   - ค่า EF อ่านจาก TbEmissionSource.EF ณ เวลาที่สร้างรายงาน (CfoReportService บรรทัด ~927) ไม่ได้เก็บ
//     ประวัติ EF รายปี ดังนั้นปีฐานและปีรายงานจะใช้ EF ชุดเดียวกันเสมอ คอลัมน์ AS จึงแสดง "EF เท่าเดิม"
//     ความแตกต่างที่ทดสอบได้จริงคือ "ปริมาณ" และ "รายการเข้า/ออก"
//   - ผลผลิต (Production) ที่ใช้คำนวณ Carbon intensity ใน Fr-05 ไม่ได้อยู่ในฐานข้อมูล ผู้ทดสอบต้องกรอก
//     ในหน้า Wizard (Fr-01) เอง มิฉะนั้น D17/D18/D33/D34 จะเป็น 0 ตามที่ระบบกันหารศูนย์ไว้
//
// วิธีใช้: ที่ Services/Common/DependencyInjection.cs เปลี่ยนการเรียกเป็น DatabaseSeeder5.SeedAsync(context, logger);
public static class DatabaseSeeder5
{
    private const int FirstYear = 2023;
    private const int LastYear = 2025;

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        logger.LogInformation("Starting DatabaseSeeder5 (CFO report feature-test dataset, {First}-{Last})...", FirstYear, LastYear);
        try
        {
            // ใช้ข้อมูลหลักและโครงสร้างองค์กรชุดเดียวกับ Seeder2/Seeder4
            await DatabaseSeeder2.SeedAsync(context, logger);
            await SeedReportTestScenarioAsync(context, logger);

            logger.LogInformation("DatabaseSeeder5 completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during DatabaseSeeder5 seeding.");
            throw;
        }
    }

    /// <summary>นิยามหัวข้อกิจกรรมหนึ่งรายการของชุดทดสอบ</summary>
    private sealed class TopicPlan
    {
        public string Title { get; init; } = string.Empty;
        public string Remark { get; init; } = string.Empty;
        public eTgoScopeNo Scope { get; init; }
        public string ActivityGroupCode { get; init; } = string.Empty;
        public int FacilitySlot { get; init; } = 1;          // 1 = สถานประกอบการหลัก, 2 = สถานประกอบการที่สอง (คนละบริษัท)
        public decimal BaseQuantity { get; init; } = 100m;   // ปริมาณต่อเดือนของปีแรก
        public decimal YearGrowth { get; init; } = 0.10m;    // อัตราเพิ่มต่อปี (0.10 = +10% ต่อปี)
        public int FirstYear { get; init; } = DatabaseSeeder5.FirstYear;
        public int LastYear { get; init; } = DatabaseSeeder5.LastYear;
        public decimal Ratio { get; init; } = 1.0m;          // UnitConversionRatio
        public float SharePercent { get; init; } = 100f;     // EmissionSharePercent
    }

    private static async Task SeedReportTestScenarioAsync(ApplicationDbContext context, ILogger logger)
    {
        // ---------- 1) หาสถานประกอบการ 3 แห่ง (ที่ 3 ตั้งใจให้ไม่มีข้อมูล) ----------
        var facilities = await context.Facilities
            .Where(f => !f.IsDeleted)
            .OrderBy(f => f.Id)
            .Take(3)
            .ToListAsync();

        if (facilities.Count == 0)
        {
            logger.LogWarning("DatabaseSeeder5: ไม่พบสถานประกอบการที่ใช้งานได้ ข้ามการ seed");
            return;
        }

        var primaryFacility = facilities[0];
        var secondaryFacility = facilities.Count > 1 ? facilities[1] : facilities[0];

        int subId = 1;
        var company = await context.Companies.Include(c => c.Corporate)
            .FirstOrDefaultAsync(c => c.Id == primaryFacility.CompanyId);
        if (company?.Corporate != null && company.Corporate.SubscriptionId > 0)
        {
            subId = company.Corporate.SubscriptionId;
        }

        // ---------- 2) เตรียมแผนหัวข้อทั้งหมด ----------
        var plans = new List<TopicPlan>();

        // Scope 1 — 16 รายการ (โควตาฟอร์ม 14 แถว จึงต้องมีการยุบรวม 3 รายการสุดท้าย)
        for (int i = 1; i <= 16; i++)
        {
            plans.Add(new TopicPlan
            {
                Title = $"[S1-{i:D2}] การเผาไหม้อยู่กับที่ หน่วยผลิตที่ {i} (Stationary Combustion Unit {i})",
                Remark = "Scope 1 - Stationary Combustion (ชุดทดสอบโควตาแถว Fr-04.1)",
                Scope = eTgoScopeNo.Scope1,
                ActivityGroupCode = "0101",
                BaseQuantity = 120m + (i * 15m),
                YearGrowth = i % 3 == 0 ? -0.08m : 0.12m,     // มีทั้งเพิ่มขึ้นและลดลง เพื่อให้คอลัมน์ AS มีทั้ง + และ -
                Ratio = i == 5 ? 0.84m : 1.0m,                // ratio != 1 หนึ่งรายการ (litre -> kg)
                SharePercent = i == 7 ? 60f : 100f            // share < 100 หนึ่งรายการ (ถือหุ้น 60%)
            });
        }

        // Scope 2 — 2 รายการ
        plans.Add(new TopicPlan
        {
            Title = "[S2-01] การใช้พลังงานไฟฟ้าจากโครงข่าย (Purchased Grid Electricity)",
            Remark = "Scope 2 - Purchased Electricity",
            Scope = eTgoScopeNo.Scope2,
            ActivityGroupCode = "0201",
            BaseQuantity = 9000m,
            YearGrowth = 0.06m
        });
        plans.Add(new TopicPlan
        {
            Title = "[S2-02] การใช้ไฟฟ้าของสำนักงานสาขา (Branch Office Electricity)",
            Remark = "Scope 2 - Purchased Electricity (สถานประกอบการที่สอง สำหรับทดสอบ In-Scope)",
            Scope = eTgoScopeNo.Scope2,
            ActivityGroupCode = "0201",
            FacilitySlot = 2,
            BaseQuantity = 2500m,
            YearGrowth = 0.04m
        });

        // Scope 3 — 9 หมวด (0301..0309) โควตาฟอร์ม 7 แถว จึงต้องมีการยุบรวม
        var scope3Plan = new (string Code, string Title, decimal Qty, decimal Growth)[]
        {
            ("0301", "การจัดซื้อวัตถุดิบและวัสดุสิ้นเปลือง (Purchased Goods and Services)", 900m, 0.15m),
            ("0302", "การจัดซื้อสินทรัพย์ถาวร (Capital Goods)",                              120m, 0.30m),
            ("0303", "กิจกรรมที่เกี่ยวข้องกับเชื้อเพลิงและพลังงาน (Fuel- and Energy-Related)", 700m, 0.05m),
            ("0304", "การขนส่งและกระจายสินค้าต้นน้ำ (Upstream Transportation)",              450m, -0.10m),
            ("0305", "การกำจัดของเสียจากการดำเนินงาน (Waste Generated in Operations)",       300m, 0.08m),
            ("0306", "การเดินทางเพื่อธุรกิจ (Business Travel)",                              160m, 0.25m),
            ("0307", "การเดินทางไป-กลับของพนักงาน (Employee Commuting)",                     240m, 0.03m),
            ("0308", "สินทรัพย์ที่เช่าต้นน้ำ (Upstream Leased Assets)",                        80m, 0.00m),
            ("0309", "การขนส่งและกระจายสินค้าปลายน้ำ (Downstream Transportation)",           110m, 0.18m),
        };

        foreach (var (code, title, qty, growth) in scope3Plan)
        {
            plans.Add(new TopicPlan
            {
                Title = $"[S3-{code}] {title}",
                Remark = $"Scope 3 Category {code.Substring(2)} (ชุดทดสอบ Fr-03.2 และโควตาแถว Fr-04.1)",
                Scope = eTgoScopeNo.Scope3,
                ActivityGroupCode = code,
                BaseQuantity = qty,
                YearGrowth = growth
            });
        }

        // รายงานแยก (Biogenic)
        plans.Add(new TopicPlan
        {
            Title = "[S4-01] การปล่อย CO₂ ชีวภาพจากไบโอดีเซล B7 (Biogenic CO₂)",
            Remark = "Scope 4 - Separate Report (Biogenic CO2)",
            Scope = eTgoScopeNo.SeparateReport,
            ActivityGroupCode = "0401",
            BaseQuantity = 210m,
            YearGrowth = 0.10m
        });

        // เคสสำคัญ: รายการที่เพิ่งเริ่มในปีรายงาน และรายการที่เลิกไปหลังปี 2024
        plans.Add(new TopicPlan
        {
            Title = "[NEW-2025] การติดตั้งระบบทำความเย็นใหม่ (Refrigerant Top-up, เริ่มปี 2025)",
            Remark = "เคสทดสอบ: รายการที่มีเฉพาะปีรายงาน — คอลัมน์ AS ของ Fr-04.2 ต้องรายงานเป็นรายการใหม่",
            Scope = eTgoScopeNo.Scope1,
            ActivityGroupCode = "0102",
            BaseQuantity = 45m,
            YearGrowth = 0m,
            FirstYear = 2025,
            LastYear = 2025
        });
        plans.Add(new TopicPlan
        {
            Title = "[RETIRED-2024] การใช้น้ำมันเตาของหม้อไอน้ำเดิม (ยกเลิกปลายปี 2024)",
            Remark = "เคสทดสอบ: รายการที่หายไปในปีรายงาน — คอลัมน์ AS ของ Fr-04.2 ต้องขึ้นว่าไม่มีในปีรายงาน",
            Scope = eTgoScopeNo.Scope1,
            ActivityGroupCode = "0101",
            BaseQuantity = 380m,
            YearGrowth = -0.05m,
            FirstYear = 2023,
            LastYear = 2024
        });

        // ---------- 3) เตรียมคลัง EmissionSource แยกตามขอบเขต แล้วหมุนใช้ไม่ให้ทุกแถวเป็น EF ตัวเดียวกัน ----------
        var sourcePool = new Dictionary<eTgoScopeNo, List<TbEmissionSource>>();
        foreach (var scope in new[] { eTgoScopeNo.Scope1, eTgoScopeNo.Scope2, eTgoScopeNo.Scope3, eTgoScopeNo.SeparateReport })
        {
            // [2026-09-18][Claude] TbEmissionSource.EF เป็น [NotMapped] (คำนวณจาก CO2 x GWP ในหน่วยความจำ)
            // จึงกรอง EF > 0 ฝั่ง client ไม่ได้ใส่ใน LINQ ที่แปลเป็น SQL — เคยทำให้ seed ล้มทั้งชุด
            // [2026-09-18][Claude] ใช้เฉพาะ EF ที่ยังไม่หมดอายุ ณ วันที่ seed
            // (TbEmissionSource.IsExpire เป็น [NotMapped] จึงกรองด้วย DateExpire ซึ่ง map จริง)
            // มิฉะนั้นชุดทดสอบจะติด pre-flight เรื่อง EF หมดอายุ และบล็อกการออกไฟล์ทุกครั้ง
            var today = AppTimeZone.LocalNow.Date;

            var scopeSources = await context.EmissionSources
                .Include(s => s.Group)
                .Where(s => !s.IsDeleted
                         && s.Group != null
                         && s.Group.TgoScopeNo == scope
                         && (s.DateExpire == null || s.DateExpire >= today))
                .OrderBy(s => s.Id)
                .Take(120)
                .ToListAsync();

            sourcePool[scope] = scopeSources
                .Where(s => s.EF > 0m && !s.IsExpire)
                .Take(30)
                .ToList();

            if (sourcePool[scope].Count == 0)
            {
                logger.LogWarning("DatabaseSeeder5: ไม่พบค่า EF ที่ยังไม่หมดอายุของ {Scope} — หัวข้อในขอบเขตนี้จะถูกข้าม", scope);
            }
        }

        var sourceCursor = new Dictionary<eTgoScopeNo, int>();

        // ---------- 4) สร้าง Topic + Child + Events ----------
        int orderId = 1;
        int createdTopics = 0, createdEvents = 0, skipped = 0;

        foreach (var plan in plans)
        {
            var activityGroup = await context.ActivityGroups
                .FirstOrDefaultAsync(ag => ag.Code == plan.ActivityGroupCode && !ag.IsDeleted);

            if (activityGroup == null)
            {
                activityGroup = await context.ActivityGroups
                    .FirstOrDefaultAsync(ag => ag.TgoScopeNo == plan.Scope && !ag.IsDeleted);
            }

            // หมุนเลือก EF ในขอบเขตเดียวกันไปเรื่อย ๆ เพื่อให้แต่ละแถวมี EF/หน่วย/แหล่งอ้างอิงต่างกันจริง
            TbEmissionSource? source = null;
            if (sourcePool.TryGetValue(plan.Scope, out var pool) && pool.Count > 0)
            {
                int cursor = sourceCursor.TryGetValue(plan.Scope, out var c) ? c : 0;
                source = pool[cursor % pool.Count];
                sourceCursor[plan.Scope] = cursor + 1;
            }

            if (activityGroup == null || source == null)
            {
                logger.LogWarning("DatabaseSeeder5: ข้าม '{Title}' เพราะไม่พบ ActivityGroup ({Code}) หรือ EmissionSource ของ {Scope}",
                    plan.Title, plan.ActivityGroupCode, plan.Scope);
                skipped++;
                continue;
            }

            var facility = plan.FacilitySlot == 2 ? secondaryFacility : primaryFacility;

            var topic = await context.Topics
                .Include(t => t.Childs)
                .FirstOrDefaultAsync(t => t.Level == eTopicLevel.Facility
                                       && t.TargetId == facility.Id
                                       && t.Title == plan.Title
                                       && !t.IsDeleted);

            if (topic == null)
            {
                topic = new TbActivityTopic
                {
                    SubscriptionId = subId,
                    Level = eTopicLevel.Facility,
                    TargetId = facility.Id,
                    EmissionGroupId = source.GroupId > 0 ? source.GroupId : 1,
                    Title = plan.Title,
                    Remark = $"{plan.Remark} - {facility.Title}",
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

                await context.TopicChilds.AddAsync(new TbActivityTopicChild
                {
                    TopicId = topic.Id,
                    ActivityGroupId = activityGroup.Id,
                    SourceId = source.Id,
                    ActivityUnitId = source.UnitId > 0 ? source.UnitId : null,
                    UnitConversionRatio = plan.Ratio,
                    EmissionSharePercent = plan.SharePercent,
                    IsActive = true,
                    IsDeleted = false
                });

                await context.SaveChangesAsync("System");
                createdTopics++;
            }

            createdEvents += await SeedMonthlyEventsAsync(context, topic, subId, plan);
        }

        await context.SaveChangesAsync("System");

        logger.LogInformation(
            "DatabaseSeeder5: สร้างหัวข้อใหม่ {Topics} รายการ, เหตุการณ์รายเดือน {Events} รายการ, ข้าม {Skipped} รายการ " +
            "(สถานประกอบการหลัก: {Primary} / รอง: {Secondary})",
            createdTopics, createdEvents, skipped, primaryFacility.Title, secondaryFacility.Title);
    }

    /// <summary>
    /// สร้างเหตุการณ์รายเดือนตามแผน — ปริมาณต่างกันทุกปีตาม YearGrowth และผันตามเดือน (ฤดูกาล ±12%)
    /// เพื่อให้รายงานแต่ละปีไม่เท่ากันจริง ไม่ใช่ตัวเลขคงที่แบบ Seeder4
    /// </summary>
    private static async Task<int> SeedMonthlyEventsAsync(
        ApplicationDbContext context,
        TbActivityTopic topic,
        int subId,
        TopicPlan plan)
    {
        int created = 0;

        for (int year = plan.FirstYear; year <= plan.LastYear; year++)
        {
            int yearOffset = year - plan.FirstYear;
            decimal yearFactor = 1m;
            for (int i = 0; i < yearOffset; i++) yearFactor *= (1m + plan.YearGrowth);

            for (int month = 1; month <= 12; month++)
            {
                // ฤดูกาล: เดือน 3-5 สูงกว่าเฉลี่ย 12%, เดือน 9-11 ต่ำกว่าเฉลี่ย 8%
                decimal seasonFactor = month is >= 3 and <= 5 ? 1.12m
                                     : month is >= 9 and <= 11 ? 0.92m
                                     : 1.00m;

                decimal qty = Math.Round(plan.BaseQuantity * yearFactor * seasonFactor, 2, MidpointRounding.AwayFromZero);
                if (qty <= 0m) continue;

                var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                var monthEnd = monthStart.AddMonths(1);

                bool exists = await context.Events.AnyAsync(e =>
                    e.TopicId == topic.Id &&
                    !e.IsDeleted &&
                    e.TimeStamp >= monthStart &&
                    e.TimeStamp < monthEnd);

                if (exists) continue;

                await context.Events.AddAsync(new TbActivityEvent
                {
                    TopicId = topic.Id,
                    SubscriptionId = subId,
                    Quantity = qty,
                    TimeStamp = new DateTime(year, month, 15, 0, 0, 0, DateTimeKind.Utc),
                    Remark = $"Seeder5 monthly event {year}/{month:D2} (qty {qty:N2})",
                    IsActive = true,
                    IsDeleted = false
                });
                created++;
            }
        }

        if (created > 0) await context.SaveChangesAsync("System");
        return created;
    }
}
