using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;

namespace TU.ACAMP.WebApp.Data;

// [2026-09-24][Claude] DatabaseSeeder7 = ชุดข้อมูลจำลอง (Synthetic Demo Tenant) สำหรับทดสอบระบบแบบครบวงจร
//
// โครงสร้าง: 1 Subscription -> 1 Corporate -> 1 Company -> 2 Facilities (ข้อมูลสมมติทั้งหมด ไม่ใช่องค์กรจริง)
//   - D7-FAC-001 โรงงานผลิตอาหารแปรรูป นครปฐม   (ไฟฟ้า กฟภ. / ประปา กปภ.)
//   - D7-FAC-002 ศูนย์กระจายสินค้า บางนา         (ไฟฟ้า กฟน. / ประปา กปน.)
// หัวข้อกิจกรรมมีครบทั้ง 3 ระดับ: Corporate (สำนักงานใหญ่), Company (กิจกรรมทั้งบริษัท เช่น รถทีมขาย ขนส่งสินค้า),
//   และ Facility ทั้ง 2 แห่ง — ทุกระดับ/ทุกเป้าหมายครบ 4 ขอบเขต
//
// ข้อมูลกิจกรรม: ม.ค. 2023 ถึงเดือนล่าสุดที่ปิดงวดแล้ว (เดือนก่อนหน้าเดือนปัจจุบันตาม AppTimeZone)
//   - ครบ 4 ขอบเขตในแต่ละเป้าหมาย: Scope 1, Scope 2, Scope 3 และ Scope 4 (Separate Report)
//     Scope 3 / Scope 4 มาทั้งจาก Topic ของตัวเอง และจาก Child ที่แตกอัตโนมัติตาม TbEmissionMapping
//     (ใช้เงื่อนไขชุดเดียวกับ ActivityTopicApiController: แตกกิ่งเฉพาะ Master ที่เป็น Scope 1/2)
//   - รูปแบบการเกิดข้อมูล 3 แบบ:
//       Monthly  = มีทุกเดือน (ค่าไฟฟ้า ค่าน้ำ น้ำมัน LPG ขยะ ฯลฯ)
//       Sporadic = บางเดือน (เติมสารทำความเย็น) สุ่มตามความน่าจะเป็น และบังคับอย่างน้อย 1 ครั้งต่อปี
//       Annual   = ปีละครั้งในเดือนที่กำหนด (เติมถังดับเพลิง CO2 ตามรอบบำรุงรักษา)
//   - ปริมาณ = ฐานต่อเดือน x แนวโน้มรายปี x ฤดูกาล x สัญญาณรบกวนสุ่ม (+ spike บางเดือนสำหรับเครื่องปั่นไฟ)
//     ใช้ Random แบบกำหนด seed ต่อหัวข้อ ผลลัพธ์จึงเหมือนเดิมทุกครั้งที่รัน (ทำซ้ำได้ / ตรวจสอบได้)
//
// Idempotent: ค้นหาด้วย TaxId / Code / Title ก่อนสร้างเสมอ, Event ที่มีอยู่แล้วในเดือนเดียวกันจะไม่ถูกสร้างซ้ำ
//   และข้อมูลที่ผู้ใช้ Soft Delete ไปแล้วจะไม่ถูกสร้างกลับขึ้นมา
public static class DatabaseSeeder7
{
    #region constants
    public const int FirstYear = 2023;
    public const string SubscriptionTaxId = "0105567000777";
    public const string SubscriptionTitle = "บริษัท เดโม่ฟู้ดส์ อินดัสทรี จำกัด (ข้อมูลจำลอง)";
    public const string CorporateCode = "D7-CRP-001";
    public const string CompanyCode = "D7-COM-001";
    public const string PlantCode = "D7-FAC-001";
    public const string DcCode = "D7-FAC-002";
    private const string SeederUser = "System";

    // [2026-09-26][Claude] สถานการณ์ทดสอบ Dashboard: ปีฐาน 2024 / ปีคำนวณ 2025 / เป้า Net-Zero ลด 50% ภายใน 2030
    //   ยอดปี 2025 = ยอดเดือนเดียวกันของปี 2024 x ตัวคูณรายเดือนด้านล่าง (ทุก Topic ใช้ตัวคูณเดียวกัน → ยอดรวมรายเดือนเป็นสัดส่วนเดียวกัน)
    //   เส้นเป้าหมายปี 2025 = ปีฐาน x (1 - 0.5 x 1/6) = 91.67% ของปีฐาน; เกณฑ์เตือน 80% ของเส้นเป้า = 73.33% ของปีฐาน
    public const int ScenarioBaseYear = 2024;
    public const int ScenarioCalcYear = 2025;
    public const int ScenarioTargetYear = 2030;
    public const decimal ScenarioReductionPercent = 50m;
    public static readonly double[] ScenarioCalcYearFactors =
    {
        1.10, // ม.ค. สูงกว่าปีฐาน (แดง)
        0.96, // ก.พ. ต่ำกว่าปีฐาน แต่เกินเส้นเป้า (แดง)
        0.85, // มี.ค. ใกล้เส้นเป้า >80% (ส้ม)
        0.55, // เม.ย. ต่ำกว่าเส้นเป้ามาก (เขียว)
        1.12, // พ.ค. สูงกว่าปีฐาน (แดง)
        0.80, // มิ.ย. ใกล้เส้นเป้า (ส้ม)
        0.60, // ก.ค. ต่ำกว่าเส้นเป้ามาก (เขียว)
        1.05, // ส.ค. สูงกว่าปีฐาน (แดง)
        0.65, // ก.ย. ต่ำกว่าเส้นเป้ามาก (เขียว)
        0.90, // ต.ค. ใกล้เส้นเป้า (ส้ม)
        1.08, // พ.ย. สูงกว่าปีฐาน (แดง)
        0.58  // ธ.ค. ต่ำกว่าเส้นเป้ามาก (เขียว)
    };
    #endregion

    #region seasonal profiles (index 0 = มกราคม)
    // โหลดทำความเย็น: สูงช่วงหน้าร้อน มี.ค.-พ.ค. ต่ำช่วงหน้าหนาว ธ.ค.-ม.ค.
    private static readonly double[] CoolingSeason = { 0.92, 0.95, 1.08, 1.15, 1.12, 1.03, 0.99, 0.98, 0.97, 0.98, 0.95, 0.90 };
    // การใช้น้ำ: สูงช่วงหน้าร้อน
    private static readonly double[] WaterSeason = { 0.95, 0.97, 1.07, 1.12, 1.06, 1.00, 0.98, 0.97, 0.96, 0.98, 0.97, 0.97 };
    // กำลังการผลิต/ขนส่ง: ต่ำช่วงสงกรานต์ (เม.ย.) สูงช่วงไตรมาส 4 (เทศกาลปลายปี)
    private static readonly double[] ProductionSeason = { 0.97, 0.95, 1.02, 0.86, 1.00, 0.98, 1.00, 1.02, 1.00, 1.06, 1.08, 1.10 };
    // งานสำนักงาน: ลดลงช่วงสงกรานต์และปลายปี
    private static readonly double[] OfficeSeason = { 1.00, 1.00, 1.05, 0.85, 1.00, 1.00, 1.00, 1.02, 1.02, 1.03, 1.00, 0.93 };
    private static readonly double[] FlatSeason = { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
    #endregion

    public enum eEventPattern
    {
        Monthly = 1,
        Sporadic = 2,
        Annual = 3
    }

    /// <summary>นิยามหัวข้อกิจกรรมหนึ่งรายการของชุดข้อมูลจำลอง</summary>
    public sealed class TopicPlan
    {
        /// <summary>ระดับขององค์กรที่หัวข้อผูกอยู่ (Corporate / Company / Facility)</summary>
        public eTopicLevel Level { get; init; } = eTopicLevel.Facility;
        /// <summary>Code ของ Corporate / Company / Facility ตาม Level</summary>
        public string TargetCode { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Remark { get; init; } = string.Empty;
        public string EventRemark { get; init; } = string.Empty;
        /// <summary>Id ของ Master Emission Source (อ้างอิง tb_emission_source.json)</summary>
        public int SourceId { get; init; }
        public int RandomSeed { get; init; }
        public eEventPattern Pattern { get; init; } = eEventPattern.Monthly;

        // Monthly / Annual
        public double BaseQuantity { get; init; }
        public double NoisePercent { get; init; } = 0.05;   // ±5%
        public double YearGrowth { get; init; }              // 0.03 = +3% ต่อปี
        public double[] Season { get; init; } = FlatSeason;
        public double SpikeChance { get; init; }             // โอกาสเกิด spike ต่อเดือน (เช่น ไฟดับต้องเดินเครื่องปั่นไฟนาน)
        public double SpikeFactor { get; init; } = 1;

        // Sporadic
        public double Probability { get; init; }             // โอกาสเกิดต่อเดือน
        public double MinQuantity { get; init; }
        public double MaxQuantity { get; init; }

        // Annual
        public int AnnualMonth { get; init; } = 6;

        public int Decimals { get; init; } = 2;
    }

    #region plans
    public static IReadOnlyList<TopicPlan> BuildPlans()
    {
        #region hide [2026-09-24]
        return new List<TopicPlan>
        {
            // ======================= D7-FAC-001 โรงงานผลิตอาหารแปรรูป นครปฐม =======================
            // Scope 1
            new() { TargetCode = PlantCode, RandomSeed = 7101, SourceId = 6,   // LPG (kg) 0101 Stationary -> mapping: Scope3 ก๊าซหุงต้ม
                    Title = "ก๊าซ LPG หม้อไอน้ำไลน์ผลิต (Boiler LPG)", Remark = "Scope 1 - การเผาไหม้อยู่กับที่ หม้อไอน้ำ 2 ตัน/ชม.",
                    EventRemark = "ใบกำกับภาษีก๊าซ LPG (ถังเหมา)", BaseQuantity = 9500, NoisePercent = 0.06, YearGrowth = 0.03, Season = ProductionSeason, Decimals = 0 },
            new() { TargetCode = PlantCode, RandomSeed = 7102, SourceId = 20,  // Diesel B7: Report in Scope 1 (litre) 0101 -> mapping: Scope3 + Biogenic
                    Title = "น้ำมันดีเซล B7 เครื่องกำเนิดไฟฟ้าสำรอง (Generator)", Remark = "Scope 1 - เครื่องกำเนิดไฟฟ้าสำรอง 500 kVA (ทดสอบเดินเครื่องรายสัปดาห์ + ไฟดับ)",
                    EventRemark = "บันทึกการเติมน้ำมันเครื่องปั่นไฟ", BaseQuantity = 180, NoisePercent = 0.15, SpikeChance = 0.10, SpikeFactor = 2.8, Decimals = 1 },
            new() { TargetCode = PlantCode, RandomSeed = 7103, SourceId = 114, // Diesel B7: Report in Scope 1 (litre) 0102 Mobile -> mapping: Scope3 + Biogenic
                    Title = "น้ำมันดีเซล B7 รถยก/รถบรรทุกภายในโรงงาน", Remark = "Scope 1 - การเผาไหม้เคลื่อนที่ (รถยกดีเซล 6 คัน, รถบรรทุก 6 ล้อ 2 คัน)",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 1450, NoisePercent = 0.07, YearGrowth = 0.02, Season = ProductionSeason, Decimals = 1 },
            new() { TargetCode = PlantCode, RandomSeed = 7104, SourceId = 123, // Gasohol E20: Report in Scope 1 (litre) 0102
                    Title = "น้ำมันแก๊สโซฮอล์ E20 รถยนต์ส่วนกลาง", Remark = "Scope 1 - รถยนต์ส่วนกลางฝ่ายบริหารและฝ่ายขาย 5 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 620, NoisePercent = 0.10, Season = OfficeSeason, Decimals = 1 },
            new() { TargetCode = PlantCode, RandomSeed = 7105, SourceId = 144, Pattern = eEventPattern.Sporadic, // R-410a (kg) 0104
                    Title = "สารทำความเย็น R-410A ห้องเย็นและเครื่องปรับอากาศ", Remark = "Scope 1 - การรั่วไหล เติมสารทำความเย็นตามใบงานซ่อมบำรุง",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-410A", Probability = 0.20, MinQuantity = 3, MaxQuantity = 12, Decimals = 1 },
            new() { TargetCode = PlantCode, RandomSeed = 7106, SourceId = 685, Pattern = eEventPattern.Annual, AnnualMonth = 6, // CO2 (kg) 0105
                    Title = "ก๊าซ CO2 เติมถังดับเพลิงประจำปี", Remark = "Scope 1 - เติมถังดับเพลิงชนิด CO2 ตามรอบตรวจสอบประจำปี (15 ถัง)",
                    EventRemark = "ใบรับรองการเติมถังดับเพลิงประจำปี", BaseQuantity = 45, NoisePercent = 0.10, Decimals = 1 },
            // Scope 2
            new() { TargetCode = PlantCode, RandomSeed = 7107, SourceId = 131, // ไฟฟ้า grid mix 2022-2024 (kWh) 0201 -> mapping: Scope3 ไฟฟ้าต้นน้ำ
                    Title = "การใช้ไฟฟ้าจากการไฟฟ้าส่วนภูมิภาค (กฟภ.)", Remark = "Scope 2 - มิเตอร์หลักโรงงาน (TOU 22 kV)",
                    EventRemark = "ใบแจ้งค่าไฟฟ้า กฟภ.", BaseQuantity = 215000, NoisePercent = 0.05, YearGrowth = 0.025, Season = CoolingSeason, Decimals = 0 },
            // Scope 3
            new() { TargetCode = PlantCode, RandomSeed = 7108, SourceId = 190, // น้ำประปา กปภ. (m³)
                    Title = "การใช้น้ำประปา (การประปาส่วนภูมิภาค)", Remark = "Scope 3 - น้ำประปาใช้ในกระบวนการผลิตและล้างทำความสะอาด",
                    EventRemark = "ใบแจ้งค่าน้ำประปา กปภ.", BaseQuantity = 3200, NoisePercent = 0.07, YearGrowth = 0.02, Season = WaterSeason, Decimals = 0 },
            new() { TargetCode = PlantCode, RandomSeed = 7109, SourceId = 626, // ฝังกลบขยะแบบถูกหลักสุขาภิบาล (kg)
                    Title = "ขยะทั่วไปส่งกำจัดแบบฝังกลบ", Remark = "Scope 3 - ของเสียจากการดำเนินงาน (มีโครงการคัดแยกรีไซเคิล ลดลงต่อเนื่อง)",
                    EventRemark = "ใบกำกับการขนส่งของเสีย (Manifest)", BaseQuantity = 7800, NoisePercent = 0.09, YearGrowth = -0.04, Season = ProductionSeason, Decimals = 0 },
            new() { TargetCode = PlantCode, RandomSeed = 7110, SourceId = 254, // รถ 10 ล้อ 100% Loading (tkm)
                    Title = "การขนส่งวัตถุดิบเข้าโรงงาน (รถบรรทุก 10 ล้อ)", Remark = "Scope 3 - การขนส่งต้นน้ำโดยผู้รับจ้างขนส่ง",
                    EventRemark = "สรุปเที่ยววิ่งผู้รับจ้างขนส่ง", BaseQuantity = 48000, NoisePercent = 0.10, YearGrowth = 0.03, Season = ProductionSeason, Decimals = 0 },
            // Scope 4 (Separate Report)
            new() { TargetCode = PlantCode, RandomSeed = 7111, SourceId = 134, Pattern = eEventPattern.Sporadic, // R-22 (kg) 0401-IPCC
                    Title = "สารทำความเย็น R-22 เครื่องทำน้ำเย็นรุ่นเก่า (Chiller)", Remark = "Scope 4 - ก๊าซนอกพิธีสารเกียวโต (HCFC-22) รายงานแยก",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-22", Probability = 0.10, MinQuantity = 2, MaxQuantity = 8, Decimals = 1 },

            // ======================= D7-FAC-002 ศูนย์กระจายสินค้า บางนา =======================
            // Scope 1
            new() { TargetCode = DcCode, RandomSeed = 7201, SourceId = 114, // Diesel B7 Mobile -> mapping: Scope3 + Biogenic
                    Title = "น้ำมันดีเซล B7 รถขนส่งสินค้าของบริษัท", Remark = "Scope 1 - รถบรรทุกห้องเย็นกระจายสินค้าของบริษัท 12 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 6800, NoisePercent = 0.08, YearGrowth = 0.04, Season = ProductionSeason, Decimals = 1 },
            new() { TargetCode = DcCode, RandomSeed = 7202, SourceId = 106, // LPG (kg) 0102 Mobile -> mapping: Scope3 ก๊าซหุงต้ม
                    Title = "ก๊าซ LPG รถยกในคลังสินค้า", Remark = "Scope 1 - รถยก LPG 8 คัน",
                    EventRemark = "ใบส่งของก๊าซ LPG ถัง 15 กก.", BaseQuantity = 750, NoisePercent = 0.08, YearGrowth = 0.02, Season = ProductionSeason, Decimals = 0 },
            new() { TargetCode = DcCode, RandomSeed = 7203, SourceId = 20, // Diesel B7 Stationary -> mapping: Scope3 + Biogenic
                    Title = "น้ำมันดีเซล B7 เครื่องกำเนิดไฟฟ้าสำรอง (Generator)", Remark = "Scope 1 - เครื่องกำเนิดไฟฟ้าสำรอง 250 kVA",
                    EventRemark = "บันทึกการเติมน้ำมันเครื่องปั่นไฟ", BaseQuantity = 90, NoisePercent = 0.20, SpikeChance = 0.08, SpikeFactor = 3.0, Decimals = 1 },
            new() { TargetCode = DcCode, RandomSeed = 7204, SourceId = 135, Pattern = eEventPattern.Sporadic, // R-32 (kg)
                    Title = "สารทำความเย็น R-32 เครื่องปรับอากาศสำนักงาน", Remark = "Scope 1 - การรั่วไหล เครื่องปรับอากาศแบบแยกส่วน 24 เครื่อง",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-32", Probability = 0.15, MinQuantity = 1, MaxQuantity = 4, Decimals = 1 },
            new() { TargetCode = DcCode, RandomSeed = 7205, SourceId = 138, Pattern = eEventPattern.Sporadic, // R-134a (kg)
                    Title = "สารทำความเย็น R-134a ตู้ทำความเย็นรถขนส่ง", Remark = "Scope 1 - การรั่วไหล ชุดทำความเย็นบนรถบรรทุกห้องเย็น",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-134a", Probability = 0.22, MinQuantity = 2, MaxQuantity = 6, Decimals = 1 },
            // Scope 2
            new() { TargetCode = DcCode, RandomSeed = 7206, SourceId = 131, // ไฟฟ้า -> mapping: Scope3 ไฟฟ้าต้นน้ำ
                    Title = "การใช้ไฟฟ้าจากการไฟฟ้านครหลวง (กฟน.)", Remark = "Scope 2 - ห้องเย็นควบคุมอุณหภูมิและสำนักงาน",
                    EventRemark = "ใบแจ้งค่าไฟฟ้า กฟน.", BaseQuantity = 78000, NoisePercent = 0.05, YearGrowth = 0.03, Season = CoolingSeason, Decimals = 0 },
            // Scope 3
            new() { TargetCode = DcCode, RandomSeed = 7207, SourceId = 189, // น้ำประปา กปน. (m³)
                    Title = "การใช้น้ำประปา (การประปานครหลวง)", Remark = "Scope 3 - น้ำประปาใช้ในสำนักงานและล้างรถขนส่ง",
                    EventRemark = "ใบแจ้งค่าน้ำประปา กปน.", BaseQuantity = 520, NoisePercent = 0.08, YearGrowth = 0.01, Season = WaterSeason, Decimals = 0 },
            new() { TargetCode = DcCode, RandomSeed = 7208, SourceId = 643, // กระดาษพิมพ์เขียน (kg)
                    Title = "กระดาษพิมพ์เขียน A4 สำนักงาน", Remark = "Scope 3 - สินค้าและบริการที่จัดซื้อ (ลดลงจากนโยบายเอกสารอิเล็กทรอนิกส์)",
                    EventRemark = "ใบสั่งซื้อวัสดุสำนักงาน", BaseQuantity = 120, NoisePercent = 0.12, YearGrowth = -0.08, Season = OfficeSeason, Decimals = 1 },
            new() { TargetCode = DcCode, RandomSeed = 7209, SourceId = 626, // ฝังกลบ (kg)
                    Title = "ขยะทั่วไปส่งกำจัดแบบฝังกลบ", Remark = "Scope 3 - ของเสียจากการดำเนินงาน (บรรจุภัณฑ์และขยะสำนักงาน)",
                    EventRemark = "ใบกำกับการขนส่งของเสีย (Manifest)", BaseQuantity = 2600, NoisePercent = 0.10, YearGrowth = -0.03, Season = ProductionSeason, Decimals = 0 },
            // Scope 4 (Separate Report)
            new() { TargetCode = DcCode, RandomSeed = 7210, SourceId = 134, Pattern = eEventPattern.Sporadic, // R-22 (kg)
                    Title = "สารทำความเย็น R-22 เครื่องปรับอากาศรุ่นเก่า", Remark = "Scope 4 - ก๊าซนอกพิธีสารเกียวโต (HCFC-22) รายงานแยก",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-22", Probability = 0.08, MinQuantity = 1, MaxQuantity = 3, Decimals = 1 },

            // ======================= D7-CRP-001 ระดับ Corporate (สำนักงานใหญ่ กรุงเทพฯ) =======================
            // Scope 1 (+ Scope 3 / Scope 4 Biogenic จาก mapping ของดีเซล B7)
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7301, SourceId = 114, // Diesel B7 Mobile -> mapping: Scope3 + Biogenic
                    Title = "น้ำมันดีเซล B7 รถตู้สำนักงานใหญ่", Remark = "Scope 1 - รถตู้รับส่งผู้บริหารและเอกสารของสำนักงานใหญ่ 3 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 420, NoisePercent = 0.10, YearGrowth = 0.01, Season = OfficeSeason, Decimals = 1 },
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7302, SourceId = 123, // Gasohol E20 Mobile
                    Title = "น้ำมันแก๊สโซฮอล์ E20 รถประจำตำแหน่งผู้บริหาร", Remark = "Scope 1 - รถประจำตำแหน่งผู้บริหาร 4 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 380, NoisePercent = 0.10, Season = OfficeSeason, Decimals = 1 },
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7303, SourceId = 135, Pattern = eEventPattern.Sporadic, // R-32
                    Title = "สารทำความเย็น R-32 เครื่องปรับอากาศสำนักงานใหญ่", Remark = "Scope 1 - การรั่วไหล เครื่องปรับอากาศแบบแยกส่วน 40 เครื่อง",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-32", Probability = 0.18, MinQuantity = 1, MaxQuantity = 5, Decimals = 1 },
            // Scope 2 (+ Scope 3 ไฟฟ้าต้นน้ำจาก mapping)
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7304, SourceId = 131,
                    Title = "การใช้ไฟฟ้าสำนักงานใหญ่ (กฟน.)", Remark = "Scope 2 - อาคารสำนักงานใหญ่ 6 ชั้น",
                    EventRemark = "ใบแจ้งค่าไฟฟ้า กฟน.", BaseQuantity = 24000, NoisePercent = 0.05, YearGrowth = -0.02, Season = CoolingSeason, Decimals = 0 },
            // Scope 3
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7305, SourceId = 189,
                    Title = "การใช้น้ำประปาสำนักงานใหญ่ (กปน.)", Remark = "Scope 3 - น้ำประปาอาคารสำนักงานใหญ่",
                    EventRemark = "ใบแจ้งค่าน้ำประปา กปน.", BaseQuantity = 180, NoisePercent = 0.08, Season = WaterSeason, Decimals = 0 },
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7306, SourceId = 643,
                    Title = "กระดาษพิมพ์เขียน A4 สำนักงานใหญ่", Remark = "Scope 3 - สินค้าและบริการที่จัดซื้อ (ลดลงจากระบบเอกสารอิเล็กทรอนิกส์)",
                    EventRemark = "ใบสั่งซื้อวัสดุสำนักงาน", BaseQuantity = 65, NoisePercent = 0.12, YearGrowth = -0.10, Season = OfficeSeason, Decimals = 1 },
            // Scope 4 (Separate Report)
            new() { Level = eTopicLevel.Corporate, TargetCode = CorporateCode, RandomSeed = 7307, SourceId = 134, Pattern = eEventPattern.Sporadic,
                    Title = "สารทำความเย็น R-22 เครื่องปรับอากาศห้องเซิร์ฟเวอร์รุ่นเก่า", Remark = "Scope 4 - ก๊าซนอกพิธีสารเกียวโต (HCFC-22) รายงานแยก",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-22", Probability = 0.08, MinQuantity = 1, MaxQuantity = 3, Decimals = 1 },

            // ======================= D7-COM-001 ระดับ Company (กิจกรรมทั้งบริษัทที่ไม่ผูกกับสถานประกอบการใด) =======================
            // Scope 1 (+ Scope 3 / Scope 4 Biogenic จาก mapping ของดีเซล B7)
            new() { Level = eTopicLevel.Company, TargetCode = CompanyCode, RandomSeed = 7401, SourceId = 114,
                    Title = "น้ำมันดีเซล B7 รถกระบะทีมขายและทีมบริการ", Remark = "Scope 1 - รถกระบะทีมขายต่างจังหวัดและทีมช่างบริการ 14 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 2100, NoisePercent = 0.08, YearGrowth = 0.03, Season = ProductionSeason, Decimals = 1 },
            new() { Level = eTopicLevel.Company, TargetCode = CompanyCode, RandomSeed = 7402, SourceId = 123,
                    Title = "น้ำมันแก๊สโซฮอล์ E20 รถยนต์ทีมขาย", Remark = "Scope 1 - รถเก๋งทีมขายกรุงเทพฯ และปริมณฑล 8 คัน",
                    EventRemark = "สรุปใบเสร็จน้ำมัน Fleet Card", BaseQuantity = 900, NoisePercent = 0.09, YearGrowth = 0.02, Season = OfficeSeason, Decimals = 1 },
            // Scope 2 (+ Scope 3 ไฟฟ้าต้นน้ำจาก mapping)
            new() { Level = eTopicLevel.Company, TargetCode = CompanyCode, RandomSeed = 7403, SourceId = 131,
                    Title = "การใช้ไฟฟ้าสำนักงานขายภูมิภาค (กฟภ.)", Remark = "Scope 2 - สำนักงานขายเช่า เชียงใหม่ / ขอนแก่น / หาดใหญ่",
                    EventRemark = "ใบแจ้งค่าไฟฟ้า กฟภ.", BaseQuantity = 6500, NoisePercent = 0.06, YearGrowth = 0.02, Season = CoolingSeason, Decimals = 0 },
            // Scope 3
            new() { Level = eTopicLevel.Company, TargetCode = CompanyCode, RandomSeed = 7404, SourceId = 254,
                    Title = "การขนส่งสินค้าจากโรงงานไปศูนย์กระจายสินค้า (ผู้รับจ้างขนส่ง)", Remark = "Scope 3 - การขนส่งและกระจายสินค้าที่บริษัทจ่ายค่าขนส่ง (รถบรรทุก 10 ล้อ)",
                    EventRemark = "สรุปเที่ยววิ่งผู้รับจ้างขนส่ง", BaseQuantity = 32000, NoisePercent = 0.10, YearGrowth = 0.04, Season = ProductionSeason, Decimals = 0 },
            // Scope 4 (Separate Report)
            new() { Level = eTopicLevel.Company, TargetCode = CompanyCode, RandomSeed = 7405, SourceId = 134, Pattern = eEventPattern.Sporadic,
                    Title = "สารทำความเย็น R-22 เครื่องปรับอากาศสำนักงานขายภูมิภาค", Remark = "Scope 4 - ก๊าซนอกพิธีสารเกียวโต (HCFC-22) รายงานแยก",
                    EventRemark = "ใบงานซ่อมบำรุง เติมสารทำความเย็น R-22", Probability = 0.10, MinQuantity = 1, MaxQuantity = 4, Decimals = 1 },
        };
        #endregion
    }
    #endregion

    public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-24]
        logger.LogInformation("Starting DatabaseSeeder7 (Synthetic demo tenant: topics at corporate, company and 2 facilities, {First}-now)...", FirstYear);
        try
        {
            // Master Library ต้องมีก่อน (Seed เฉพาะตารางที่ยังว่าง ไม่เขียนทับของเดิม)
            await DatabaseSeeder6.SeedIdentityAndLibraryIfEmptyAsync(context, logger);

            var sub = await SeedSubscriptionAsync(context, logger);
            if (sub == null) return;

            await SeedSubscriptionUsersAsync(context, sub.Id, logger);

            var targets = await SeedOrganizationAsync(context, sub.Id, logger);
            if (targets.Count == 0) return;

            await SeedTopicsAndEventsAsync(context, sub.Id, targets, logger);

            await SeedNetZeroTargetAsync(context, sub.Id, logger);

            logger.LogInformation("DatabaseSeeder7 completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during DatabaseSeeder7 seeding.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-26][Claude] ตั้งเป้า Net-Zero ระดับ Subscription (ปีฐาน 2024, ลด 50% ภายใน 2030) เฉพาะเมื่อยังไม่มี — ไม่เขียนทับค่าที่ผู้ใช้บันทึกเอง
    /// </summary>
    private static async Task SeedNetZeroTargetAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        var exists = await context.NetZeroTargets.AnyAsync(t => !t.IsDeleted && t.SubscriptionId == subId && t.CorporateId == null);
        if (exists) return;

        context.NetZeroTargets.Add(new TbNetZeroTarget
        {
            SubscriptionId = subId,
            CorporateId = null,
            BaseYear = ScenarioBaseYear,
            TargetYear = ScenarioTargetYear,
            TargetReductionPercent = ScenarioReductionPercent,
            EnableAlert = true,
            AlertThresholdPercent = 80.0m,
            CreatedAt = AppTimeZone.UtcNow,
            CreatedBy = SeederUser
        });
        await context.SaveChangesAsync(SeederUser);
        logger.LogInformation("DatabaseSeeder7: Net-Zero target seeded (Base {Base}, Target {Target}, -{Pct}%).", ScenarioBaseYear, ScenarioTargetYear, ScenarioReductionPercent);
    }

    /// <summary>
    /// เดือนสุดท้ายที่ปิดงวดแล้ว (เดือนก่อนหน้าเดือนปัจจุบันตาม AppTimeZone) คืนค่าเป็นวันที่ 1 ของเดือน
    /// </summary>
    public static DateTime GetLastClosedMonth()
    {
        #region hide [2026-09-24]
        var nowLocal = AppTimeZone.LocalNow;
        return new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified).AddMonths(-1);
        #endregion
    }

    #region organization
    private static async Task<TbSubscription?> SeedSubscriptionAsync(ApplicationDbContext context, ILogger logger)
    {
        #region hide [2026-09-24]
        var existing = await context.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TaxId == SubscriptionTaxId);

        if (existing != null)
        {
            if (existing.IsDeleted)
            {
                logger.LogInformation("DatabaseSeeder7: Demo subscription was soft-deleted by user. Skipped all seeding.");
                return null;
            }
            return existing;
        }

        var sub = new TbSubscription
        {
            Title = SubscriptionTitle,
            TaxId = SubscriptionTaxId,
            OwnerName = SubscriptionTitle,
            OwnerEmail = "demo-001@acamp.com",
            OwnerPhone = "02-000-7777",
            ContactPerson = "ผู้ดูแลระบบ (ข้อมูลจำลอง)",
            BillingAddress = "เลขที่ 77 ถนนตัวอย่าง ตำบลตัวอย่าง อำเภอสามพราน จังหวัดนครปฐม 73110 (ข้อมูลจำลอง)",
            SubscriptionPlan = eSubscriptionPlan.Premium,
            OwnerType = eSubscriptionOwnerType.Juristic,
            StartDate = new DateTime(FirstYear, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiryDate = AppTimeZone.UtcNow.AddYears(2),
            IsActive = true,
            IsDeleted = false,
            Remarks = "DatabaseSeeder7 - Synthetic demo tenant for end-to-end testing (not a real organization)"
        };
        sub.ApplyPlanQuotas(eSubscriptionPlan.Premium);

        await context.Subscriptions.AddAsync(sub);
        await context.SaveChangesAsync(SeederUser);
        logger.LogInformation("DatabaseSeeder7: Created demo subscription '{Title}' (ID {Id}).", sub.Title, sub.Id);
        return sub;
        #endregion
    }

    private static async Task SeedSubscriptionUsersAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        #region hide [2026-09-24]
        var usersToMap = new[]
        {
            ("demo-001@acamp.com", eSubscriptionRole.Owner),
            ("patipath.eia@nectec.or.th", eSubscriptionRole.Admin)
        };

        foreach (var (email, role) in usersToMap)
        {
            var normalized = email.ToLower();
            var exists = await context.SubscriptionUsers.IgnoreQueryFilters()
                .AnyAsync(u => u.SubscriptionId == subId && u.Email.ToLower() == normalized);
            if (exists) continue;

            await context.SubscriptionUsers.AddAsync(new TbSubscriptionUser
            {
                SubscriptionId = subId,
                Email = email,
                Role = role,
                IsActive = true
            });
            logger.LogInformation("DatabaseSeeder7: Mapped user '{Email}' as {Role} to subscription ID {SubId}.", email, role, subId);
        }

        await context.SaveChangesAsync(SeederUser);
        #endregion
    }

    /// <summary>
    /// สร้าง Corporate -> Company -> 2 Facilities แล้วคืน Id ของเป้าหมายที่ใช้งานได้ (key = Code ซึ่งไม่ซ้ำกันข้ามระดับ)
    /// </summary>
    private static async Task<Dictionary<string, int>> SeedOrganizationAsync(ApplicationDbContext context, int subId, ILogger logger)
    {
        #region hide [2026-09-24]
        var result = new Dictionary<string, int>();
        var corpCode = CorporateCode.ToLower();
        var compCode = CompanyCode.ToLower();

        // 1. Corporate
        var corp = await context.Corporates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.SubscriptionId == subId && c.Code.ToLower() == corpCode);
        if (corp == null)
        {
            corp = new TbCorporate
            {
                SubscriptionId = subId,
                Code = CorporateCode,
                Title = "บริษัท เดโม่ฟู้ดส์ อินดัสทรี จำกัด (ข้อมูลจำลอง)",
                Remark = "องค์กรระดับรวมงบ (ข้อมูลจำลองสำหรับทดสอบระบบ DatabaseSeeder7)",
                MaterialityThreshold = 5.0m,
                IsActive = true,
                IsDeleted = false
            };
            corp.EnsureKeyIdGenerated();
            await context.Corporates.AddAsync(corp);
            await context.SaveChangesAsync(SeederUser);
            logger.LogInformation("DatabaseSeeder7: Created corporate '{Title}'.", corp.Title);
        }
        else if (corp.IsDeleted)
        {
            logger.LogInformation("DatabaseSeeder7: Demo corporate was soft-deleted by user. Skipped organization seeding.");
            return result;
        }
        result[CorporateCode] = corp.Id;

        // 2. Company
        var comp = await context.Companies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CorporateId == corp.Id && c.Code.ToLower() == compCode);
        if (comp == null)
        {
            comp = new TbCompany
            {
                CorporateId = corp.Id,
                Code = CompanyCode,
                Title = "บริษัท เดโม่ฟู้ดส์ อินดัสทรี จำกัด",
                EquityShare = 100,
                Remark = "นิติบุคคลผู้รายงาน (ข้อมูลจำลองสำหรับทดสอบระบบ DatabaseSeeder7)",
                IsActive = true,
                IsDeleted = false
            };
            comp.EnsureKeyIdGenerated();
            await context.Companies.AddAsync(comp);
            await context.SaveChangesAsync(SeederUser);
            logger.LogInformation("DatabaseSeeder7: Created company '{Title}'.", comp.Title);
        }
        else if (comp.IsDeleted)
        {
            logger.LogInformation("DatabaseSeeder7: Demo company was soft-deleted by user. Skipped facility seeding.");
            return result;
        }
        result[CompanyCode] = comp.Id;

        // 3. Facilities
        var facilityDefs = new[]
        {
            (Code: PlantCode, Title: "โรงงานผลิตอาหารแปรรูป นครปฐม", Remark: "โรงงานผลิตอาหารแปรรูปและห้องเย็น อ.สามพราน จ.นครปฐม (ข้อมูลจำลอง)"),
            (Code: DcCode, Title: "ศูนย์กระจายสินค้า บางนา", Remark: "คลังสินค้าควบคุมอุณหภูมิและสำนักงานขาย อ.บางพลี จ.สมุทรปราการ (ข้อมูลจำลอง)")
        };

        foreach (var def in facilityDefs)
        {
            var code = def.Code.ToLower();
            var fac = await context.Facilities.IgnoreQueryFilters()
                .FirstOrDefaultAsync(f => f.CompanyId == comp.Id && f.Code.ToLower() == code);

            if (fac == null)
            {
                fac = new TbFacility
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
                await context.SaveChangesAsync(SeederUser);
                logger.LogInformation("DatabaseSeeder7: Created facility '{Title}' ({Code}).", fac.Title, fac.Code);
            }
            else if (fac.IsDeleted)
            {
                logger.LogInformation("DatabaseSeeder7: Facility {Code} was soft-deleted by user. Skipped.", def.Code);
                continue;
            }

            result[def.Code] = fac.Id;
        }

        return result;
        #endregion
    }
    #endregion

    #region topics & events
    private static async Task SeedTopicsAndEventsAsync(
        ApplicationDbContext context,
        int subId,
        Dictionary<string, int> targets,
        ILogger logger)
    {
        #region hide [2026-09-24]
        var plans = BuildPlans();
        var today = AppTimeZone.LocalNow.Date;
        var lastMonth = GetLastClosedMonth();

        var activityGroups = await context.ActivityGroups.AsNoTracking().ToListAsync();

        // Mapping ระดับระบบ (SubscriptionId == null) เรียงตาม Id เพื่อให้ผลลัพธ์คงที่
        var mappings = await context.EmissionMappings.AsNoTracking()
            .Where(m => !m.IsDeleted && m.SubscriptionId == null)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var masterIds = plans.Select(p => p.SourceId).Distinct().ToList();
        var childIds = mappings
            .Where(m => masterIds.Contains(m.Scope1ChildId) || masterIds.Contains(m.Scope2ChildId))
            .SelectMany(m => new[] { m.Scope3ChildId, m.BiogenicChildId })
            .Where(id => id != 0);
        var neededIds = masterIds.Concat(childIds).Distinct().ToList();

        // ใช้เฉพาะ EF ที่ยังไม่หมดอายุ (IsExpire เป็น [NotMapped] จึงกรองด้วย DateExpire)
        var sources = await context.EmissionSources.AsNoTracking()
            .Include(s => s.Group)
            .Where(s => neededIds.Contains(s.Id) && !s.IsDeleted && (s.DateExpire == null || s.DateExpire >= today))
            .ToDictionaryAsync(s => s.Id);

        int orderId = 1, createdTopics = 0, createdEvents = 0, skipped = 0;

        foreach (var plan in plans)
        {
            if (!targets.TryGetValue(plan.TargetCode, out var targetId)) { skipped++; continue; }

            if (!sources.TryGetValue(plan.SourceId, out var master))
            {
                logger.LogWarning("DatabaseSeeder7: Skipped '{Title}' because emission source ID {Id} was not found, deleted or expired.", plan.Title, plan.SourceId);
                skipped++;
                continue;
            }

            var normalizedTitle = plan.Title.ToLower();
            var topic = await context.Topics.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.SubscriptionId == subId
                                       && t.Level == plan.Level
                                       && t.TargetId == targetId
                                       && t.Title.ToLower() == normalizedTitle);

            if (topic != null && topic.IsDeleted)
            {
                // ผู้ใช้ลบหัวข้อนี้ไปแล้ว ไม่สร้างกลับ
                continue;
            }

            if (topic == null)
            {
                topic = await CreateTopicWithChildsAsync(context, subId, targetId, plan, master, sources, mappings, activityGroups, orderId);
                createdTopics++;
            }
            orderId++;

            createdEvents += await SeedEventsAsync(context, subId, topic.Id, plan, lastMonth);
        }

        logger.LogInformation(
            "DatabaseSeeder7: Created {Topics} topic(s), {Events} event(s), skipped {Skipped} plan(s). Period {First}-01 .. {Last:yyyy-MM}.",
            createdTopics, createdEvents, skipped, FirstYear, lastMonth);
        #endregion
    }

    /// <summary>
    /// สร้าง Topic + Child โดยแตกกิ่ง Scope 3 / Biogenic ตาม TbEmissionMapping (เงื่อนไขเดียวกับ ActivityTopicApiController)
    /// </summary>
    private static async Task<TbActivityTopic> CreateTopicWithChildsAsync(
        ApplicationDbContext context,
        int subId,
        int targetId,
        TopicPlan plan,
        TbEmissionSource master,
        Dictionary<int, TbEmissionSource> sources,
        List<TbEmissionMapping> mappings,
        List<TbActivityGroup> activityGroups,
        int orderId)
    {
        #region hide [2026-09-24]
        var topic = new TbActivityTopic
        {
            SubscriptionId = subId,
            Level = plan.Level,
            TargetId = targetId,
            EmissionGroupId = master.GroupId,
            Title = plan.Title,
            Remark = $"{plan.Remark} (ข้อมูลจำลอง DatabaseSeeder7)",
            OrderId = orderId,
            IsActive = true,
            IsDeleted = false
        };

        await context.Topics.AddAsync(topic);
        SyncShadowForeignKeys(context, topic);
        await context.SaveChangesAsync(SeederUser);

        var childs = new List<TbActivityTopicChild>
        {
            new()
            {
                TopicId = topic.Id,
                ActivityGroupId = TbEmissionMapping.GetActivityGroupIdForSource(master, activityGroups),
                SourceId = master.Id,
                ActivityUnitId = master.UnitId > 0 ? master.UnitId : null,
                UnitConversionRatio = 1m,
                EmissionSharePercent = 100f,
                IsActive = true
            }
        };

        var masterScope = master.Group?.TgoScopeNo;
        if (masterScope == eTgoScopeNo.Scope1 || masterScope == eTgoScopeNo.Scope2)
        {
            var mapping = mappings.FirstOrDefault(m => m.Scope1ChildId == master.Id || m.Scope2ChildId == master.Id);
            if (mapping != null)
            {
                if (mapping.Scope3ChildId > 0 && sources.TryGetValue(mapping.Scope3ChildId, out var s3Source))
                {
                    childs.Add(new TbActivityTopicChild
                    {
                        TopicId = topic.Id,
                        ActivityGroupId = TbEmissionMapping.GetActivityGroupIdForSource(s3Source, activityGroups),
                        SourceId = s3Source.Id,
                        ActivityUnitId = s3Source.UnitId > 0 ? s3Source.UnitId : null,
                        UnitConversionRatio = mapping.UnitConversionRatio ?? 1m,
                        EmissionSharePercent = 100f,
                        IsActive = true
                    });
                }

                if (mapping.BiogenicChildId > 0 && sources.TryGetValue(mapping.BiogenicChildId, out var bioSource))
                {
                    childs.Add(new TbActivityTopicChild
                    {
                        TopicId = topic.Id,
                        ActivityGroupId = TbEmissionMapping.GetActivityGroupIdForSource(bioSource, activityGroups),
                        SourceId = bioSource.Id,
                        ActivityUnitId = bioSource.UnitId > 0 ? bioSource.UnitId : null,
                        UnitConversionRatio = 1m,
                        EmissionSharePercent = TbEmissionMapping.BiogenicChildEmissionSharePercent, // [2026-09-24][Claude] EF Biogenic รวมสัดส่วน B7/E20 ไว้แล้ว (ตรงกับ Seeder2/4)
                        IsActive = true
                    });
                }
            }
        }

        await context.TopicChilds.AddRangeAsync(childs);
        await context.SaveChangesAsync(SeederUser);
        return topic;
        #endregion
    }

    /// <summary>
    /// สร้าง Event ตามรูปแบบของแผน ตั้งแต่ ม.ค. FirstYear ถึงเดือนล่าสุดที่ปิดงวด (ข้ามเดือนที่มี Event อยู่แล้ว)
    /// TimeStamp = วันที่ 1 ของเดือน (AppTimeZone.ToReportingPeriod) ตามมาตรฐานเดียวกับ ActivityEventService
    /// </summary>
    private static async Task<int> SeedEventsAsync(ApplicationDbContext context, int subId, int topicId, TopicPlan plan, DateTime lastMonth)
    {
        #region hide [2026-09-24]
        var existingMonths = (await context.Events.IgnoreQueryFilters()
                .Where(e => e.TopicId == topicId)
                .Select(e => e.TimeStamp)
                .ToListAsync())
            .Select(t => (t.Year, t.Month))
            .ToHashSet();

        var generated = GenerateQuantities(plan, lastMonth);
        var toAdd = new List<TbActivityEvent>();

        // [2026-09-26][Claude] ปีคำนวณของสถานการณ์ทดสอบ: ซิงก์ Event ที่ seeder สร้างไว้เดิมให้ตรงกับค่าใหม่ (DB ที่ seed ไปแล้ว)
        //   แก้เฉพาะ Event ที่ยังไม่ถูกลบ และ Remark ขึ้นต้นด้วยข้อความของแผน (ไม่แตะข้อมูลที่ผู้ใช้บันทึกเอง)
        var calcYearGenerated = generated
            .Where(g => g.Period.Year == ScenarioCalcYear)
            .ToDictionary(g => g.Period.Month, g => g.Quantity);
        var calcYearExisting = await context.Events
            .Where(e => e.TopicId == topicId && !e.IsDeleted && e.TimeStamp.Year == ScenarioCalcYear)
            .ToListAsync();
        var changed = false;
        foreach (var ev in calcYearExisting.Where(e => e.Remark != null && e.Remark.StartsWith(plan.EventRemark)))
        {
            if (calcYearGenerated.TryGetValue(ev.TimeStamp.Month, out var newQty))
            {
                if (ev.Quantity != newQty)
                {
                    ev.Quantity = newQty;
                    changed = true;
                }
            }
            else
            {
                ev.IsDeleted = true;
                changed = true;
            }
        }
        if (changed)
        {
            await context.SaveChangesAsync(SeederUser);
        }

        foreach (var (period, quantity) in generated)
        {
            if (existingMonths.Contains((period.Year, period.Month))) continue;

            toAdd.Add(new TbActivityEvent
            {
                SubscriptionId = subId,
                TopicId = topicId,
                Quantity = quantity,
                TimeStamp = AppTimeZone.ToReportingPeriod(period),
                Remark = $"{plan.EventRemark} งวด {period.Month:D2}/{period.Year}",
                IsActive = true,
                IsDeleted = false
            });
        }

        if (toAdd.Count > 0)
        {
            await context.Events.AddRangeAsync(toAdd);
            await context.SaveChangesAsync(SeederUser);
        }

        return toAdd.Count;
        #endregion
    }

    /// <summary>
    /// คำนวณปริมาณรายเดือนแบบสุ่ม (กำหนด seed) — ไม่แตะฐานข้อมูล จึงทดสอบได้โดยตรง
    /// ลำดับการเรียก Random คงที่ทุกครั้ง ผลลัพธ์จึงเหมือนเดิมเสมอสำหรับช่วงเวลาเดียวกัน
    /// </summary>
    public static List<(DateTime Period, decimal Quantity)> GenerateQuantities(TopicPlan plan, DateTime lastMonth)
    {
        #region hide [2026-09-24]
        var rng = new Random(plan.RandomSeed);
        var result = new List<(DateTime Period, decimal Quantity)>();

        for (int year = FirstYear; year <= lastMonth.Year; year++)
        {
            int lastMonthOfYear = year == lastMonth.Year ? lastMonth.Month : 12;
            if (lastMonthOfYear < 1) break;

            switch (plan.Pattern)
            {
                case eEventPattern.Monthly:
                    for (int month = 1; month <= lastMonthOfYear; month++)
                    {
                        double t = (year - FirstYear) + (month - 1) / 12.0;
                        double trend = Math.Pow(1 + plan.YearGrowth, t);
                        double noise = 1 + (rng.NextDouble() * 2 - 1) * plan.NoisePercent;
                        double qty = plan.BaseQuantity * trend * plan.Season[month - 1] * noise;
                        if (plan.SpikeChance > 0 && rng.NextDouble() < plan.SpikeChance)
                        {
                            qty *= plan.SpikeFactor;
                        }
                        AddIfPositive(result, year, month, qty, plan.Decimals);
                    }
                    break;

                case eEventPattern.Sporadic:
                    {
                        var picked = new List<int>();
                        for (int month = 1; month <= lastMonthOfYear; month++)
                        {
                            if (rng.NextDouble() < plan.Probability) picked.Add(month);
                        }
                        // บังคับอย่างน้อย 1 ครั้งต่อปี เพื่อให้ทุกปีมีข้อมูลของหัวข้อนี้
                        if (picked.Count == 0) picked.Add(rng.Next(1, lastMonthOfYear + 1));

                        foreach (var month in picked)
                        {
                            double qty = plan.MinQuantity + rng.NextDouble() * (plan.MaxQuantity - plan.MinQuantity);
                            AddIfPositive(result, year, month, qty, plan.Decimals);
                        }
                    }
                    break;

                case eEventPattern.Annual:
                    if (plan.AnnualMonth <= lastMonthOfYear)
                    {
                        double noise = 1 + (rng.NextDouble() * 2 - 1) * plan.NoisePercent;
                        AddIfPositive(result, year, plan.AnnualMonth, plan.BaseQuantity * noise, plan.Decimals);
                    }
                    break;
            }
        }

        // [2026-09-26][Claude] สถานการณ์ทดสอบ: แทนค่าปีคำนวณด้วย "เดือนเดียวกันของปีฐาน x ตัวคูณรายเดือน"
        //   ทำหลังวน Random ครบแล้ว ลำดับการสุ่มของปีอื่นจึงไม่เปลี่ยน; Sporadic/Annual ใช้เดือนเดียวกับปีฐาน
        if (lastMonth.Year >= ScenarioCalcYear)
        {
            int lastCalcMonth = lastMonth.Year == ScenarioCalcYear ? lastMonth.Month : 12;
            var baseEntries = result.Where(r => r.Period.Year == ScenarioBaseYear).ToList();
            result.RemoveAll(r => r.Period.Year == ScenarioCalcYear);
            foreach (var (period, quantity) in baseEntries)
            {
                if (period.Month > lastCalcMonth) continue;
                var qty = (double)quantity * ScenarioCalcYearFactors[period.Month - 1];
                AddIfPositive(result, ScenarioCalcYear, period.Month, qty, plan.Decimals);
            }
            result.Sort((a, b) => a.Period.CompareTo(b.Period));
        }

        return result;
        #endregion
    }

    private static void AddIfPositive(List<(DateTime Period, decimal Quantity)> result, int year, int month, double qty, int decimals)
    {
        var value = Math.Round((decimal)qty, decimals, MidpointRounding.AwayFromZero);
        if (value <= 0m) return;
        result.Add((new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified), value));
    }

    /// <summary>ตั้งค่า Shadow FK ของ Topic ตาม Level (สำเนาตรรกะเดียวกับ ActivityTopicService.SyncShadowForeignKeys)</summary>
    private static void SyncShadowForeignKeys(ApplicationDbContext context, TbActivityTopic topic)
    {
        #region hide [2026-09-24]
        var entry = context.Entry(topic);
        if (entry.Metadata.FindProperty("TbCorporateId") != null)
            entry.Property("TbCorporateId").CurrentValue = topic.Level == eTopicLevel.Corporate && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        if (entry.Metadata.FindProperty("TbCompanyId") != null)
            entry.Property("TbCompanyId").CurrentValue = topic.Level == eTopicLevel.Company && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        if (entry.Metadata.FindProperty("TbFacilityId") != null)
            entry.Property("TbFacilityId").CurrentValue = topic.Level == eTopicLevel.Facility && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        #endregion
    }
    #endregion
}
