using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.WebApp.Services.Reports;

public class CfoReportService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CfoReportService> _logger;
    private readonly IConfiguration _configuration;

    public CfoReportService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IWebHostEnvironment env,
        ILogger<CfoReportService> logger,
        IConfiguration? configuration = null)
    {
        _dbFactory = dbFactory;
        _env = env;
        _logger = logger;
        _configuration = configuration ?? new ConfigurationManager();
    }

    #region DTO Models
    public class CfoAuditReportDto
    {
        public string OrganizationTitle { get; set; } = string.Empty;
        public string OrganizationCode { get; set; } = string.Empty;
        public eTopicLevel Level { get; set; } = eTopicLevel.Corporate;
        public string LevelDisplay => Level.ToString();
        public int Year { get; set; }
        public string ConsolidationApproach { get; set; } = "Operational Control";
        public decimal MaterialityThreshold { get; set; } = 5.0m;
        public eGwpVersion GwpVersion { get; set; } = eGwpVersion.AR5;
        public string GwpVersionDisplay => GwpVersion switch
        {
            eGwpVersion.AR4 => "IPCC AR4 (100-year GWP)",
            eGwpVersion.AR5 => "IPCC AR5 (100-year GWP - TGO Standard)",
            eGwpVersion.AR6 => "IPCC AR6 (100-year GWP)",
            _ => "IPCC AR5 (100-year GWP)"
        };

        // Summary Totals (in tCO2e)
        public decimal TotalScope1_tCO2e { get; set; }
        public decimal TotalScope2_tCO2e { get; set; }
        public decimal TotalScope3_tCO2e { get; set; }
        public decimal TotalGrossEmissions_tCO2e => TotalScope1_tCO2e + TotalScope2_tCO2e + TotalScope3_tCO2e;
        public decimal TotalSeparate_tCO2e { get; set; }

        // Scope Breakdowns
        public List<ScopeBreakdownDto> Scopes { get; set; } = new();

        // ISO 14064-1 Categories Breakdowns
        public List<IsoCategoryBreakdownDto> IsoCategories { get; set; } = new();

        // Gas-by-Gas Inventory (7 GHG Gases)
        public List<GasInventoryDto> GasInventory { get; set; } = new();

        // Audit Trail Log
        public List<ActivityAuditLineDto> AuditLines { get; set; } = new();

        // [GHG-12] Pre-flight EF validity acknowledgment and warnings
        public string? ExpiryAcknowledgmentReason { get; set; }
        public List<string> PreFlightWarnings { get; set; } = new();

        // [GHG-02] Verification status
        public bool ExportVerified { get; set; } = true;
        public List<string> ExportVerificationMessages { get; set; } = new();
    }

    public class ScopeBreakdownDto
    {
        public eTgoScopeNo ScopeNo { get; set; }
        public string ScopeName { get; set; } = string.Empty;
        public decimal TotalKgCO2e { get; set; }
        public decimal Total_tCO2e => TotalKgCO2e / 1000m;
        public decimal SharePercent { get; set; }
        public List<ActivityGroupAuditDto> ActivityGroups { get; set; } = new();
    }

    public class IsoCategoryBreakdownDto
    {
        public int CategoryNo { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string StandardRef { get; set; } = "ISO 14064-1:2018";
        public decimal TotalKgCO2e { get; set; }
        public decimal Total_tCO2e => TotalKgCO2e / 1000m;
        public decimal SharePercent { get; set; }
        public List<ActivityGroupAuditDto> ActivityGroups { get; set; } = new();
    }

    public class ActivityGroupAuditDto
    {
        public int ActivityGroupId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public decimal TotalKgCO2e { get; set; }
        public decimal Total_tCO2e => TotalKgCO2e / 1000m;
        public decimal ScopeSharePercent { get; set; }
        public decimal OverallSharePercent { get; set; }
        public bool IsMaterialHotspot { get; set; }
        public List<ActivityAuditLineDto> Topics { get; set; } = new();
    }

    public class GasInventoryDto
    {
        public string GasName { get; set; } = string.Empty;
        public string ChemicalFormula { get; set; } = string.Empty;
        public decimal GwpFactor { get; set; }
        public decimal Scope1_tCO2e { get; set; }
        public decimal Scope2_tCO2e { get; set; }
        public decimal Scope3_tCO2e { get; set; }
        public decimal Total_tCO2e => Scope1_tCO2e + Scope2_tCO2e + Scope3_tCO2e;
        public decimal SharePercent { get; set; }
    }

    public class ActivityAuditLineDto
    {
        public int TopicId { get; set; }
        public string TopicTitle { get; set; } = string.Empty;
        public int ActivityGroupId { get; set; }
        public string ActivityGroupCode { get; set; } = string.Empty;
        public string ActivityGroupTitle { get; set; } = string.Empty;
        public eTgoScopeNo ScopeNo { get; set; }
        public int IsoCategoryNo { get; set; }
        public int SourceId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public string ActivityUnitName { get; set; } = string.Empty;
        public string EfUnitName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EF { get; set; }
        public decimal Ratio { get; set; } = 1m;
        public decimal SharePercent { get; set; } = 100m;
        public decimal ConvertedQuantity => Quantity * Ratio * (SharePercent / 100m);
        public decimal KgCO2e { get; set; }
        public decimal Total_tCO2e => KgCO2e / 1000m;
        public string OriginSource { get; set; } = string.Empty;
        public int AttachedFilesCount { get; set; }

        // [GHG-20] Raw EF origin flags for marking columns R..Y in Fr-04.1
        public bool SrcSelfCollect { get; set; }
        public bool SrcSupplier { get; set; }
        public bool SrcThLciDb { get; set; }
        public bool SrcTgoEf { get; set; }
        public bool SrcThaiRes { get; set; }
        public bool SrcIntDb { get; set; }
        public bool SrcOther { get; set; }
        public bool SrcSubstitute { get; set; }

        // EF Validity & Traceability (H-04, H-07, H-08)
        public eGwpVersion GwpVersion { get; set; } = eGwpVersion.AR5;
        public string GwpVersionDisplay => GwpVersion.ToString();
        public DateTime? EfValidFrom { get; set; }
        public DateTime? EfValidTo { get; set; }
        public string EfValidityStatus { get; set; } = "Valid";
        public string EfRef { get; set; } = string.Empty;

        // [2026-09-23][Claude] หน่วยงานที่หัวข้อกิจกรรมผูกอยู่ (Topic.Level / TargetId) — ใช้เป็น "ที่ตั้ง/ตำแหน่ง" ในรายงาน Word ข้อ 3.2.x
        // snapshot ที่สร้างก่อนวันนี้ไม่มีค่าเหล่านี้ (ค่าว่าง)
        public eTopicLevel TopicLevel { get; set; }
        public int TopicTargetId { get; set; }
        public string LocationTitle { get; set; } = string.Empty;

        // Gas decomposition per unit (H-03)
        public decimal CO2 { get; set; }
        public decimal FossilCH4 { get; set; }
        public decimal CH4 { get; set; }
        public decimal N2O { get; set; }
        public decimal SF6 { get; set; }
        public decimal NF3 { get; set; }
        public decimal HFCs { get; set; }
        public decimal PFCs { get; set; }
        public decimal gwpCO2 { get; set; } = 1m;
        public decimal gwpFossilCH4 { get; set; } = 30m;
        public decimal gwpCH4 { get; set; } = 28m;
        public decimal gwpN2O { get; set; } = 265m;
        public decimal gwpSF6 { get; set; } = 23500m;
        public decimal gwpNF3 { get; set; } = 16100m;
        public decimal gwpHFCs { get; set; } = 1300m;
        public decimal gwpPFCs { get; set; } = 6630m;
    }

    // [2026-09-16][Antigravity] Product item DTO for Fr-01 product amount grid (#, Product Name, Product Amount, Unit)
    public class CfoProductItemDto
    {
        public int Index { get; set; } = 1;
        public string ProductName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Unit { get; set; } = "Tonnes";
    }

    // [2026-09-17][Claude] แถวประเมินความมีนัยสำคัญ Scope 3 ของชีต Fr-03.2 (แถว 19..33 = Category 1..15)
    // เกณฑ์เชิงคุณภาพ (E, F, H, K, N, O) ระบบไม่มีข้อมูล ต้องให้ผู้ใช้กรอกเองผ่าน Wizard
    public class CfoFr032CategoryDto
    {
        public int CategoryNo { get; set; }                     // 1..15 (คอลัมน์ A)
        public string CategoryTitle { get; set; } = string.Empty; // คอลัมน์ B (ตรึงตามเทมเพลต ไม่เขียนทับ)
        public bool HasSource { get; set; }                     // C : องค์กรมีแหล่งปล่อยหมวดนี้หรือไม่ (ประตูด่านแรก)
        public decimal MagnitudePercent { get; set; }           // D : % ของ Scope 3 ทั้งหมด (ระบบคำนวณให้ ผู้ใช้แก้ไม่ได้)
        public int? InfluenceScore { get; set; }                // E : ระดับ 1..5
        public int? RiskScore { get; set; }                     // F : ระดับ 1..5
        public int? SectorGuidanceScore { get; set; }           // H : ระดับ 1..5
        public int? OutsourcingScore { get; set; }              // K : ระดับ 1..5
        public int? EngagementScore { get; set; }               // N : ระดับ 1..5
        public string? Remark { get; set; }                     // O : หมายเหตุ

        // [2026-09-18][Claude] true = คะแนนอย่างน้อยหนึ่งเกณฑ์มาจากค่าเริ่มต้นของระบบ (ผู้ใช้ยังไม่ได้ทบทวน)
        public bool IsAutoScored { get; set; }
    }


    /// <summary>
    /// [2026-09-17][Claude] เกณฑ์ให้คะแนนความมีนัยสำคัญ Scope 3 ของชีต Fr-03.2
    ///
    /// ที่มา: เทมเพลต TCFO_R_01 v07 ของ อบก. กำหนด "หัวข้อที่ต้องพิจารณา" ไว้ 6 ข้อ แต่ไม่ได้กำหนดน้ำหนัก
    /// หรือเส้นตัดใด ๆ (ช่อง A35 เปิดให้องค์กรอธิบายเกณฑ์ของตนเอง) เกณฑ์ชุดนี้จึงเป็นเกณฑ์ภายในของระบบ ACAMP
    /// ที่ออกแบบให้สอดคล้องกับแนวคิดของ ISO 14064-1:2018 (Annex H) และ GHG Protocol Scope 3 Standard
    /// ซึ่งให้น้ำหนักกับ "ขนาดการปล่อย" และ "อำนาจในการลด" มากที่สุด — องค์กรปรับน้ำหนักได้ แต่ต้องประกาศในรายงาน
    /// </summary>
    public static class Fr032Rubric
    {
        // น้ำหนักรวม = 1.00 (ห้ามแก้ให้ผลรวมเพี้ยน มิฉะนั้นคะแนนเต็มจะไม่เท่ากับ 5.00)
        public const decimal WeightMagnitude  = 0.30m; // D : ขนาดการปล่อย — ระบบคำนวณจากข้อมูลจริง
        public const decimal WeightInfluence  = 0.20m; // E : ระดับอำนาจในการลด
        public const decimal WeightRisk       = 0.20m; // F : ความเสี่ยง / โอกาสทางธุรกิจ
        public const decimal WeightSector     = 0.15m; // H : แนวทางรายสาขาอุตสาหกรรม
        public const decimal WeightOutsource  = 0.10m; // K : การจ้างงานภายนอกในกิจกรรมหลัก
        public const decimal WeightEngagement = 0.05m; // N : การมีส่วนร่วมของพนักงาน

        /// <summary>คะแนนถ่วงน้ำหนักตั้งแต่ระดับนี้ขึ้นไป ถือว่า "มีนัยสำคัญ"</summary>
        public const decimal SignificanceCutoff = 3.00m;

        /// <summary>ระดับคะแนนที่ถือว่าเข้าเงื่อนไขของช่องติ๊ก (คอลัมน์ K และ N)</summary>
        public const int TickScoreThreshold = 4;

        /// <summary>
        /// แปลง % ของ Scope 3 เป็นระดับ 1..5 โดยอิงเกณฑ์นัยสำคัญ (T) ที่องค์กรประกาศไว้ในหน้า Setup
        /// 1: &lt; 0.2T | 2: 0.2T–&lt;T | 3: T–&lt;2T | 4: 2T–&lt;4T | 5: ≥ 4T
        /// (ค่าเริ่มต้น T = 5% จะได้ช่วง &lt;1%, 1–5%, 5–10%, 10–20%, ≥20%)
        /// </summary>
        public static int GetMagnitudeScore(decimal magnitudePercent, decimal thresholdPercent)
        {
            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;

            if (magnitudePercent >= 4m * t) return 5;
            if (magnitudePercent >= 2m * t) return 4;
            if (magnitudePercent >= t) return 3;
            if (magnitudePercent >= 0.2m * t) return 2;
            return 1;
        }

        /// <summary>
        /// คำนวณคะแนนถ่วงน้ำหนัก (1.00–5.00) และผลตัดสิน
        /// - หมวดที่ไม่มีแหล่งปล่อย (HasSource = false) ไม่ประเมิน
        /// - เกณฑ์ข้อที่ผู้ใช้ยังไม่เลือก จะถูกตัดออกแล้ว normalize น้ำหนักที่เหลือ เพื่อไม่ให้คะแนนต่ำเกินจริง
        /// - กฎบังคับ: ถ้า Magnitude ≥ เกณฑ์นัยสำคัญ ให้ถือว่ามีนัยสำคัญเสมอ แม้คะแนนรวมจะไม่ถึงเส้นตัด
        /// </summary>
        public static Fr032EvaluationResult Evaluate(CfoFr032CategoryDto item, decimal thresholdPercent)
        {
            if (!item.HasSource)
            {
                return new Fr032EvaluationResult { IsAssessed = false };
            }

            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;
            int magnitudeScore = GetMagnitudeScore(item.MagnitudePercent, t);

            decimal weightedSum = WeightMagnitude * magnitudeScore;
            decimal usedWeight = WeightMagnitude;
            bool isComplete = true;

            void Accumulate(int? score, decimal weight)
            {
                if (score.HasValue)
                {
                    weightedSum += weight * score.Value;
                    usedWeight += weight;
                }
                else
                {
                    isComplete = false;
                }
            }

            Accumulate(item.InfluenceScore, WeightInfluence);
            Accumulate(item.RiskScore, WeightRisk);
            Accumulate(item.SectorGuidanceScore, WeightSector);
            Accumulate(item.OutsourcingScore, WeightOutsource);
            Accumulate(item.EngagementScore, WeightEngagement);

            decimal score = usedWeight > 0m ? Math.Round(weightedSum / usedWeight, 2, MidpointRounding.AwayFromZero) : 0m;
            bool forcedByMagnitude = item.MagnitudePercent >= t;

            return new Fr032EvaluationResult
            {
                IsAssessed = true,
                IsComplete = isComplete,
                MagnitudeScore = magnitudeScore,
                WeightedScore = score,
                ForcedByMagnitude = forcedByMagnitude,
                IsSignificant = forcedByMagnitude || score >= SignificanceCutoff
            };
        }

        /// <summary>
        /// [2026-09-18][Claude] ค่าเริ่มต้นของ 5 เกณฑ์เชิงคุณภาพรายหมวด (Influence, Risk, Sector, Outsourcing, Engagement)
        ///
        /// ที่มา: ระบบไม่มีข้อมูลเชิงคุณภาพของแต่ละองค์กร ค่าชุดนี้จึงเป็น "ค่าตั้งต้นกลาง" ของ ACAMP
        /// ที่ให้คะแนนตามลักษณะทั่วไปของแต่ละหมวดตาม GHG Protocol Scope 3 (เช่น หมวดที่องค์กรกำหนด
        /// เงื่อนไขจัดซื้อได้เองจะได้ Influence สูง, หมวดที่ผูกกับกฎระเบียบ/ลูกค้าจะได้ Risk สูง)
        /// ไม่ใช่ค่าที่ อบก. หรือมาตรฐานใดกำหนด — ผู้ใช้ต้องทบทวนและแก้ให้ตรงกับบริบทองค์กรก่อนยื่น
        /// ระบบจะติดธง IsAutoScored และเขียนกำกับไว้ในคอลัมน์ Remark ของฟอร์มเสมอ
        /// </summary>
        private static readonly Dictionary<int, int[]> DefaultScoreProfiles = new()
        {
            //                      E  F  H  K  N
            [1]  = new[] { 3, 4, 4, 3, 2 }, // Purchased goods and services
            [2]  = new[] { 2, 2, 2, 2, 1 }, // Capital goods
            [3]  = new[] { 4, 3, 4, 1, 3 }, // Fuel- and energy related activities
            [4]  = new[] { 3, 3, 3, 4, 2 }, // Upstream transportation and distribution
            [5]  = new[] { 4, 2, 3, 4, 4 }, // Waste generated in operations
            [6]  = new[] { 5, 2, 2, 1, 5 }, // Business travel
            [7]  = new[] { 3, 1, 2, 1, 5 }, // Employee commuting
            [8]  = new[] { 3, 2, 2, 2, 2 }, // Upstream leased assets
            [9]  = new[] { 2, 3, 3, 4, 1 }, // Downstream transportation and distribution
            [10] = new[] { 2, 3, 3, 3, 1 }, // Processing of sold products
            [11] = new[] { 2, 5, 4, 1, 1 }, // Use of sold products
            [12] = new[] { 2, 3, 3, 2, 1 }, // End-of-life treatment of sold products
            [13] = new[] { 2, 2, 2, 2, 1 }, // Downstream leased assets
            [14] = new[] { 2, 2, 2, 3, 1 }, // Franchises
            [15] = new[] { 1, 4, 3, 1, 1 }, // Investments
        };

        // [2026-09-25][Antigravity] รายชื่อ 15 หมวดมาตรฐาน Scope 3 สำหรับ Fr-03.2
        public static readonly string[] CategoryTitles =
        {
            "Purchased goods and services",
            "Capital goods",
            "Fuel- and energy related activities",
            "Upstream transportation and distribution",
            "Waste generated in operations",
            "Business travel",
            "Employee commuting",
            "Upstream leased assets",
            "Downstream transportation and distribution",
            "Processing of sold products",
            "Use of sold products",
            "End-of-life treatment of sold products",
            "Downstream leased assets",
            "Franchises",
            "Investments"
        };

        /// <summary>
        /// [2026-09-25][Antigravity] สร้างรายการเริ่มต้น 15 หมวด Scope 3 พร้อมชื่อหมวดมาตรฐาน
        /// </summary>
        public static List<CfoFr032CategoryDto> CreateDefaultCategories()
        {
            return Enumerable.Range(1, 15).Select(i => new CfoFr032CategoryDto
            {
                CategoryNo = i,
                CategoryTitle = CategoryTitles[i - 1]
            }).ToList();
        }

        /// <summary>
        /// [2026-09-18][Claude] ให้คะแนนอัตโนมัติเฉพาะเกณฑ์ที่ผู้ใช้ยังไม่ได้เลือก (ค่าที่ผู้ใช้กรอกเองมาก่อนเสมอ)
        /// - ใช้ค่าตั้งต้นรายหมวดจาก DefaultScoreProfiles
        /// - ปรับตามข้อมูลจริง: หมวดที่ Magnitude ตั้งแต่ 2 เท่าของเกณฑ์นัยสำคัญขึ้นไป ให้บวก Risk อีก 1 ระดับ (สูงสุด 5)
        /// - หมวดที่ไม่มีแหล่งปล่อย ไม่ให้คะแนน
        /// </summary>
        public static void ApplyAutoScores(IEnumerable<CfoFr032CategoryDto>? items, decimal thresholdPercent)
        {
            if (items == null) return;

            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;

            foreach (var item in items)
            {
                if (!item.HasSource) continue;
                if (!DefaultScoreProfiles.TryGetValue(item.CategoryNo, out var profile)) continue;

                int riskDefault = item.MagnitudePercent >= 2m * t
                    ? Math.Min(5, profile[1] + 1)
                    : profile[1];

                bool filled = false;

                int? Fill(int? current, int fallback)
                {
                    if (current.HasValue) return current;
                    filled = true;
                    return fallback;
                }

                item.InfluenceScore      = Fill(item.InfluenceScore, profile[0]);
                item.RiskScore           = Fill(item.RiskScore, riskDefault);
                item.SectorGuidanceScore = Fill(item.SectorGuidanceScore, profile[2]);
                item.OutsourcingScore    = Fill(item.OutsourcingScore, profile[3]);
                item.EngagementScore     = Fill(item.EngagementScore, profile[4]);

                if (filled) item.IsAutoScored = true;
            }
        }

        /// <summary>
        /// [2026-09-18][Claude] เติมค่า Magnitude จากผลคำนวณจริง แล้วติ๊ก "มีแหล่งปล่อย" ให้อัตโนมัติ
        /// ทุกหมวดที่มีการปล่อยมากกว่า 0 (ครอบคลุมกรณี Magnitude เกินเกณฑ์นัยสำคัญด้วย)
        /// ใช้ร่วมกันทั้งหน้า Wizard และตอนเขียนไฟล์ เพื่อให้ผลบนจอกับในไฟล์ตรงกันเสมอ
        /// </summary>
        public static void ApplyAutoSource(IEnumerable<CfoFr032CategoryDto>? items, IReadOnlyDictionary<int, decimal> magnitudePercents)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                item.MagnitudePercent = magnitudePercents.TryGetValue(item.CategoryNo, out var percent) ? percent : 0m;
                if (item.MagnitudePercent > 0m) item.HasSource = true;
            }
        }

        /// <summary>
        /// [2026-09-17][Claude] หนึ่งแถวของ matrix เกณฑ์ (6 เกณฑ์ x ระดับ 1..5)
        /// ใช้ร่วมกันทั้งหน้า Wizard และการเขียนลงชีต Fr-03.2 ใต้ช่อง A35
        /// </summary>
        public class Fr032CriterionRow
        {
            public string Criterion { get; set; } = string.Empty;  // ชื่อเกณฑ์
            public string Column { get; set; } = string.Empty;     // คอลัมน์ในฟอร์ม Fr-03.2
            public decimal Weight { get; set; }                    // น้ำหนัก 0..1
            public string[] Levels { get; set; } = new string[5];  // index 0 = ระดับ 1 ... index 4 = ระดับ 5
        }

        /// <summary>หัวเรื่องของตารางเกณฑ์ (บรรทัดเดียว)</summary>
        /// <summary>
        /// [2026-09-18][Claude] เซลล์ E, F, H ของเทมเพลต อบก. เป็นรูปแบบ 0.00% ระบบจึงบันทึกระดับ 1-5
        /// เป็น % ของคะแนนเต็ม ทุกที่ที่แสดงระดับคะแนนต้องกำกับ % เดียวกันนี้ ไม่ให้หน้าจอกับไฟล์อ่านคนละภาษา
        /// </summary>
        public static int ScoreToPercent(int level) => Math.Clamp(level, 1, 5) * 20;

        /// <summary>ป้ายระดับคะแนนพร้อม % ที่จะถูกบันทึกลงฟอร์ม เช่น "ระดับ 3 (60%)"</summary>
        public static string GetScoreLabel(int level) => $"ระดับ {level} ({ScoreToPercent(level)}%)";

        /// <summary>คะแนนถ่วงน้ำหนัก (1.00-5.00) คิดเป็น % ของคะแนนเต็ม ใช้สื่อสารให้ตรงกับคอลัมน์ E/F/H ในฟอร์ม</summary>
        public static decimal WeightedScoreToPercent(decimal weightedScore) => Math.Round(weightedScore * 20m, 0, MidpointRounding.AwayFromZero);

        /// <summary>เส้นตัด "มีนัยสำคัญ" คิดเป็น % ของคะแนนเต็ม (3.00/5.00 = 60%)</summary>
        public static decimal SignificanceCutoffPercent => WeightedScoreToPercent(SignificanceCutoff);

        public static string GetRubricHeadline(decimal thresholdPercent)
        {
            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;
            return $"เกณฑ์การประเมินความมีนัยสำคัญ Scope 3 (ACAMP Weighted Scoring) — ให้คะแนนแต่ละเกณฑ์เป็นระดับ 1–5 ซึ่งบันทึกลงคอลัมน์ E, F, H ของฟอร์มเป็น % ของคะแนนเต็ม (ระดับ 1 = 20%, 2 = 40%, 3 = 60%, 4 = 80%, 5 = 100%) แล้วรวมแบบถ่วงน้ำหนัก คะแนนเต็ม 5.00 = 100% | เกณฑ์นัยสำคัญขององค์กร T = {t:0.##}% ของการปล่อย Scope 3";
        }

        /// <summary>
        /// matrix เกณฑ์ประเมิน: แถว = 6 เกณฑ์ตามที่เทมเพลตกำหนด, คอลัมน์ = ระดับคะแนน 1..5
        /// </summary>
        public static List<Fr032CriterionRow> GetRubricMatrix(decimal thresholdPercent)
        {
            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;

            return new List<Fr032CriterionRow>
            {
                new Fr032CriterionRow
                {
                    Criterion = "ขนาดการปล่อย (Magnitude) — ระบบคำนวณอัตโนมัติ",
                    Column = "D",
                    Weight = WeightMagnitude,
                    Levels = new[]
                    {
                        $"< {0.2m * t:0.##}%",
                        $"{0.2m * t:0.##}% – < {t:0.##}%",
                        $"{t:0.##}% – < {2m * t:0.##}%",
                        $"{2m * t:0.##}% – < {4m * t:0.##}%",
                        $"≥ {4m * t:0.##}%"
                    }
                },
                new Fr032CriterionRow
                {
                    Criterion = "อำนาจในการลด (Level of influence)",
                    Column = "E",
                    Weight = WeightInfluence,
                    Levels = new[]
                    {
                        "ไม่มีอำนาจควบคุม ไม่มีข้อมูลคู่ค้า",
                        "มีข้อมูลบางส่วน แต่ลดจริงไม่ได้",
                        "ต่อรองผ่านสัญญาได้บางส่วน",
                        "กำหนดเงื่อนไขจัดซื้อ/ปฏิบัติการได้โดยตรง",
                        "ควบคุมเต็มที่ ลดได้ในรอบรายงานถัดไป"
                    }
                },
                new Fr032CriterionRow
                {
                    Criterion = "ความเสี่ยง/โอกาส (Risk or opportunity)",
                    Column = "F",
                    Weight = WeightRisk,
                    Levels = new[]
                    {
                        "ไม่มีผลต่อธุรกิจ",
                        "มีผลทางอ้อม ระยะยาว",
                        "มีผลต่อชื่อเสียงหรือลูกค้าบางราย",
                        "มีผลต่อกฎระเบียบหรือต้นทุน (CBAM ภาษีคาร์บอน) หรือลูกค้าหลัก",
                        "เป็นเงื่อนไขธุรกิจทันที (แพ้ประมูล เสียลูกค้า ผิดกฎหมาย)"
                    }
                },
                new Fr032CriterionRow
                {
                    Criterion = "แนวทางรายสาขา (Sector guidance)",
                    Column = "H",
                    Weight = WeightSector,
                    Levels = new[]
                    {
                        "ไม่ถูกกล่าวถึง",
                        "กล่าวถึงเป็นทางเลือก",
                        "แนะนำให้รายงานหากมีข้อมูล",
                        "แนะนำชัดเจนสำหรับอุตสาหกรรมนี้",
                        "บังคับ/เป็นหมวดหลักตามแนวทางหรือข้อกำหนด อบก."
                    }
                },
                new Fr032CriterionRow
                {
                    Criterion = "การจ้างภายนอก (Outsourcing)",
                    Column = "K",
                    Weight = WeightOutsource,
                    Levels = new[]
                    {
                        "ไม่มีการจ้างภายนอก",
                        "จ้างเป็นครั้งคราว ไม่ใช่กิจกรรมหลัก",
                        "จ้างประจำ แต่เป็นกิจกรรมสนับสนุน",
                        "จ้างในกิจกรรมหลักบางส่วน",
                        "กิจกรรมหลักถูกจ้างภายนอกทั้งหมด"
                    }
                },
                new Fr032CriterionRow
                {
                    Criterion = "การมีส่วนร่วมของพนักงาน (Employee engagement)",
                    Column = "N",
                    Weight = WeightEngagement,
                    Levels = new[]
                    {
                        "พนักงานไม่เกี่ยวข้อง",
                        "เกี่ยวข้องทางอ้อม สื่อสารได้ยาก",
                        "สื่อสาร/รณรงค์ได้ ยังไม่มีตัวชี้วัด",
                        "มีโครงการมีส่วนร่วมพร้อมตัวชี้วัด",
                        "มีส่วนร่วมโดยตรงและวัดผลได้"
                    }
                }
            };
        }

        /// <summary>บรรทัดสรุปใต้ตาราง: สูตร กฎการตัดสิน และวิธีบันทึกลงแบบฟอร์ม</summary>
        public static List<string> GetRubricFooterLines(decimal thresholdPercent)
        {
            decimal t = thresholdPercent > 0m ? thresholdPercent : 5.0m;

            return new List<string>
            {
                "สูตร: คะแนนถ่วงน้ำหนัก = ผลรวม (น้ำหนักเกณฑ์ x ระดับคะแนน) หารด้วย ผลรวมน้ำหนักของเกณฑ์ที่ให้คะแนนแล้ว (normalize เพื่อไม่ให้หมวดที่ยังให้คะแนนไม่ครบถูกกดคะแนนต่ำเกินจริง)",
                $"การตัดสิน: หมวดที่ไม่มีแหล่งปล่อย (ไม่ติ๊กคอลัมน์ C) ไม่ประเมิน | คะแนนถ่วงน้ำหนักตั้งแต่ {SignificanceCutoffPercent:0}% ({SignificanceCutoff:0.00}/5.00) ขึ้นไป = มีนัยสำคัญ | หมวดที่ Magnitude ตั้งแต่ {t:0.##}% ขึ้นไป = มีนัยสำคัญเสมอ แม้คะแนนรวมไม่ถึงเส้นตัด",
                $"การบันทึกลงแบบฟอร์ม: เซลล์ E, F, H ของเทมเพลต อบก. เป็นรูปแบบ 0.00% จึงบันทึกระดับคะแนนเป็น % ของคะแนนเต็ม (ระดับ 1 = 20%, 2 = 40%, 3 = 60%, 4 = 80%, 5 = 100%) ส่วนคอลัมน์ D เป็น % ของการปล่อย Scope 3 จริง ไม่ใช่ระดับคะแนน | คอลัมน์ C ติ๊กเมื่อองค์กรมีแหล่งปล่อยในหมวดนั้น (ระบบติ๊กอัตโนมัติเมื่อพบข้อมูลกิจกรรมใน Fr-04.1) ส่วนผลการตัดสินมีนัยสำคัญดูที่คอลัมน์ Remark | คอลัมน์ K และ N ติ๊กเมื่อได้ระดับตั้งแต่ {TickScoreThreshold} ขึ้นไป",
                "การให้คะแนนอัตโนมัติ: เกณฑ์ E, F, H, K, N ที่องค์กรยังไม่ได้ประเมิน ระบบจะเติมค่าตั้งต้นกลางตามลักษณะทั่วไปของหมวดนั้น (และบวก Risk อีก 1 ระดับเมื่อ Magnitude ตั้งแต่ 2 เท่าของเกณฑ์นัยสำคัญ) ค่าที่องค์กรกรอกเองจะถูกใช้ก่อนเสมอ และแถวที่ยังใช้ค่าตั้งต้นจะถูกกำกับไว้ในคอลัมน์ Remark",
                "ที่มา: เทมเพลต TCFO_R_01 v07 ของ อบก. กำหนดเฉพาะหัวข้อที่ต้องพิจารณา 6 ข้อ ไม่ได้กำหนดน้ำหนัก สูตรรวม หรือเส้นตัด น้ำหนักและเส้นตัดข้างต้นจึงเป็นเกณฑ์ภายในขององค์กร อ้างอิงแนวคิดจาก ISO 14064-1:2018 (Annex H) และ GHG Protocol Corporate Value Chain (Scope 3) Standard"
            };
        }
    }

    /// <summary>[2026-09-17][Claude] ผลการประเมินหนึ่งหมวดของ Fr-03.2</summary>
    public class Fr032EvaluationResult
    {
        public bool IsAssessed { get; set; }        // false = ไม่มีแหล่งปล่อยหมวดนี้
        public bool IsComplete { get; set; }        // ผู้ใช้เลือกครบทั้ง 5 หัวข้อหรือยัง
        public int MagnitudeScore { get; set; }     // 1..5 จากข้อมูลจริง
        public decimal WeightedScore { get; set; }  // 1.00..5.00
        public bool ForcedByMagnitude { get; set; } // เข้าเงื่อนไขบังคับด้วยขนาดการปล่อย
        public bool IsSignificant { get; set; }
    }

    // [2026-09-15][Antigravity] Overrides supplied from AddEditCfoReport tabs before generating the final snapshot
    public class CfoReportOverridesDto
    {
        public string? OrganizationTitle { get; set; }
        public string? GhgTeamName { get; set; }
        public string? CertifierOrgTitle { get; set; }
        public HashSet<int>? Scope3CheckedCategories { get; set; }

        // [2026-09-17][Claude] Fr-03.2 เกณฑ์ประเมินความมีนัยสำคัญรายหมวด (มาก่อน Scope3CheckedCategories ถ้ามีค่า)
        public List<CfoFr032CategoryDto>? Fr032Categories { get; set; }

        // [2026-09-16][Antigravity] Fr-01 Full Sheet Field Overrides
        public DateTime? PreparationDate { get; set; }
        public string? OrganizationImageBase64 { get; set; }
        public string? CalculatePeriodText { get; set; }
        public decimal? CalculateProductionAmount { get; set; }
        public string? CalculateProductionUnit { get; set; }
        public List<CfoProductItemDto>? CalcProductItems { get; set; }
        public string? BasePeriodText { get; set; }
        public decimal? BaseProductionAmount { get; set; }
        public string? BaseProductionUnit { get; set; }
        public List<CfoProductItemDto>? BaseProductItems { get; set; }
        public string? OrgInfoMultiLine { get; set; }
        public string? OrgInfo1 { get; set; }
        public string? OrgInfo2 { get; set; }
        public string? OrgInfo3 { get; set; }
        public string? OrgInfo4 { get; set; }
        public string? OrgInfo5 { get; set; }
        public string? ContactAddress { get; set; }
        public DateTime? RegistrationDate { get; set; }

        // [2026-09-17][Claude] Fr-02 แผนภาพองค์กร: รูปที่ผู้ใช้อัปโหลด (data URL หรือ base64 ล้วน) + วันที่หัวฟอร์ม (M4)
        public string? Fr02ImageBase64 { get; set; }
        public DateTime? Fr02DiagramDate { get; set; }

        // [2026-09-17][Claude] Fr-03.1 แผนภาพโครงสร้างองค์กรและหน้าที่ความรับผิดชอบ: รูป + วันที่หัวฟอร์ม (M4) + วันที่เสร็จสิ้น (K26)
        public string? Fr031ImageBase64 { get; set; }
        public DateTime? Fr031DiagramDate { get; set; }
        public DateTime? Fr031CompletionDate { get; set; }

        // [2026-09-16][Antigravity] Focused sheet tab when opening Excel workbook (defaults to "Fr-01")
        public string? FocusSheetName { get; set; } = "Fr-01";

        // [2026-09-16][Antigravity] Single sheet export: when true, only Fr-01 is exported and all other sheets are excluded (defaults to false to preserve all cross-sheet formula linkages)
        public bool OnlyFr01Sheet { get; set; } = false;
    }
    #endregion

    #region Tenant & Hierarchy Helpers
    /// <summary>
    /// [2026-09-17][Claude] Fail-closed tenant guard: ต้องระบุ allowedSubscriptionIds เสมอ และต้องไม่ว่าง
    /// </summary>
    private bool HasTenantScope(HashSet<int>? allowedSubscriptionIds, string caller)
    {
        if (allowedSubscriptionIds == null)
        {
            _logger.LogError("{Caller} ถูกเรียกโดยไม่ระบุ allowedSubscriptionIds — ปฏิเสธคำขอ (fail-closed)", caller);
            return false;
        }

        return allowedSubscriptionIds.Count > 0;
    }

    /// <summary>
    /// [2026-09-17][Claude] แปลง level/targetId เป็นรายการ Corporate / Company / Facility ที่อยู่ในขอบเขต
    /// (รวมตัวกรอง In-Scope Organizational Boundary) — ใช้ร่วมกันระหว่าง GetAvailableCalculateYearsAsync และ GenerateReportAsync
    /// </summary>
    private static async Task<(List<int> CorpIds, List<int> CompanyIds, List<int> FacilityIds)> ResolveHierarchyAsync(
        ApplicationDbContext context,
        eTopicLevel level,
        int targetId,
        List<int>? inScopeChildIds)
    {
        List<int> corpIds = new();
        List<int> companyIds = new();
        List<int> facilityIds = new();

        bool hasInScopeFilter = inScopeChildIds != null && inScopeChildIds.Any();

        if (level == eTopicLevel.Corporate)
        {
            corpIds.Add(targetId);
            companyIds = await context.Companies
                .Where(c => c.CorporateId == targetId && !c.IsDeleted && (!hasInScopeFilter || inScopeChildIds!.Contains(c.Id)))
                .Select(c => c.Id)
                .ToListAsync();
            facilityIds = await context.Facilities
                .Where(f => companyIds.Contains(f.CompanyId) && !f.IsDeleted)
                .Select(f => f.Id)
                .ToListAsync();
        }
        else if (level == eTopicLevel.Company)
        {
            companyIds.Add(targetId);
            facilityIds = await context.Facilities
                .Where(f => f.CompanyId == targetId && !f.IsDeleted && (!hasInScopeFilter || inScopeChildIds!.Contains(f.Id)))
                .Select(f => f.Id)
                .ToListAsync();
        }
        else if (level == eTopicLevel.Facility)
        {
            facilityIds.Add(targetId);
        }

        return (corpIds, companyIds, facilityIds);
    }

    /// <summary>
    /// [2026-09-17][Claude] เพิ่มข้อความเตือนแบบไม่ซ้ำ เพื่อไม่ให้รายการเตือนโตตามจำนวนรายการกิจกรรม
    /// </summary>
    private static void AddUniqueWarning(List<string> target, string message)
    {
        if (!target.Contains(message))
        {
            target.Add(message);
        }
    }

    /// <summary>
    /// [2026-09-17][Claude] ทำเครื่องหมายว่าไฟล์ที่ export ออกไปไม่ผ่านการตรวจสอบ พร้อมข้อความที่ผู้ใช้เห็นได้บนหน้าจอ
    /// </summary>
    private static void MarkExportUnverified(CfoAuditReportDto report, string message)
    {
        report.ExportVerified = false;
        AddUniqueWarning(report.ExportVerificationMessages, message);
    }
    #endregion

    #region TGO Category Formatting & Sorting Helpers
    /// <summary>
    /// [2026-09-16][Antigravity] Formats an activity group code and title into standard TGO Category notation (Option A, e.g. 1.1 Stationary Combustion, 1.4 Fugitive Emissions).
    /// </summary>
    public static string FormatTgoCategory(string? code, string title, eTgoScopeNo? scopeNo = null)
    {
        if (string.IsNullOrWhiteSpace(title)) return "-";
        title = title.Trim();

        string catPrefix = string.Empty;

        if (!string.IsNullOrWhiteSpace(code))
        {
            var c = code.Trim();

            // Already prefixed with standard category number e.g. "1.4 " or "1.1 "
            if (System.Text.RegularExpressions.Regex.IsMatch(title, @"^\d+\.\d+\s+"))
            {
                return title;
            }

            // Pattern 1: 4-digit code e.g. "0101" -> "1.1", "0104" -> "1.4", "0201" -> "2.1", "0315" -> "3.15"
            if (c.Length == 4 && int.TryParse(c.Substring(0, 2), out int sNum) && int.TryParse(c.Substring(2, 2), out int cNum))
            {
                catPrefix = $"{sNum}.{cNum}";
            }
            // Pattern 2: TGO code e.g. "S1-01" -> "1.1", "S1-04" -> "1.4", "S2-01" -> "2.1", "S3-15" -> "3.15"
            else if (System.Text.RegularExpressions.Regex.IsMatch(c, @"^[sS](\d+)-(\d+)$"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(c, @"^[sS](\d+)-(\d+)$");
                int s = int.Parse(match.Groups[1].Value);
                int n = int.Parse(match.Groups[2].Value);
                catPrefix = $"{s}.{n}";
            }
            // Pattern 3: Already "1.1", "1.4", etc.
            else if (System.Text.RegularExpressions.Regex.IsMatch(c, @"^\d+\.\d+$"))
            {
                catPrefix = c;
            }
        }

        // Fallback: if no prefix from code but scopeNo is provided
        if (string.IsNullOrEmpty(catPrefix) && scopeNo.HasValue && !string.IsNullOrWhiteSpace(code))
        {
            var digits = new string(code.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int d) && d > 0 && d <= 99)
            {
                int s = (int)scopeNo.Value;
                catPrefix = $"{s}.{d}";
            }
        }

        if (!string.IsNullOrEmpty(catPrefix))
        {
            return $"{catPrefix} {title}";
        }

        return string.IsNullOrWhiteSpace(code) || code == "-" ? title : $"{code} {title}";
    }

    /// <summary>
    /// [2026-09-16][Antigravity] Produces a sortable key for group codes (e.g. "0101" -> "000101", "S1-04" -> "000104") to ensure ascending order.
    /// </summary>
    public static string GetSortableCodeKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "999999";
        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int n))
        {
            return n.ToString("D6");
        }
        return code;
    }
    #endregion

    #region Calculation Methods
    /// <summary>
    /// [2026-09-16][Antigravity] ดึงรายการปีปฏิทินที่มีข้อมูลกิจกรรมจริง (TbActivityEvent) ขององค์กรที่ระบุ
    /// เพื่อใช้แสดงผลใน DropDown Calculate Year บนหน้า Setup (ไม่นำปีที่ไม่มีข้อมูลกิจกรรมมาใส่ใน list)
    /// </summary>
    public async Task<List<int>> GetAvailableCalculateYearsAsync(
        eTopicLevel level,
        int targetId,
        HashSet<int>? allowedSubscriptionIds,
        List<int>? inScopeChildIds = null)
    {
        // [2026-09-17][Claude] Fail-closed tenant isolation: ไม่ส่ง allowedSubscriptionIds มา = ไม่คืนข้อมูลใด ๆ
        if (!HasTenantScope(allowedSubscriptionIds, nameof(GetAvailableCalculateYearsAsync)) || targetId <= 0)
        {
            return new List<int>();
        }

        using var context = await _dbFactory.CreateDbContextAsync();

        // 1. Resolve Organizational Hierarchy
        var (corpIds, companyIds, facilityIds) = await ResolveHierarchyAsync(context, level, targetId, inScopeChildIds);

        // 2. Fetch Topic IDs under hierarchy that are active
        var topicIds = await context.Topics
            .Where(t => !t.IsDeleted &&
                 allowedSubscriptionIds!.Contains(t.SubscriptionId) &&
                 ((t.Level == eTopicLevel.Corporate && corpIds.Contains(t.TargetId)) ||
                  (t.Level == eTopicLevel.Company && companyIds.Contains(t.TargetId)) ||
                  (t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId))))
            .Select(t => t.Id)
            .ToListAsync();

        if (!topicIds.Any())
        {
            return new List<int>();
        }

        // 3. Query distinct years from non-deleted activity events
        var years = await context.Events
            .Where(e => !e.IsDeleted &&
                        allowedSubscriptionIds!.Contains(e.SubscriptionId) &&
                        topicIds.Contains(e.TopicId) &&
                        e.TimeStamp.Year >= 2000)
            .Select(e => e.TimeStamp.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return years;
    }

    /// <summary>
    /// [GHG-14] สร้างรายงาน CFO Audit Report พร้อมระบบ Tenant Isolation (allowedSubscriptionIds)
    /// และ [GHG-12] ตรวจสอบช่วงอายุ EF เทียบวันสิ้นรอบรายงาน
    /// </summary>
    public async Task<CfoAuditReportDto> GenerateReportAsync(
        eTopicLevel level,
        int targetId,
        int year,
        HashSet<int>? allowedSubscriptionIds,
        decimal thresholdPercent = 5.0m,
        string? expiryAcknowledgmentReason = null,
        List<int>? inScopeChildIds = null)
    {
        using var context = await _dbFactory.CreateDbContextAsync();

        var report = new CfoAuditReportDto
        {
            Level = level,
            Year = year,
            MaterialityThreshold = thresholdPercent,
            ExpiryAcknowledgmentReason = expiryAcknowledgmentReason
        };

        // [GHG-14] Fail-closed security: TargetId must be specified and allowedSubscriptionIds must be a non-empty set
        // [2026-09-17][Claude] null = ไม่ระบุ tenant scope ถือเป็น fail-closed (เดิมแปลว่า "ไม่กรอง" ซึ่งเปิดข้ามองค์กรได้)
        if (!HasTenantScope(allowedSubscriptionIds, nameof(GenerateReportAsync)) || targetId == 0)
        {
            return report;
        }

        // 1. Resolve Organization Info (Filtered by allowedSubscriptionIds)
        if (level == eTopicLevel.Corporate && targetId > 0)
        {
            var corp = await context.Corporates.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == targetId && !c.IsDeleted && allowedSubscriptionIds!.Contains(c.SubscriptionId));
            if (corp == null) return report; // Fail-closed on tenant mismatch
            report.OrganizationTitle = corp.Title;
            report.OrganizationCode = corp.Code;
        }
        else if (level == eTopicLevel.Company && targetId > 0)
        {
            var comp = await context.Companies.Include(c => c.Corporate).AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == targetId && !c.IsDeleted && allowedSubscriptionIds!.Contains(c.Corporate.SubscriptionId));
            if (comp == null) return report; // Fail-closed on tenant mismatch
            report.OrganizationTitle = comp.Title;
            report.OrganizationCode = comp.Code;
        }
        else if (level == eTopicLevel.Facility && targetId > 0)
        {
            var fac = await context.Facilities.Include(f => f.Company).ThenInclude(c => c!.Corporate).AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == targetId && !f.IsDeleted && f.Company != null && f.Company.Corporate != null && allowedSubscriptionIds!.Contains(f.Company.Corporate.SubscriptionId));
            if (fac == null) return report; // Fail-closed on tenant mismatch
            report.OrganizationTitle = fac.Title;
            report.OrganizationCode = fac.Code;
        }

        // 2. Fetch all topic events under hierarchy (Filtered by Tenant & In-Scope Boundary)
        var (corpIds, companyIds, facilityIds) = await ResolveHierarchyAsync(context, level, targetId, inScopeChildIds);

        var topics = await context.Topics
            .Where(t => !t.IsDeleted &&
                 allowedSubscriptionIds!.Contains(t.SubscriptionId) &&
                 ((t.Level == eTopicLevel.Corporate && corpIds.Contains(t.TargetId)) ||
                  (t.Level == eTopicLevel.Company && companyIds.Contains(t.TargetId)) ||
                  (t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId))))
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.ActivityGroup)
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.ActivityUnit)
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Source)
                    .ThenInclude(s => s!.Group)
            .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                .ThenInclude(c => c.Source)
                    .ThenInclude(s => s!.Unit)
            .Include(t => t.Events.Where(e => !e.IsDeleted && allowedSubscriptionIds!.Contains(e.SubscriptionId)))
            // [2026-09-17][Claude] Include หลายคอลเลกชันในคิวรีเดียวทำให้แถวคูณกัน (cartesian explosion) จึงแยกคิวรี
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync();

        // 3. Process line items
        var auditLines = new List<ActivityAuditLineDto>();
        decimal sumScope1Kg = 0m;
        decimal sumScope2Kg = 0m;
        decimal sumScope3Kg = 0m;
        decimal sumSeparateKg = 0m;

        // Gas accumulation dictionaries (per gas: Scope 1, 2, 3 in kgCO2e)
        var gasScope1 = new Dictionary<string, decimal> { ["CO2"] = 0m, ["CH4"] = 0m, ["N2O"] = 0m, ["SF6"] = 0m, ["NF3"] = 0m, ["HFCs"] = 0m, ["PFCs"] = 0m };
        var gasScope2 = new Dictionary<string, decimal> { ["CO2"] = 0m, ["CH4"] = 0m, ["N2O"] = 0m, ["SF6"] = 0m, ["NF3"] = 0m, ["HFCs"] = 0m, ["PFCs"] = 0m };
        var gasScope3 = new Dictionary<string, decimal> { ["CO2"] = 0m, ["CH4"] = 0m, ["N2O"] = 0m, ["SF6"] = 0m, ["NF3"] = 0m, ["HFCs"] = 0m, ["PFCs"] = 0m };

        // [2026-09-23][Claude] ชื่อ Facility / Company / Corporate ที่หัวข้อกิจกรรมผูกอยู่ (LINQ ล้วน, คิวรีละ 1 ครั้ง)
        var topicFacilityIds = topics.Where(t => t.Level == eTopicLevel.Facility).Select(t => t.TargetId).Distinct().ToList();
        var topicCompanyIds = topics.Where(t => t.Level == eTopicLevel.Company).Select(t => t.TargetId).Distinct().ToList();
        var topicCorporateIds = topics.Where(t => t.Level == eTopicLevel.Corporate).Select(t => t.TargetId).Distinct().ToList();

        var facilityTitles = await context.Facilities.AsNoTracking()
            .Where(f => topicFacilityIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);
        var companyTitles = await context.Companies.AsNoTracking()
            .Where(c => topicCompanyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);
        var corporateTitles = await context.Corporates.AsNoTracking()
            .Where(c => topicCorporateIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);

        foreach (var topic in topics)
        {
            var locationTitle = topic.Level switch
            {
                eTopicLevel.Facility => facilityTitles.GetValueOrDefault(topic.TargetId, string.Empty),
                eTopicLevel.Company => companyTitles.GetValueOrDefault(topic.TargetId, string.Empty),
                eTopicLevel.Corporate => corporateTitles.GetValueOrDefault(topic.TargetId, string.Empty),
                _ => string.Empty
            };

            var events = year > 0
                ? topic.Events.Where(e => e.TimeStamp.Year == year).ToList()
                : topic.Events.ToList();

            if (!events.Any()) continue;
            var totalQty = events.Sum(e => e.Quantity);

            // parse attached files count
            int filesCount = 0;
            if (topic.JsonDataFiles != null && topic.JsonDataFiles is System.Text.Json.Nodes.JsonArray jArr)
            {
                filesCount = jArr.Count;
            }

            foreach (var child in topic.Childs)
            {
                if (child.Source == null) continue;

                if (child.ActivityGroup == null && child.Source.Group == null)
                {
                    _logger.LogWarning("Missing ActivityGroup and Source.Group for TopicChild {ChildId}, TopicId {TopicId}. Data quality flag set.", child.Id, topic.Id);
                }

                var tgoScope = child.ActivityGroup?.TgoScopeNo ?? child.Source.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                var ratio = child.UnitConversionRatio ?? 1m;
                var ef = child.Source.EF;
                var kg = EmissionCalculator.CalculateKgCO2e(totalQty, child);

                // Map ISO Category (B-06)
                int isoCat = ResolveIsoCategory(child, topic.Id);

                // [2026-09-15][Antigravity] ACAMP EF Policy: EF validity is evaluated as of current report generation date using TbEmissionSource.IsExpire
                string validityStatus = "Valid";
                if (child.Source.IsExpire)
                {
                    validityStatus = "Expired";
                }
                else if (!child.Source.DateApproved.HasValue && !child.Source.DateExpire.HasValue)
                {
                    validityStatus = "NoValidityDates";
                }

                // [GHG-07] Guard / Pre-flight check: GWP Version != AR5
                if (child.Source.GwpVersion != eGwpVersion.AR5)
                {
                    // [2026-09-17][Claude] รวมเป็นข้อความเดียวต่อแหล่งกำเนิด ไม่ใช่ต่อรายการกิจกรรม เพื่อไม่ให้ list โตไม่จำกัด
                    AddUniqueWarning(report.PreFlightWarnings, $"[GHG-07 GWP Warning] แหล่งกำเนิด '{child.Source.Title}' ใช้ค่า GWP {child.Source.GwpVersion} ซึ่งแตกต่างจากเทมเพลตมาตรฐาน อบก. (AR5)");
                }

                // [GHG-13] Validation check: ActivityUnitId != Source.UnitId with Ratio == 1
                if (child.ActivityUnitId.HasValue && child.Source.UnitId > 0 && child.ActivityUnitId.Value != child.Source.UnitId && ratio == 1m)
                {
                    AddUniqueWarning(report.PreFlightWarnings, $"[GHG-13 Unit Warning] กิจกรรม '{topic.Title}' มีหน่วยกิจกรรม ({child.ActivityUnit?.Title ?? "Custom"}) ไม่ตรงกับหน่วยของ EF ({child.Source.Unit?.Title ?? "EF Unit"}) แต่อัตราส่วน Unit Conversion Ratio ยังคงเป็น 1.0000");
                }

                var line = new ActivityAuditLineDto
                {
                    TopicId = topic.Id,
                    TopicTitle = topic.Title,
                    ActivityGroupId = child.ActivityGroupId,
                    ActivityGroupCode = child.ActivityGroup?.Code ?? "-",
                    ActivityGroupTitle = child.ActivityGroup?.Title ?? topic.Title,
                    ScopeNo = tgoScope,
                    IsoCategoryNo = isoCat,
                    SourceId = child.Source.Id,
                    SourceName = child.Source.Title,
                    GwpVersion = child.Source.GwpVersion,
                    UnitName = child.ActivityUnit?.Title ?? child.Source.Unit?.Title ?? "-",
                    ActivityUnitName = child.ActivityUnit?.Title ?? child.Source.Unit?.Title ?? "-",
                    EfUnitName = child.Source.Unit?.Title ?? "-",
                    Quantity = totalQty,
                    EF = ef,
                    Ratio = ratio,
                    SharePercent = (decimal)child.EmissionSharePercent,
                    KgCO2e = kg,
                    OriginSource = child.Source.TGOEF ? "TGO EF" :
                                   child.Source.THLCIDB ? "TH LCI" :
                                   child.Source.IntDB ? "Int DB" :
                                   child.Source.Supplier ? "Supplier" :
                                   child.Source.SelfCollect ? "Self-Collect" : "Custom",
                    SrcSelfCollect = child.Source.SelfCollect,
                    SrcSupplier = child.Source.Supplier,
                    SrcThLciDb = child.Source.THLCIDB,
                    SrcTgoEf = child.Source.TGOEF,
                    SrcThaiRes = child.Source.ThaiRes,
                    SrcIntDb = child.Source.IntDB,
                    SrcOther = child.Source.Other,
                    SrcSubstitute = child.Source.Substitute,
                    AttachedFilesCount = filesCount,
                    EfValidFrom = child.Source.DateApproved,
                    EfValidTo = child.Source.DateExpire,
                    EfValidityStatus = validityStatus,
                    EfRef = child.Source.Ref ?? string.Empty,
                    TopicLevel = topic.Level,
                    TopicTargetId = topic.TargetId,
                    LocationTitle = locationTitle,
                    CO2 = child.Source.CO2,
                    FossilCH4 = child.Source.FossilCH4,
                    CH4 = child.Source.CH4,
                    N2O = child.Source.N2O,
                    SF6 = child.Source.SF6,
                    NF3 = child.Source.NF3,
                    HFCs = child.Source.HFCs,
                    PFCs = child.Source.PFCs,
                    gwpCO2 = child.Source.gwpCO2 > 0 ? child.Source.gwpCO2 : 1m,
                    gwpFossilCH4 = child.Source.gwpFossilCH4 > 0 ? child.Source.gwpFossilCH4 : 30m,
                    gwpCH4 = child.Source.gwpCH4 > 0 ? child.Source.gwpCH4 : 28m,
                    gwpN2O = child.Source.gwpN2O > 0 ? child.Source.gwpN2O : 265m,
                    gwpSF6 = child.Source.gwpSF6 > 0 ? child.Source.gwpSF6 : 23500m,
                    gwpNF3 = child.Source.gwpNF3 > 0 ? child.Source.gwpNF3 : 16100m,
                    gwpHFCs = child.Source.gwpHFCs > 0 ? child.Source.gwpHFCs : 1300m,
                    gwpPFCs = child.Source.gwpPFCs > 0 ? child.Source.gwpPFCs : 6630m
                };
                auditLines.Add(line);

                // Accumulate scopes
                switch (tgoScope)
                {
                    case eTgoScopeNo.Scope1:
                        sumScope1Kg += kg;
                        AccumulateGases(gasScope1, totalQty, child);
                        break;
                    case eTgoScopeNo.Scope2:
                        sumScope2Kg += kg;
                        AccumulateGases(gasScope2, totalQty, child);
                        break;
                    case eTgoScopeNo.Scope3:
                        sumScope3Kg += kg;
                        AccumulateGases(gasScope3, totalQty, child);
                        break;
                    default:
                        sumSeparateKg += kg;
                        break;
                }
            }
        }

        report.TotalScope1_tCO2e = sumScope1Kg / 1000m;
        report.TotalScope2_tCO2e = sumScope2Kg / 1000m;
        report.TotalScope3_tCO2e = sumScope3Kg / 1000m;
        report.TotalSeparate_tCO2e = sumSeparateKg / 1000m;
        report.AuditLines = auditLines;

        if (auditLines.Any())
        {
            var gwpVersions = auditLines.Select(l => l.GwpVersion).Distinct().ToList();
            if (gwpVersions.Count == 1)
            {
                report.GwpVersion = gwpVersions[0];
            }
        }

        decimal totalGrossKg = sumScope1Kg + sumScope2Kg + sumScope3Kg;

        // Self-reconcile check: sum of 7-gas vs Scope totals (H-01)
        var s1GasSum = gasScope1.Values.Sum();
        var s2GasSum = gasScope2.Values.Sum();
        var s3GasSum = gasScope3.Values.Sum();
        if (Math.Abs(s1GasSum - sumScope1Kg) > 0.01m)
        {
            _logger.LogWarning("Scope 1 gas breakdown sum ({GasSum:N4} kg) does not reconcile with Scope 1 total ({ScopeSum:N4} kg)", s1GasSum, sumScope1Kg);
        }
        if (Math.Abs(s2GasSum - sumScope2Kg) > 0.01m)
        {
            _logger.LogWarning("Scope 2 gas breakdown sum ({GasSum:N4} kg) does not reconcile with Scope 2 total ({ScopeSum:N4} kg)", s2GasSum, sumScope2Kg);
        }
        if (Math.Abs(s3GasSum - sumScope3Kg) > 0.01m)
        {
            _logger.LogWarning("Scope 3 gas breakdown sum ({GasSum:N4} kg) does not reconcile with Scope 3 total ({ScopeSum:N4} kg)", s3GasSum, sumScope3Kg);
        }

        // 4. Build Scope Groups Breakdown
        report.Scopes = BuildScopeBreakdowns(auditLines, totalGrossKg, thresholdPercent);

        // 5. Build ISO Categories Breakdown
        report.IsoCategories = BuildIsoCategoryBreakdowns(auditLines, totalGrossKg);

        // 6. Build Gas-by-Gas Matrix (H-02)
        report.GasInventory = BuildGasInventory(gasScope1, gasScope2, gasScope3, report.TotalGrossEmissions_tCO2e, auditLines);

        return report;
    }

    private int ResolveIsoCategory(TbActivityTopicChild child, int topicId)
    {
        if (child.ActivityGroup != null && (int)child.ActivityGroup.IsoCategoryNo >= 1 && (int)child.ActivityGroup.IsoCategoryNo <= 6)
        {
            return (int)child.ActivityGroup.IsoCategoryNo;
        }

        if (child.Source?.Group != null && (int)child.Source.Group.IsoCategoryNo >= 1 && (int)child.Source.Group.IsoCategoryNo <= 6)
        {
            return (int)child.Source.Group.IsoCategoryNo;
        }

        _logger.LogWarning("Missing ActivityGroup / Source.Group IsoCategoryNo for TopicChild {ChildId}, TopicId {TopicId}. Using fallback.", child.Id, topicId);

        var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
        return scope switch
        {
            eTgoScopeNo.Scope1 => 1,
            eTgoScopeNo.Scope2 => 2,
            eTgoScopeNo.Scope3 => 4,
            _ => 1 // SeparateReport/Biogenic is under Category 1 (Direct GHG emissions and removals) in ISO 14064-1
        };
    }

    private void AccumulateGases(Dictionary<string, decimal> target, decimal qty, TbActivityTopicChild child)
    {
        var s = child.Source;
        if (s == null) return;

        var ratio = child.UnitConversionRatio ?? 1m;
        var share = (decimal)child.EmissionSharePercent / 100m;
        var baseFactor = qty * ratio * share;

        // Accumulate each gas in kgCO2e with respective GWP (H-01)
        target["CO2"] += baseFactor * s.CO2 * (decimal)s.gwpCO2;
        target["CH4"] += baseFactor * (s.CH4 * (decimal)s.gwpCH4 + s.FossilCH4 * (decimal)s.gwpFossilCH4);
        target["N2O"] += baseFactor * s.N2O * (decimal)s.gwpN2O;
        target["SF6"] += baseFactor * s.SF6 * (decimal)s.gwpSF6;
        target["NF3"] += baseFactor * s.NF3 * (decimal)s.gwpNF3;
        target["HFCs"] += baseFactor * s.HFCs * (decimal)s.gwpHFCs;
        target["PFCs"] += baseFactor * s.PFCs * (decimal)s.gwpPFCs;
    }

    private List<ScopeBreakdownDto> BuildScopeBreakdowns(List<ActivityAuditLineDto> lines, decimal totalGrossKg, decimal thresholdPercent)
    {
        var scopes = new[] { eTgoScopeNo.Scope1, eTgoScopeNo.Scope2, eTgoScopeNo.Scope3, (eTgoScopeNo)4 };
        var list = new List<ScopeBreakdownDto>();

        foreach (var s in scopes)
        {
            var sLines = lines.Where(l => l.ScopeNo == s).ToList();
            var scopeTotalKg = sLines.Sum(l => l.KgCO2e);

            var scopeName = s switch
            {
                eTgoScopeNo.Scope1 => "Scope 1 (Direct GHG Emissions)",
                eTgoScopeNo.Scope2 => "Scope 2 (Indirect Energy GHG Emissions)",
                eTgoScopeNo.Scope3 => "Scope 3 (Other Indirect Value Chain GHG Emissions)",
                _ => "Separate Report (Biogenic & Carbon Offsets)"
            };

            var agGroups = sLines.GroupBy(l => new { l.ActivityGroupId, l.ActivityGroupCode, l.ActivityGroupTitle })
                .Select(g =>
                {
                    var gKg = g.Sum(x => x.KgCO2e);
                    var overallShare = totalGrossKg > 0 ? (gKg * 100m / totalGrossKg) : 0m;
                    return new ActivityGroupAuditDto
                    {
                        ActivityGroupId = g.Key.ActivityGroupId,
                        Code = g.Key.ActivityGroupCode,
                        Title = g.Key.ActivityGroupTitle,
                        TotalKgCO2e = gKg,
                        ScopeSharePercent = scopeTotalKg > 0 ? (gKg * 100m / scopeTotalKg) : 0m,
                        OverallSharePercent = overallShare,
                        IsMaterialHotspot = overallShare >= thresholdPercent,
                        Topics = g.ToList()
                    };
                }).OrderByDescending(g => g.TotalKgCO2e).ToList();

            list.Add(new ScopeBreakdownDto
            {
                ScopeNo = s,
                ScopeName = scopeName,
                TotalKgCO2e = scopeTotalKg,
                SharePercent = totalGrossKg > 0 ? (scopeTotalKg * 100m / totalGrossKg) : 0m,
                ActivityGroups = agGroups
            });
        }

        return list;
    }

    private List<IsoCategoryBreakdownDto> BuildIsoCategoryBreakdowns(List<ActivityAuditLineDto> lines, decimal totalGrossKg)
    {
        var isoNames = new Dictionary<int, string>
        {
            [1] = "Category 1: Direct GHG emissions and removals",
            [2] = "Category 2: Indirect GHG emissions from imported energy",
            [3] = "Category 3: Indirect GHG emissions from transportation",
            [4] = "Category 4: Indirect GHG emissions from products used by organization",
            [5] = "Category 5: Indirect GHG emissions associated with the use of products from organization",
            [6] = "Category 6: Indirect GHG emissions from other sources"
        };

        var list = new List<IsoCategoryBreakdownDto>();
        for (int cat = 1; cat <= 6; cat++)
        {
            // Cat Gross lines (excluding SeparateReport from Gross category shares)
            var catLines = lines.Where(l => l.IsoCategoryNo == cat && l.ScopeNo != eTgoScopeNo.SeparateReport).ToList();
            var catTotalKg = catLines.Sum(l => l.KgCO2e);

            var agGroups = catLines.GroupBy(l => new { l.ActivityGroupId, l.ActivityGroupCode, l.ActivityGroupTitle })
                .Select(g =>
                {
                    var gKg = g.Sum(x => x.KgCO2e);
                    return new ActivityGroupAuditDto
                    {
                        ActivityGroupId = g.Key.ActivityGroupId,
                        Code = g.Key.ActivityGroupCode,
                        Title = g.Key.ActivityGroupTitle,
                        TotalKgCO2e = gKg,
                        ScopeSharePercent = catTotalKg > 0 ? (gKg * 100m / catTotalKg) : 0m,
                        OverallSharePercent = totalGrossKg > 0 ? (gKg * 100m / totalGrossKg) : 0m,
                        Topics = g.ToList()
                    };
                }).OrderByDescending(g => g.TotalKgCO2e).ToList();

            list.Add(new IsoCategoryBreakdownDto
            {
                CategoryNo = cat,
                CategoryName = isoNames[cat],
                TotalKgCO2e = catTotalKg,
                SharePercent = totalGrossKg > 0 ? (catTotalKg * 100m / totalGrossKg) : 0m,
                ActivityGroups = agGroups
            });
        }

        // [B-06] Self-reconcile: ผลรวม Category 1-6 ต้องเท่ากับ Gross Total (Scope 1-3)
        var catSum = list.Sum(c => c.TotalKgCO2e);
        if (Math.Abs(catSum - totalGrossKg) > 0.01m)
        {
            _logger.LogWarning("ISO Category breakdown sum ({CatSum:N4} kg) does not reconcile with Gross Total ({GrossSum:N4} kg)", catSum, totalGrossKg);
        }

        return list;
    }

    private List<GasInventoryDto> BuildGasInventory(
        Dictionary<string, decimal> s1,
        Dictionary<string, decimal> s2,
        Dictionary<string, decimal> s3,
        decimal totalGross_tCO2e,
        List<ActivityAuditLineDto> lines)
    {
        // [H-02] Calculate actual weighted average GWP for gases from audit lines
        decimal hfcMass = lines.Sum(l => l.ConvertedQuantity * l.HFCs);
        decimal hfcGwpWeighted = lines.Sum(l => l.ConvertedQuantity * l.HFCs * l.gwpHFCs);
        decimal hfcGwp = hfcMass > 0 ? (hfcGwpWeighted / hfcMass) : 1300m;

        decimal pfcMass = lines.Sum(l => l.ConvertedQuantity * l.PFCs);
        decimal pfcGwpWeighted = lines.Sum(l => l.ConvertedQuantity * l.PFCs * l.gwpPFCs);
        decimal pfcGwp = pfcMass > 0 ? (pfcGwpWeighted / pfcMass) : 6630m;

        decimal ch4Mass = lines.Sum(l => l.ConvertedQuantity * (l.CH4 + l.FossilCH4));
        decimal ch4GwpWeighted = lines.Sum(l => l.ConvertedQuantity * (l.CH4 * l.gwpCH4 + l.FossilCH4 * l.gwpFossilCH4));
        decimal ch4Gwp = ch4Mass > 0 ? (ch4GwpWeighted / ch4Mass) : 28m;

        var gases = new[]
        {
            ("Carbon Dioxide", "CO2", 1m),
            ("Methane", "CH4", Math.Round(ch4Gwp, 2)),
            ("Nitrous Oxide", "N2O", 265m),
            ("Sulfur Hexafluoride", "SF6", 23500m),
            ("Nitrogen Trifluoride", "NF3", 16100m),
            ("Hydrofluorocarbons", "HFCs", Math.Round(hfcGwp, 2)),
            ("Perfluorocarbons", "PFCs", Math.Round(pfcGwp, 2))
        };

        var list = new List<GasInventoryDto>();
        foreach (var (name, formula, gwp) in gases)
        {
            var s1_t = s1.GetValueOrDefault(formula, 0m) / 1000m;
            var s2_t = s2.GetValueOrDefault(formula, 0m) / 1000m;
            var s3_t = s3.GetValueOrDefault(formula, 0m) / 1000m;
            var total_t = s1_t + s2_t + s3_t;

            list.Add(new GasInventoryDto
            {
                GasName = name,
                ChemicalFormula = formula,
                GwpFactor = gwp,
                Scope1_tCO2e = s1_t,
                Scope2_tCO2e = s2_t,
                Scope3_tCO2e = s3_t,
                SharePercent = totalGross_tCO2e > 0 ? (total_t * 100m / totalGross_tCO2e) : 0m
            });
        }

        return list;
    }
    #endregion

    #region Official Excel Export
    // [2026-09-17][Claude] ผูกกับ ActivityGroupId ของ seed data โดยตรง ใช้เป็น fallback เท่านั้น
    // [2026-09-18][Claude] รูป default ของระบบ ใช้เมื่อผู้ใช้ไม่แนบรูปในชีตนั้น ๆ (วางไว้ที่ wwwroot/images และ Files/Images)
    private const string DefaultOrgFacilityImage  = "default_org_facility.jpg";   // Fr-01 รูปขององค์กร
    private const string DefaultOrgDiagramImage   = "default_org_diagram.png";    // Fr-02 แผนภาพองค์กร
    private const string DefaultOrgStructureImage = "default_org_structure.png";  // Fr-03.1 แผนภาพโครงสร้างองค์กร

    // [2026-09-25][Antigravity] Internal accessibility for Scope 3 mapping reuse in executive dashboard
    internal static readonly Dictionary<int, int> Scope3ActivityGroupIdToFr032RowMap = new()
    {
        [7] = 19,  // S3-C01 / 0301: Purchased Goods and Services
        [8] = 20,  // S3-C02 / 0302: Capital Goods
        [9] = 21,  // S3-C03 / 0303: Fuel- and Energy-Related Activities
        [10] = 22, // S3-C04 / 0304: Upstream Transportation and Distribution
        [11] = 23, // S3-C05 / 0305: Waste Generated in Operations
        [12] = 24, // S3-C06 / 0306: Business Travel
        [13] = 25, // S3-C07 / 0307: Employee Commuting
        [14] = 26, // S3-C08 / 0308: Upstream Leased Assets
        [15] = 27, // S3-C09 / 0309: Downstream Transportation and Distribution
        [16] = 28, // S3-C10 / 0310: Processing of Sold Products
        [17] = 29, // S3-C11 / 0311: Use of Sold Products
        [18] = 30, // S3-C12 / 0312: End-of-Life Treatment of Sold Products
        [19] = 31, // S3-C13 / 0313: Downstream Leased Assets
        [20] = 32, // S3-C14 / 0314: Franchises
        [21] = 33  // S3-C15 / 0315: Investments
    };

    // [2026-09-17][Claude] ชื่อไฟล์ template ไม่ถูก hardcode อีกต่อไป: อ่านจาก config ก่อน แล้วค่อยสแกนโฟลเดอร์ตามรูปแบบชื่อ
    private const string TemplatePathConfigKey = "CfoReport:Ar5TemplatePath";
    private const string TemplateNameMarker = "AR5";

    // [2026-09-23][Claude] ย้าย logic ค้นเทมเพลตไปไว้ที่ ReportTemplateLocator (ใช้ร่วมกับรายงาน Word) พฤติกรรมเหมือนเดิมทุกขั้น
    private string GetTemplatePath()
        => ReportTemplateLocator.Find(_env, _configuration, _logger, TemplatePathConfigKey, "*.xlsx", TemplateNameMarker);

    public byte[] ExportToExcel(CfoAuditReportDto report)
    {
        return ExportToExcel(report, null, 0, null);
    }

    public byte[] ExportToExcel(CfoAuditReportDto calcReport, CfoAuditReportDto? baseReport, int baseYear, CfoReportOverridesDto? overrides = null)
    {
        // [GHG-07] Guard before export: warn if reporting period has any EF with GwpVersion != AR5
        if (calcReport.AuditLines.Any(l => l.GwpVersion != eGwpVersion.AR5) || calcReport.GwpVersion != eGwpVersion.AR5)
        {
            string gwpWarn = "[GHG-07 GWP Guard Warning] รอบรายงานมี Emission Factor ที่ใช้ GWP Version แตกต่างจาก AR5 (เช่น AR6/AR4) โปรดตรวจสอบดุลการคำนวณในเทมเพลต Excel AR5";
            AddUniqueWarning(calcReport.ExportVerificationMessages, gwpWarn);
            _logger.LogWarning(gwpWarn);
        }

        string templatePath = GetTemplatePath();
        if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
        {
            // [2026-09-17][Claude] เดิมเงียบแล้ว fallback ผู้ใช้ได้ไฟล์คนละฟอร์มโดยไม่รู้ตัว
            string noTemplateMsg = $"[Export] ไม่พบไฟล์ template AR5 (ค้นจาก config '{TemplatePathConfigKey}' และโฟลเดอร์ Files/Template, Files/Excels) ระบบสร้างไฟล์สำรองที่ไม่ใช่แบบฟอร์ม อบก. แทน";
            _logger.LogError(noTemplateMsg);
            MarkExportUnverified(calcReport, noTemplateMsg);
            return GenerateFallbackWorkbook(calcReport);
        }

        {
            try
            {
                using var workbook = new XLWorkbook(templatePath);
                // [2026-09-16][Antigravity] Populate sheets and track Fr-01 vertical row shift
                // [2026-09-25][Gemini] รับ layout ของแถว Fr-04.1/Fr-04.2 ที่ขยายแบบ dynamic เพื่อส่งต่อให้ Verify และ Fr-05
                var (totalShift, calcLayout, baseLayout) = PopulateTgoAr5Workbook(workbook, calcReport, baseReport, baseYear, overrides);

                // [M-03] Force calculation and enable auto calculate mode
                workbook.CalculateMode = XLCalculateMode.Auto;
                try
                {
                    workbook.RecalculateAllFormulas();
                }
                catch (Exception calcEx)
                {
                    _logger.LogWarning(calcEx, "ClosedXML formula pre-evaluation warning; Excel will compute dynamically on open.");
                }

                // [GHG-02] Assert scope totals after formula recalculation against actual AO values
                VerifyWorkbookScopeTotals(workbook, calcReport, baseReport, baseYear, calcLayout, baseLayout);

                // [2026-09-16][Antigravity] Focus and activate the requested sheet (e.g. "Fr-01") while keeping all sheets intact for formula links
                var focusSheetName = !string.IsNullOrWhiteSpace(overrides?.FocusSheetName) ? overrides.FocusSheetName : "Fr-01";
                var targetWs = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(focusSheetName, StringComparison.OrdinalIgnoreCase));
                if (targetWs != null)
                {
                    targetWs.SetTabActive();
                    targetWs.SetTabSelected();
                }

                // [2026-09-16][Antigravity] Only remove other sheets if explicitly requested; otherwise all sheets are kept intact
                if (overrides?.OnlyFr01Sheet == true)
                {
                    var nonFr01Sheets = workbook.Worksheets
                        .Where(w => !w.Name.Equals("Fr-01", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (var s in nonFr01Sheets)
                    {
                        s.Delete();
                    }
                }

                using var ms = new MemoryStream();
                workbook.SaveAs(ms);

                // [2026-09-16][Antigravity] Post-process OpenXML drawings and Chart 4 anchor position & data cache
                AdjustFr01ChartAndDrawings(ms, totalShift, calcReport, overrides);

                return ms.ToArray();
            }
            catch (Exception ex)
            {
                string failMsg = $"[Export] สร้างไฟล์จาก template AR5 ไม่สำเร็จ ({ex.GetType().Name}: {ex.Message}) ระบบสร้างไฟล์สำรองที่ไม่ใช่แบบฟอร์ม อบก. แทน";
                _logger.LogError(ex, "Error exporting TGO AR5 Excel template, falling back to generated audit workbook.");
                MarkExportUnverified(calcReport, failMsg);
            }
        }

        return GenerateFallbackWorkbook(calcReport);
    }

    private (int totalShift, Fr04BlockLayout calcLayout, Fr04BlockLayout? baseLayout) PopulateTgoAr5Workbook(XLWorkbook workbook, CfoAuditReportDto calcReport, CfoAuditReportDto? baseReport, int baseYear, CfoReportOverridesDto? overrides = null)
    {
        // 1. Fr-01: รายละเอียดขององค์กร
        int totalShift = PopulateFr01(workbook, calcReport, baseReport, baseYear, overrides);

        // 2. Fr-02: แผนภาพองค์กร (รูปที่ผู้ใช้อัปโหลด + วันที่หัวฟอร์ม)
        PopulateFr02(workbook, calcReport, overrides);

        // 3. Fr-03.1: แผนภาพโครงสร้างองค์กรและหน้าที่ความรับผิดชอบ
        PopulateFr031(workbook, calcReport, overrides);

        // 4. Fr-03.2: ประเมินความมีนัยสำคัญ Scope 3 (15 Categories)
        PopulateFr032(workbook, calcReport, overrides);

        // 5. Fr-04.1: บัญชีรายการข้อมูลกิจกรรมและปริมาณการปล่อย GHG (ปีคำนวณ)
        // [2026-09-25][Gemini] ขยายแถวแบบ dynamic เพื่อบรรจุรายการกิจกรรม 100% โดยไม่ยุบรวม
        var calcLayout = PopulateFr04Sheet(workbook, "Fr-04.1", calcReport, calcReport.Year);

        // 6. Fr-04.2: บัญชีรายการข้อมูลกิจกรรมและปริมาณการปล่อย GHG (ปีฐาน) (B-04)
        Fr04BlockLayout? baseLayout = null;
        if (baseReport != null && baseReport.AuditLines.Any())
        {
            baseLayout = PopulateFr04Sheet(workbook, "Fr-04.2", baseReport, baseYear > 0 ? baseYear : baseReport.Year);

            // [2026-09-18][Claude] คอลัมน์ AS ของ Fr-04.2 = "ระบุความแตกต่างระหว่างการรายงานปริมาณ
            // ก๊าซเรือนกระจกของปีฐานและปีปัจจุบัน" — เขียนข้อความสรุปรายแถวจากการเทียบกับปีรายงาน
            var wsBase = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-04.2", StringComparison.OrdinalIgnoreCase));
            if (wsBase != null) WriteFr042DiffColumn(wsBase, baseReport, calcReport, baseLayout);
        }
        else
        {
            ClearFr04Sheet(workbook, "Fr-04.2", baseYear);
        }

        // 7. Fr-05: สรุปผลการประเมิน (ปล่อยให้สูตร =ROUNDUP('Fr-04.1'...) ทำงานอัตโนมัติ)
        //    ยกเว้น Carbon intensity ที่ต้องกัน #DIV/0! เมื่อผลผลิตเป็น 0
        PopulateFr05(workbook, calcLayout, baseLayout, overrides);

        // 6. Activity Data Audit Registry: ทะเบียนข้อมูลกิจกรรมแบบละเอียดสำหรับการทวนสอบ
        PopulateAuditRegistrySheet(workbook, calcReport);

        return (totalShift, calcLayout, baseLayout);
    }

    private int PopulateFr01(XLWorkbook workbook, CfoAuditReportDto calcReport, CfoAuditReportDto? baseReport, int baseYear, CfoReportOverridesDto? overrides = null)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-01", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return 0;

        // [2026-09-16][Antigravity] Apply user overrides for top header fields
        var orgName = !string.IsNullOrWhiteSpace(overrides?.OrganizationTitle) ? overrides.OrganizationTitle : calcReport.OrganizationTitle;
        ws.Cell("B6").Value = orgName;
        ws.Cell("G3").Value = orgName;
        ws.Cell("G4").Value = !string.IsNullOrWhiteSpace(overrides?.GhgTeamName) ? overrides.GhgTeamName : $"{calcReport.LevelDisplay} GHG Team";

        // [2026-09-25][Gemini] ใช้วันที่ปัจจุบัน (AppTimeZone.LocalNow.Date) เป็นค่า default เสมอตามคำสั่ง
        var prepDate = overrides?.PreparationDate ?? AppTimeZone.LocalNow.Date;
        ws.Cell("J4").Value = prepDate.Date;
        ws.Cell("J4").Style.NumberFormat.Format = "dd/MM/yyyy";
        ws.Cell("J4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // [2026-09-16][Antigravity] Format activity as TGO category (Option A) and sort ascending by code with at least one blank row per scope
        List<string> GetScopeTitles(eTgoScopeNo scopeNo)
        {
            var sg = calcReport.Scopes.FirstOrDefault(s => s.ScopeNo == scopeNo);
            if (sg == null || !sg.ActivityGroups.Any()) return new List<string>();
            return sg.ActivityGroups
                .OrderBy(ag => GetSortableCodeKey(ag.Code))
                .Select(ag => FormatTgoCategory(ag.Code, ag.Title, scopeNo))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }

        var s1Titles = GetScopeTitles(eTgoScopeNo.Scope1);
        var s2Titles = GetScopeTitles(eTgoScopeNo.Scope2);
        var s3Titles = GetScopeTitles(eTgoScopeNo.Scope3);

        // 1. Scope 1 (Rows 9..17, capacity: 9 slots, always guarantee at least 1 blank row)
        int s1Capacity = 9;
        int s1StartRow = 9;
        int s1EndRow = 17;
        int s1RequiredRows = s1Titles.Count == 0 ? s1Capacity : Math.Max(s1Capacity, s1Titles.Count + 1);
        int s1Extra = s1RequiredRows - s1Capacity;
        int s1Shift = s1Extra;

        if (s1Extra > 0)
        {
            ws.Row(s1EndRow).InsertRowsBelow(s1Extra);
            var existingMerge = ws.MergedRanges.FirstOrDefault(m => m.RangeAddress.FirstAddress.RowNumber == s1StartRow && m.RangeAddress.FirstAddress.ColumnNumber == 6);
            if (existingMerge != null) ws.MergedRanges.Remove(existingMerge);
            ws.Range(s1StartRow, 6, s1EndRow + s1Extra, 7).Merge();
            ws.Cell("F9").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell("F9").Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        for (int i = 0; i < s1RequiredRows; i++)
        {
            int r = s1StartRow + i;
            if (s1Titles.Count == 0)
            {
                if (i == 0) ws.Cell(r, 8).Value = "-";
                else ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }
            else if (i < s1Titles.Count)
            {
                ws.Cell(r, 8).Value = s1Titles[i];
            }
            else
            {
                ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }

            ws.Cell(r, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(r, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            CleanMerge(ws, r, 8, 10);
            ws.Range(r, 8, r, 10).Merge();
            ws.Range(r, 8, r, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        // 2. Scope 2 (Rows 18..20 + s1Shift, capacity: 3 slots, always guarantee at least 1 blank row)
        int s2Capacity = 3;
        int s2StartRow = 18 + s1Shift;
        int s2EndRow = s2StartRow + s2Capacity - 1;
        int s2RequiredRows = s2Titles.Count == 0 ? s2Capacity : Math.Max(s2Capacity, s2Titles.Count + 1);
        int s2Extra = s2RequiredRows - s2Capacity;
        int s2Shift = s2Extra;

        if (s2Extra > 0)
        {
            ws.Row(s2EndRow).InsertRowsBelow(s2Extra);
            var existingMerge = ws.MergedRanges.FirstOrDefault(m => m.RangeAddress.FirstAddress.RowNumber == s2StartRow && m.RangeAddress.FirstAddress.ColumnNumber == 6);
            if (existingMerge != null) ws.MergedRanges.Remove(existingMerge);
            ws.Range(s2StartRow, 6, s2EndRow + s2Extra, 7).Merge();
            ws.Cell(s2StartRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(s2StartRow, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        for (int i = 0; i < s2RequiredRows; i++)
        {
            int r = s2StartRow + i;
            if (s2Titles.Count == 0)
            {
                if (i == 0) ws.Cell(r, 8).Value = "-";
                else ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }
            else if (i < s2Titles.Count)
            {
                ws.Cell(r, 8).Value = s2Titles[i];
            }
            else
            {
                ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }

            ws.Cell(r, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(r, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            CleanMerge(ws, r, 8, 10);
            ws.Range(r, 8, r, 10).Merge();
            ws.Range(r, 8, r, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        // 3. Scope 3 (Row 21 + s1Shift + s2Shift, initial capacity: 1 slot, always guarantee at least 1 blank row)
        int s3Capacity = 1;
        int s3StartRow = 21 + s1Shift + s2Shift;
        int s3EndRow = s3StartRow;
        int s3RequiredRows = Math.Max(1, s3Titles.Count) + 1;
        int s3Extra = s3RequiredRows - s3Capacity;

        if (s3Extra > 0)
        {
            ws.Row(s3EndRow).InsertRowsBelow(s3Extra);
            var existingMerge = ws.MergedRanges.FirstOrDefault(m => m.RangeAddress.FirstAddress.RowNumber == s3StartRow && m.RangeAddress.FirstAddress.ColumnNumber == 6);
            if (existingMerge != null) ws.MergedRanges.Remove(existingMerge);
            ws.Range(s3StartRow, 6, s3EndRow + s3Extra, 7).Merge();
            ws.Cell(s3StartRow, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(s3StartRow, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        for (int i = 0; i < s3RequiredRows; i++)
        {
            int r = s3StartRow + i;
            if (s3Titles.Count == 0)
            {
                if (i == 0) ws.Cell(r, 8).Value = "-";
                else ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }
            else if (i < s3Titles.Count)
            {
                ws.Cell(r, 8).Value = s3Titles[i];
            }
            else
            {
                ws.Cell(r, 8).Clear(XLClearOptions.Contents);
            }

            ws.Cell(r, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(r, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            CleanMerge(ws, r, 8, 10);
            ws.Range(r, 8, r, 10).Merge();
            ws.Range(r, 8, r, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        // [2026-09-16][Antigravity] Calculate total vertical row shift caused by Scope 1..3 dynamic insertions
        int totalShift = s1Shift + s2Shift + s3Extra;

        // [2026-09-16][Antigravity] Populate shifted rows below Scope 3 directly at their shifted coordinates
        int rPeriod = 22 + totalShift;
        int rCalcProd = 23 + totalShift;
        int rBasePeriod = 24 + totalShift;
        int rBaseProd = 25 + totalShift;
        int rOrgHeader = 26 + totalShift;
        int rOrg1 = 27 + totalShift;

        // Row rPeriod: ระยะเวลาเก็บข้อมูล
        ws.Cell(rPeriod, 6).Value = "ระยะเวลาเก็บข้อมูล";
        CleanMerge(ws, rPeriod, 6, 7);
        ws.Range(rPeriod, 6, rPeriod, 7).Merge();
        CleanMerge(ws, rPeriod, 8, 10);
        ws.Range(rPeriod, 8, rPeriod, 10).Merge();
        ws.Cell(rPeriod, 8).Value = !string.IsNullOrWhiteSpace(overrides?.CalculatePeriodText)
            ? overrides.CalculatePeriodText
            : (calcReport.Year > 0 ? $"1 ม.ค. {calcReport.Year} - 31 ธ.ค. {calcReport.Year}" : "ทุกปีประวัติศาสตร์");
        ws.Cell(rPeriod, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(rPeriod, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(rPeriod, 6, rPeriod, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rPeriod, 8, rPeriod, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Row rCalcProd: ผลผลิต (F:G merged, H:I merged for amount, J for unit)
        ws.Cell(rCalcProd, 6).Value = "ผลผลิต";
        CleanMerge(ws, rCalcProd, 6, 7);
        ws.Range(rCalcProd, 6, rCalcProd, 7).Merge();
        CleanMerge(ws, rCalcProd, 8, 9);
        ws.Range(rCalcProd, 8, rCalcProd, 9).Merge();

        decimal calcProdAmount = 0m;
        string calcProdUnit = "Tonnes";
        if (overrides?.CalcProductItems != null && overrides.CalcProductItems.Any())
        {
            calcProdAmount = overrides.CalcProductItems.Sum(x => x.Amount);
            var units = overrides.CalcProductItems.Select(x => x.Unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            calcProdUnit = units.Any() ? string.Join(", ", units) : (!string.IsNullOrWhiteSpace(overrides.CalculateProductionUnit) ? overrides.CalculateProductionUnit : "Tonnes");
        }
        else if (overrides?.CalculateProductionAmount.HasValue == true)
        {
            calcProdAmount = overrides.CalculateProductionAmount.Value;
            if (!string.IsNullOrWhiteSpace(overrides.CalculateProductionUnit)) calcProdUnit = overrides.CalculateProductionUnit;
        }

        ws.Cell(rCalcProd, 8).Value = calcProdAmount;
        ws.Cell(rCalcProd, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(rCalcProd, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(rCalcProd, 8).Style.NumberFormat.Format = "#,##0.00";

        ws.Cell(rCalcProd, 10).Value = calcProdUnit;
        ws.Cell(rCalcProd, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(rCalcProd, 10).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        ws.Range(rCalcProd, 6, rCalcProd, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rCalcProd, 8, rCalcProd, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Cell(rCalcProd, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Row rBasePeriod: ปีฐาน
        ws.Cell(rBasePeriod, 6).Value = "ปีฐาน";
        CleanMerge(ws, rBasePeriod, 6, 7);
        ws.Range(rBasePeriod, 6, rBasePeriod, 7).Merge();
        CleanMerge(ws, rBasePeriod, 8, 10);
        ws.Range(rBasePeriod, 8, rBasePeriod, 10).Merge();
        ws.Cell(rBasePeriod, 8).Value = !string.IsNullOrWhiteSpace(overrides?.BasePeriodText)
            ? overrides.BasePeriodText
            : (baseYear > 0 ? $"1 ม.ค. {baseYear} - 31 ธ.ค. {baseYear}" : (baseReport != null && baseReport.Year > 0 ? $"1 ม.ค. {baseReport.Year} - 31 ธ.ค. {baseReport.Year}" : "-"));
        ws.Cell(rBasePeriod, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(rBasePeriod, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(rBasePeriod, 6, rBasePeriod, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rBasePeriod, 8, rBasePeriod, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Row rBaseProd: ผลผลิตปีฐาน (F:G merged, H:I merged for amount, J for unit)
        ws.Cell(rBaseProd, 6).Value = "ผลผลิตปีฐาน";
        CleanMerge(ws, rBaseProd, 6, 7);
        ws.Range(rBaseProd, 6, rBaseProd, 7).Merge();
        CleanMerge(ws, rBaseProd, 8, 9);
        ws.Range(rBaseProd, 8, rBaseProd, 9).Merge();

        decimal baseProdAmount = 0m;
        string baseProdUnit = "Tonnes";
        if (overrides?.BaseProductItems != null && overrides.BaseProductItems.Any())
        {
            baseProdAmount = overrides.BaseProductItems.Sum(x => x.Amount);
            var units = overrides.BaseProductItems.Select(x => x.Unit).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
            baseProdUnit = units.Any() ? string.Join(", ", units) : (!string.IsNullOrWhiteSpace(overrides.BaseProductionUnit) ? overrides.BaseProductionUnit : "Tonnes");
        }
        else if (overrides?.BaseProductionAmount.HasValue == true)
        {
            baseProdAmount = overrides.BaseProductionAmount.Value;
            if (!string.IsNullOrWhiteSpace(overrides.BaseProductionUnit)) baseProdUnit = overrides.BaseProductionUnit;
        }

        ws.Cell(rBaseProd, 8).Value = baseProdAmount;
        ws.Cell(rBaseProd, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(rBaseProd, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(rBaseProd, 8).Style.NumberFormat.Format = "#,##0.00";

        ws.Cell(rBaseProd, 10).Value = baseProdUnit;
        ws.Cell(rBaseProd, 10).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(rBaseProd, 10).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        ws.Range(rBaseProd, 6, rBaseProd, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rBaseProd, 8, rBaseProd, 9).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Cell(rBaseProd, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Row rOrgHeader: ข้อมูลองค์กร (F:J merged)
        ws.Cell(rOrgHeader, 6).Value = "ข้อมูลองค์กร";
        CleanMerge(ws, rOrgHeader, 6, 10);
        ws.Range(rOrgHeader, 6, rOrgHeader, 10).Merge();
        ws.Range(rOrgHeader, 6, rOrgHeader, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Rows rOrg1..rOrg5: Organization Information (Strip leading prefixes, Col F is 1..N, G:J merged with WrapText)
        var orgLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(overrides?.OrgInfoMultiLine))
        {
            orgLines = overrides.OrgInfoMultiLine
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanOrgInfoLine)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(overrides?.OrgInfo1)) orgLines.Add(CleanOrgInfoLine(overrides.OrgInfo1));
            if (!string.IsNullOrWhiteSpace(overrides?.OrgInfo2)) orgLines.Add(CleanOrgInfoLine(overrides.OrgInfo2));
            if (!string.IsNullOrWhiteSpace(overrides?.OrgInfo3)) orgLines.Add(CleanOrgInfoLine(overrides.OrgInfo3));
            if (!string.IsNullOrWhiteSpace(overrides?.OrgInfo4)) orgLines.Add(CleanOrgInfoLine(overrides.OrgInfo4));
            if (!string.IsNullOrWhiteSpace(overrides?.OrgInfo5)) orgLines.Add(CleanOrgInfoLine(overrides.OrgInfo5));
        }

        // [2026-09-17][Gemini] รองรับการขยายแถวข้อมูลองค์กรแบบ dynamic (ความจุเดิม 5 แถว) และการันตีเว้น row ว่างอย่างน้อย 1 แถวเสมอ
        int orgCapacity = 5;
        int orgStartRow = rOrg1;
        int orgEndRow = orgStartRow + orgCapacity - 1;
        int orgRequiredRows = Math.Max(orgCapacity, orgLines.Count);
        int orgExtra = orgRequiredRows - orgCapacity;

        if (orgExtra > 0)
        {
            ws.Row(orgEndRow).InsertRowsBelow(orgExtra);
        }

        for (int i = 0; i < orgRequiredRows; i++)
        {
            int r = orgStartRow + i;
            ws.Cell(r, 6).Value = (i + 1).ToString();
            ws.Cell(r, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r, 6).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            CleanMerge(ws, r, 7, 10);
            ws.Range(r, 7, r, 10).Merge();

            string textVal = i < orgLines.Count ? orgLines[i] : string.Empty;

            ws.Cell(r, 7).Value = textVal;
            ws.Cell(r, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(r, 7).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(r, 7).Style.Alignment.WrapText = true;

            ws.Cell(r, 6).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            ws.Range(r, 7, r, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }

        // [2026-09-17][Gemini] คำนวณตำแหน่งแถวสถานที่ติดต่อและวันขอขึ้นทะเบียนหลังขยายแถวข้อมูลองค์กร พร้อมสะสม totalShift
        int rContact = 32 + totalShift + orgExtra;
        int rRegDate = 33 + totalShift + orgExtra;
        totalShift += orgExtra;

        // Row rContact: สถานที่ติดต่อ
        ws.Cell(rContact, 6).Value = "สถานที่ติดต่อ";
        var contactAddr = !string.IsNullOrWhiteSpace(overrides?.ContactAddress)
            ? overrides.ContactAddress
            : (!string.IsNullOrWhiteSpace(overrides?.CertifierOrgTitle) ? overrides.CertifierOrgTitle : orgName);
        CleanMerge(ws, rContact, 6, 7);
        ws.Range(rContact, 6, rContact, 7).Merge();
        CleanMerge(ws, rContact, 8, 10);
        ws.Range(rContact, 8, rContact, 10).Merge();
        ws.Cell(rContact, 8).Value = contactAddr;
        ws.Cell(rContact, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(rContact, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(rContact, 6, rContact, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rContact, 8, rContact, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // Row rRegDate: วันที่ขอขึ้นทะเบียน
        ws.Cell(rRegDate, 6).Value = "วันที่ขอขึ้นทะเบียน";
        CleanMerge(ws, rRegDate, 6, 7);
        ws.Range(rRegDate, 6, rRegDate, 7).Merge();
        CleanMerge(ws, rRegDate, 8, 10);
        // [2026-09-25][Gemini] ใช้วันที่ปัจจุบัน (AppTimeZone.LocalNow.Date) เป็นค่า default เสมอตามคำสั่ง
        var regDate = overrides?.RegistrationDate ?? AppTimeZone.LocalNow.Date;
        ws.Cell(rRegDate, 8).Value = regDate.Date;
        ws.Cell(rRegDate, 8).Style.NumberFormat.Format = "dd/MM/yyyy";
        ws.Cell(rRegDate, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(rRegDate, 8).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Range(rRegDate, 6, rRegDate, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(rRegDate, 8, rRegDate, 10).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // [2026-09-17][Gemini] แทรกรูปถ่าย/โลโก้องค์กรในเซลล์ A8:D33 โดยหากไม่มีการอัปโหลด ให้ใช้รูป Default Eco-Facility Placeholder
        byte[]? orgImgBytes = null;
        if (!string.IsNullOrWhiteSpace(overrides?.OrganizationImageBase64))
        {
            try
            {
                var cleanBase64 = overrides.OrganizationImageBase64;
                if (cleanBase64.Contains(","))
                {
                    cleanBase64 = cleanBase64.Substring(cleanBase64.IndexOf(",") + 1);
                }
                orgImgBytes = Convert.FromBase64String(cleanBase64);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse OrganizationImageBase64 override.");
            }
        }

        if (orgImgBytes == null || orgImgBytes.Length == 0)
        {
            // [2026-09-18][Claude] ผู้ใช้ไม่แนบรูป -> ใช้รูป default ของระบบ
            orgImgBytes = LoadSystemDefaultImage(DefaultOrgFacilityImage);
        }

        if (orgImgBytes != null && orgImgBytes.Length > 0)
        {
            try
            {
                ws.Cell("A8").Value = string.Empty;
                using var ms = new MemoryStream(orgImgBytes);
                var pic = ws.AddPicture(ms, "OrgLogo");
                pic.MoveTo(ws.Cell("A8"), 10, 10);
                int maxWidth = 260;
                int maxHeight = 220;
                int origW = pic.OriginalWidth > 0 ? pic.OriginalWidth : 260;
                int origH = pic.OriginalHeight > 0 ? pic.OriginalHeight : 220;
                double ratio = Math.Min((double)maxWidth / origW, (double)maxHeight / origH);
                int finalW = Math.Max(50, (int)(origW * ratio));
                int finalH = Math.Max(50, (int)(origH * ratio));
                pic.WithSize(finalW, finalH);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to embed organization image into Fr-01.");
            }
        }

        return totalShift;
    }

    // [2026-09-16][Antigravity] Strip leading numeric or bullet prefixes from Org Info lines (e.g. "1. Head Office" -> "Head Office")
    private static string CleanOrgInfoLine(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return Regex.Replace(raw.Trim(), @"^(\d+|[a-zA-Z])[\.\:\)\-]\s*", "").Trim();
    }

    // [2026-09-16][Antigravity] Helper to remove any overlapping merges before establishing clean cell merge
    private static void CleanMerge(IXLWorksheet ws, int row, int startCol, int endCol)
    {
        var overlaps = ws.MergedRanges
            .Where(m => m.RangeAddress.FirstAddress.RowNumber <= row && m.RangeAddress.LastAddress.RowNumber >= row &&
                        m.RangeAddress.FirstAddress.ColumnNumber <= endCol && m.RangeAddress.LastAddress.ColumnNumber >= startCol)
            .ToList();
        foreach (var m in overlaps)
        {
            ws.MergedRanges.Remove(m);
        }
    }

    // [2026-09-25][Gemini] ล้าง merged ranges ที่ทับซ้อนกับช่วง 2D ที่กำหนดเพื่อป้องกันข้อผิดพลาดเวลา merge ใหม่
    private static void CleanRangeMerge(IXLWorksheet ws, int startRow, int startCol, int endRow, int endCol)
    {
        var overlaps = ws.MergedRanges
            .Where(m => m.RangeAddress.FirstAddress.RowNumber <= endRow &&
                        m.RangeAddress.LastAddress.RowNumber >= startRow &&
                        m.RangeAddress.FirstAddress.ColumnNumber <= endCol &&
                        m.RangeAddress.LastAddress.ColumnNumber >= startCol)
            .ToList();
        foreach (var m in overlaps)
        {
            ws.MergedRanges.Remove(m);
        }
    }

    // [2026-09-16][Antigravity] Post-process OpenXML drawings and Chart 4 anchor position & data cache
    private void AdjustFr01ChartAndDrawings(MemoryStream ms, int totalShift, CfoAuditReportDto calcReport, CfoReportOverridesDto? overrides)
    {
        try
        {
            ms.Position = 0;
            using var doc = SpreadsheetDocument.Open(ms, true);
            var wbPart = doc.WorkbookPart;
            if (wbPart == null) return;

            var sheets = wbPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
            var fr01Sheet = sheets.FirstOrDefault(s => string.Equals(s.Name?.Value, "Fr-01", StringComparison.OrdinalIgnoreCase));
            if (fr01Sheet == null || fr01Sheet.Id == null) return;

            var wsPart = wbPart.GetPartById(fr01Sheet.Id.Value!) as WorksheetPart;
            if (wsPart?.DrawingsPart == null) return;

            var drawPart = wsPart.DrawingsPart;

            // 1. Shift DrawingML Anchors for Chart 4 (and any elements at or below row 34)
            if (totalShift > 0)
            {
                using var drawStream = drawPart.GetStream(FileMode.Open, FileAccess.ReadWrite);
                var xDoc = XDocument.Load(drawStream);
                XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

                var twoCellAnchors = xDoc.Descendants(xdr + "twoCellAnchor").ToList();
                bool modified = false;

                foreach (var anchor in twoCellAnchors)
                {
                    var fromRowEl = anchor.Element(xdr + "from")?.Element(xdr + "row");
                    var toRowEl = anchor.Element(xdr + "to")?.Element(xdr + "row");

                    if (fromRowEl != null && toRowEl != null &&
                        int.TryParse(fromRowEl.Value, out int fromRow) &&
                        int.TryParse(toRowEl.Value, out int toRow))
                    {
                        // If anchor is located at or below row 30 (0-indexed), shift it down by totalShift
                        if (fromRow >= 30)
                        {
                            fromRowEl.Value = (fromRow + totalShift).ToString();
                            toRowEl.Value = (toRow + totalShift).ToString();
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    drawStream.Position = 0;
                    drawStream.SetLength(0);
                    xDoc.Save(drawStream);
                }
            }

            // 2. Populate Chart 4 Data Cache & Detach External Formula Reference if OnlyFr01Sheet
            foreach (var cp in drawPart.ChartParts)
            {
                using var chartStream = cp.GetStream(FileMode.Open, FileAccess.ReadWrite);
                var chartDoc = XDocument.Load(chartStream);
                XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";

                var valEl = chartDoc.Descendants(c + "val").FirstOrDefault();
                if (valEl != null)
                {
                    decimal[] vals = new[]
                    {
                        calcReport.TotalScope1_tCO2e,
                        calcReport.TotalScope2_tCO2e,
                        calcReport.TotalScope3_tCO2e,
                        calcReport.TotalSeparate_tCO2e
                    };

                    string[] cats = new[]
                    {
                        "ประเภท 1",
                        "ประเภท 2",
                        "ประเภท 3",
                        "อื่น ๆ"
                    };

                    if (overrides?.OnlyFr01Sheet == true)
                    {
                        // Convert to numLit and strLit for standalone Excel rendering without external sheets
                        var numLit = new XElement(c + "numLit",
                            new XElement(c + "formatCode", "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)"),
                            new XElement(c + "ptCount", new XAttribute("val", "4")),
                            vals.Select((v, idx) => new XElement(c + "pt",
                                new XAttribute("idx", idx.ToString()),
                                new XElement(c + "v", v.ToString("F2", CultureInfo.InvariantCulture))
                            ))
                        );

                        valEl.RemoveNodes();
                        valEl.Add(numLit);

                        var catEl = chartDoc.Descendants(c + "cat").FirstOrDefault();
                        if (catEl != null)
                        {
                            var strLit = new XElement(c + "strLit",
                                new XElement(c + "ptCount", new XAttribute("val", "4")),
                                cats.Select((t, idx) => new XElement(c + "pt",
                                    new XAttribute("idx", idx.ToString()),
                                    new XElement(c + "v", t)
                                ))
                            );

                            catEl.RemoveNodes();
                            catEl.Add(strLit);
                        }
                    }
                    else
                    {
                        // Full export: update numCache values so initial display is accurate while preserving formula
                        var numCache = valEl.Descendants(c + "numCache").FirstOrDefault();
                        if (numCache != null)
                        {
                            var pts = numCache.Elements(c + "pt").ToList();
                            for (int idx = 0; idx < vals.Length; idx++)
                            {
                                var pt = pts.FirstOrDefault(p => (string?)p.Attribute("idx") == idx.ToString());
                                if (pt == null)
                                {
                                    pt = new XElement(c + "pt",
                                        new XAttribute("idx", idx.ToString()),
                                        new XElement(c + "v", vals[idx].ToString("F2", CultureInfo.InvariantCulture)));
                                    numCache.Add(pt);
                                }
                                else
                                {
                                    var vEl = pt.Element(c + "v");
                                    if (vEl == null)
                                    {
                                        pt.Add(new XElement(c + "v", vals[idx].ToString("F2", CultureInfo.InvariantCulture)));
                                    }
                                    else
                                    {
                                        vEl.Value = vals[idx].ToString("F2", CultureInfo.InvariantCulture);
                                    }
                                }
                            }

                            var ptCount = numCache.Element(c + "ptCount");
                            if (ptCount != null) ptCount.SetAttributeValue("val", "4");
                        }
                    }

                    chartStream.Position = 0;
                    chartStream.SetLength(0);
                    chartDoc.Save(chartStream);
                }
            }

            // 3. [2026-09-16][Antigravity] Ensure ActiveTab is set to focusSheetName in OpenXML WorkbookView
            var focusSheetName = !string.IsNullOrWhiteSpace(overrides?.FocusSheetName) ? overrides.FocusSheetName : "Fr-01";
            var allSheets = wbPart.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
            int focusIndex = allSheets.FindIndex(s => string.Equals(s.Name?.Value, focusSheetName, StringComparison.OrdinalIgnoreCase));
            if (focusIndex >= 0)
            {
                var bookViews = wbPart.Workbook.BookViews;
                if (bookViews == null)
                {
                    bookViews = new DocumentFormat.OpenXml.Spreadsheet.BookViews();
                    wbPart.Workbook.AppendChild(bookViews);
                }
                var wbView = bookViews.Descendants<DocumentFormat.OpenXml.Spreadsheet.WorkbookView>().FirstOrDefault();
                if (wbView == null)
                {
                    wbView = new DocumentFormat.OpenXml.Spreadsheet.WorkbookView();
                    bookViews.AppendChild(wbView);
                }
                wbView.ActiveTab = (uint)focusIndex;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to adjust Fr-01 chart drawing anchors and data cache.");
        }
    }

    /// <summary>
    /// [2026-09-18][Claude] Fr-05 "สรุปการปล่อยก๊าซเรือนกระจกขององค์กร"
    /// ทุกช่องใช้สูตรของเทมเพลตตามเดิม ยกเว้น Carbon intensity (D17, D18 = ปีรายงาน / D33, D34 = ปีฐาน)
    /// ซึ่งหารด้วยผลผลิตใน Fr-01 ถ้าผลผลิตเป็น 0 หรือไม่ได้กรอก สูตรเดิมจะได้ #DIV/0!
    /// จึงเขียนค่า 0 ทับสูตรในกรณีนั้น
    /// </summary>
    private void PopulateFr05(XLWorkbook workbook, Fr04BlockLayout calcLayout, Fr04BlockLayout? baseLayout, CfoReportOverridesDto? overrides = null)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-05", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        // [2026-09-25][Gemini] อัปเดตสูตรอ้างอิงเซลล์ Total ของ Fr-04.1 ให้ตรงกับตำแหน่งแถวจริงที่ขยายแบบ dynamic
        ws.Cell("C10").FormulaA1 = $"=ROUNDUP('Fr-04.1'!AO{calcLayout.S1TotalRow},0)";
        ws.Cell("C11").FormulaA1 = $"=ROUNDUP('Fr-04.1'!AO{calcLayout.S2TotalRow},0)";
        ws.Cell("C12").FormulaA1 = $"=ROUNDUP('Fr-04.1'!AO{calcLayout.S3TotalRow},0)";
        ws.Cell("C13").FormulaA1 = $"=ROUNDUP('Fr-04.1'!AO{calcLayout.SepTotalRow},0)";

        if (baseLayout != null)
        {
            ws.Cell("C26").FormulaA1 = $"=ROUNDUP('Fr-04.2'!AO{baseLayout.S1TotalRow},0)";
            ws.Cell("C27").FormulaA1 = $"=ROUNDUP('Fr-04.2'!AO{baseLayout.S2TotalRow},0)";
            ws.Cell("C28").FormulaA1 = $"=ROUNDUP('Fr-04.2'!AO{baseLayout.S3TotalRow},0)";
            ws.Cell("C29").FormulaA1 = $"=ROUNDUP('Fr-04.2'!AO{baseLayout.SepTotalRow},0)";
        }

        decimal calcProduction = overrides?.CalculateProductionAmount ?? 0m;
        decimal baseProduction = overrides?.BaseProductionAmount ?? 0m;

        if (calcProduction <= 0m)
        {
            SetZeroIntensity(ws, "D17");
            SetZeroIntensity(ws, "D18");
        }

        if (baseProduction <= 0m)
        {
            SetZeroIntensity(ws, "D33");
            SetZeroIntensity(ws, "D34");
        }

        static void SetZeroIntensity(IXLWorksheet sheet, string address)
        {
            var cell = sheet.Cell(address);
            cell.Clear(XLClearOptions.Contents);   // ตัดสูตรที่หารด้วยศูนย์ทิ้ง (เก็บรูปแบบเซลล์เดิมไว้)
            cell.Value = 0;
        }
    }

    /// <summary>
    /// [2026-09-17][Claude] Fr-02 "แผนภาพองค์กร": เขียนวันที่หัวฟอร์ม (M4) และฝังรูปแผนภาพลงพื้นที่ merge A6:M26
    /// หมายเหตุ: G3 (องค์กร) และ G4 (ผู้จัดทำ) เป็นสูตรอ้างอิง Fr-01 อยู่แล้ว จึงห้ามเขียนทับ
    /// ถ้าผู้ใช้ไม่อัปโหลดรูป จะปล่อยชีตไว้ตามเทมเพลตเดิมเพื่อให้ไปวาดต่อใน Excel ได้
    /// </summary>
    private void PopulateFr02(XLWorkbook workbook, CfoAuditReportDto calcReport, CfoReportOverridesDto? overrides = null)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-02", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        // [2026-09-25][Gemini] ใช้วันที่ปัจจุบัน (AppTimeZone.LocalNow.Date) เป็นค่า default เสมอตามคำสั่ง
        var diagramDate = overrides?.Fr02DiagramDate ?? overrides?.PreparationDate ?? AppTimeZone.LocalNow.Date;
        ws.Cell("M4").Value = diagramDate.Date;
        ws.Cell("M4").Style.NumberFormat.Format = "dd/MM/yyyy";
        ws.Cell("M4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var imgBytes = TryDecodeImageBase64(overrides?.Fr02ImageBase64, nameof(CfoReportOverridesDto.Fr02ImageBase64));
        if (imgBytes == null || imgBytes.Length == 0)
        {
            // [2026-09-18][Claude] ผู้ใช้ไม่แนบแผนภาพองค์กร -> ใช้รูป default ของระบบ ไม่ปล่อยชีตว่าง
            imgBytes = LoadSystemDefaultImage(DefaultOrgDiagramImage);
        }
        if (imgBytes == null || imgBytes.Length == 0) return;

        // พื้นที่ merge A6:M26 ~ 780 x 400 px (13 คอลัมน์ x 21 แถว ความสูงมาตรฐาน)
        EmbedFittedPicture(ws, imgBytes, "A6", "Fr02OrgDiagram", 780, 400, "Fr-02");
    }

    /// <summary>
    /// [2026-09-18][Claude] โหลดรูป default ของระบบตามชื่อไฟล์ ใช้ร่วมกันทุกชีตที่ต้องแนบรูป
    /// ค้นตามลำดับ wwwroot/images -> Files/Images -> โฟลเดอร์ปลายทางของ build
    /// </summary>
    private byte[]? LoadSystemDefaultImage(string fileName)
    {
        try
        {
            var candidatePaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            {
                candidatePaths.Add(Path.Combine(_env.WebRootPath, "images", fileName));
            }
            if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
            {
                candidatePaths.Add(Path.Combine(_env.ContentRootPath, "Files", "Images", fileName));
                candidatePaths.Add(Path.Combine(_env.ContentRootPath, "wwwroot", "images", fileName));
            }
            candidatePaths.Add(Path.Combine(AppContext.BaseDirectory, "Files", "Images", fileName));
            candidatePaths.Add(Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", fileName));

            var foundPath = candidatePaths.FirstOrDefault(File.Exists);
            if (foundPath != null) return File.ReadAllBytes(foundPath);

            _logger.LogWarning("ไม่พบรูป default ของระบบ: {File}", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load system default image {File}.", fileName);
        }
        return null;
    }

    /// <summary>
    /// [2026-09-17][Claude] แปลง data URL ("data:image/png;base64,....") หรือ base64 ล้วนเป็น byte[] แบบไม่โยน exception
    /// </summary>
    private byte[]? TryDecodeImageBase64(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            int commaIndex = value.IndexOf(',');
            var clean = commaIndex >= 0 ? value.Substring(commaIndex + 1) : value;
            return Convert.FromBase64String(clean.Trim());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse {Field} override.", fieldName);
            return null;
        }
    }

    /// <summary>
    /// [2026-09-17][Claude] Fr-03.1 "แผนภาพแสดงโครงสร้างขององค์กรและหน้าที่ความรับผิดชอบ":
    /// เขียนวันที่หัวฟอร์ม (M4), วันที่เสร็จสิ้น (K26) และฝังรูปแผนภาพลงพื้นที่ merge A7:M25
    /// หมายเหตุ: G3 (องค์กร), G4 (ผู้จัดทำ) และ B26 (จัดทำโดย) เป็นสูตรอ้างอิง Fr-01 อยู่แล้ว จึงห้ามเขียนทับ
    /// ถ้าผู้ใช้ไม่อัปโหลดรูป จะปล่อยชีตไว้ตามเทมเพลตเดิมเพื่อให้ไปวาดต่อใน Excel ได้
    /// </summary>
    private void PopulateFr031(XLWorkbook workbook, CfoAuditReportDto calcReport, CfoReportOverridesDto? overrides = null)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-03.1", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        // [2026-09-25][Gemini] ใช้วันที่ปัจจุบัน (AppTimeZone.LocalNow.Date) เป็นค่า default เสมอตามคำสั่ง
        var diagramDate = overrides?.Fr031DiagramDate ?? overrides?.PreparationDate ?? AppTimeZone.LocalNow.Date;
        ws.Cell("M4").Value = diagramDate.Date;
        ws.Cell("M4").Style.NumberFormat.Format = "dd/MM/yyyy";
        ws.Cell("M4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // K26:M26 = "เสร็จสิ้นวันที่"
        var completionDate = overrides?.Fr031CompletionDate ?? AppTimeZone.LocalNow.Date;
        ws.Cell("K26").Value = completionDate.Date;
        ws.Cell("K26").Style.NumberFormat.Format = "dd/MM/yyyy";
        ws.Cell("K26").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var imgBytes = TryDecodeImageBase64(overrides?.Fr031ImageBase64, nameof(CfoReportOverridesDto.Fr031ImageBase64));
        if (imgBytes == null || imgBytes.Length == 0)
        {
            // [2026-09-18][Claude] ผู้ใช้ไม่แนบแผนภาพโครงสร้างองค์กร -> ใช้รูป default ของระบบ
            imgBytes = LoadSystemDefaultImage(DefaultOrgStructureImage);
        }
        if (imgBytes == null || imgBytes.Length == 0) return;

        // พื้นที่ merge A7:M25 ~ 780 x 360 px (13 คอลัมน์ x 19 แถว ความสูงมาตรฐาน)
        EmbedFittedPicture(ws, imgBytes, "A7", "Fr031OrgStructure", 780, 360, "Fr-03.1");
    }

    /// <summary>
    /// [2026-09-17][Claude] ฝังรูปลงเซลล์ที่กำหนดและย่อให้พอดีกรอบโดยไม่ขยายรูปที่เล็กกว่ากรอบ
    /// </summary>
    private void EmbedFittedPicture(IXLWorksheet ws, byte[] imgBytes, string anchorCell, string pictureName, int maxWidth, int maxHeight, string sheetLabel)
    {
        try
        {
            using var ms = new MemoryStream(imgBytes);
            var pic = ws.AddPicture(ms, pictureName);
            pic.MoveTo(ws.Cell(anchorCell), 8, 8);

            int origW = pic.OriginalWidth > 0 ? pic.OriginalWidth : maxWidth;
            int origH = pic.OriginalHeight > 0 ? pic.OriginalHeight : maxHeight;
            double ratio = Math.Min((double)maxWidth / origW, (double)maxHeight / origH);
            if (ratio > 1d) ratio = 1d;
            int finalW = Math.Max(50, (int)(origW * ratio));
            int finalH = Math.Max(50, (int)(origH * ratio));
            pic.WithSize(finalW, finalH);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to embed picture into {Sheet}.", sheetLabel);
        }
    }

    /// <summary>
    /// [2026-09-17][Claude] สัดส่วน Magnitude รายหมวด Scope 3 (Category 1..15) คิดเป็น % ของ Scope 3 ทั้งหมด
    /// เป็นแหล่งความจริงเดียวสำหรับทั้งคอลัมน์ D ของ Fr-03.2 และการแสดงผลในหน้า Wizard
    /// หมวดที่ไม่มีข้อมูลกิจกรรมจะไม่อยู่ใน Dictionary (ถือเป็น 0%)
    /// </summary>
    public Dictionary<int, decimal> GetFr032MagnitudePercents(CfoAuditReportDto report)
    {
        var result = new Dictionary<int, decimal>();

        var s3Group = report.Scopes.FirstOrDefault(s => s.ScopeNo == eTgoScopeNo.Scope3);
        decimal s3Kg = s3Group?.TotalKgCO2e ?? 0m;
        if (s3Group == null || s3Kg <= 0m) return result;

        foreach (var ag in s3Group.ActivityGroups)
        {
            int row = ResolveFr032Row(ag);
            if (row < 19 || row > 33) continue;

            int category = row - 18;
            // หลายกลุ่มกิจกรรมอาจตกลงหมวดเดียวกัน ต้องบวกสะสม ไม่ใช่เขียนทับ
            result[category] = (result.TryGetValue(category, out var current) ? current : 0m)
                             + (ag.TotalKgCO2e / s3Kg * 100m);
        }

        return result;
    }

    private void PopulateFr032(XLWorkbook workbook, CfoAuditReportDto report, CfoReportOverridesDto? overrides = null)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-03.2", StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        const int dataStartRow = 19;
        const int maxRows      = 15;
        const int dataEndRow   = dataStartRow + maxRows - 1;

        // [GHG-18] C / K / N ใน template เป็นฟอนต์ Wingdings 2 ซึ่งอักขระ "P" เรนเดอร์เป็นเครื่องหมายถูก
        //          ห้ามเขียน "✓" หรือ "✗" (Unicode) ลงเซลล์เหล่านี้ เพราะไม่มี glyph ในฟอนต์นี้
        const string WINGDINGS2_CHECK = "P";

        const string COL_HAS_SOURCE = "C"; // Source of GHG      : ช่องติ๊ก (Wingdings 2)
        const string COL_MAGNITUDE  = "D"; // Magnitude          : ตัวเลข % — ระบบคำนวณให้ได้

        // [GHG-19] E, F, H เป็นเซลล์ 0.00% (Level of influence / Risk / Sector Guidance)
        //          เป็นเกณฑ์เชิงคุณภาพที่ระบบไม่มีข้อมูล ต้องให้ผู้ใช้กรอกเอง ห้ามเขียนทับ
        // [GHG-18] K (Outsourcing) และ N (Employee engagement) เป็นช่องติ๊ก Wingdings 2
        //          ระบบไม่มีข้อมูลสองเกณฑ์นี้ ต้องให้ผู้ใช้ติ๊กเอง ห้ามเขียนทับ

        // 1) รีเซ็ตเฉพาะสองคอลัมน์ที่ระบบเป็นเจ้าของ (C และ D) — คอลัมน์อื่นห้ามแตะ
        // [GHG-09] ข้อสมมติ: ระบบติ๊กคอลัมน์ C ตามเกณฑ์ "มีข้อมูลกิจกรรมในรอบรายงานนี้" (ผู้ใช้ต้องทบทวนก่อนยื่น)
        for (int r = dataStartRow; r <= dataEndRow; r++)
        {
            ws.Cell($"{COL_HAS_SOURCE}{r}").Clear(XLClearOptions.Contents);   // ไม่ติ๊ก = เว้นว่าง
            ws.Cell($"{COL_MAGNITUDE}{r}").Value = 0;
        }

        // [2026-09-25][Antigravity] แก้บั๊ก Fr-03.2 ไม่มีการประเมินนัยสำคัญ:
        // หากไม่มี overrides หรือไม่มี Fr032Categories กำหนดมา ให้สร้าง categories เริ่มต้น 15 หมวด
        // และทำการประเมินผลตาม Rubric เสมอ ไม่ปล่อยให้คอลัมน์ E, F, H, K, N, Remark และตารางคำอธิบายด้านล่างว่างเปล่า
        var categories = overrides?.Fr032Categories;
        if (categories == null || categories.Count == 0)
        {
            categories = Fr032Rubric.CreateDefaultCategories();
        }

        // ถ้ามี Scope3CheckedCategories ระบุมา ให้มั่นใจว่าหมวดเหล่านั้นถูกตั้ง HasSource = true ด้วย
        if (overrides?.Scope3CheckedCategories != null)
        {
            foreach (var cat in categories)
            {
                if (overrides.Scope3CheckedCategories.Contains(cat.CategoryNo))
                {
                    cat.HasSource = true;
                }
            }
        }

        // [2026-09-18][Claude] หมวดที่ระบบคำนวณ Magnitude ได้ แปลว่าองค์กร "มีแหล่งปล่อย" ในหมวดนั้นจริง
        // จึงติ๊ก HasSource ให้อัตโนมัติก่อนประเมิน
        Fr032Rubric.ApplyAutoSource(categories, GetFr032MagnitudePercents(report));

        // [2026-09-18][Claude] เกณฑ์เชิงคุณภาพที่ผู้ใช้ยังไม่ได้เลือก ให้ใช้ค่าเริ่มต้นของระบบ
        // เพื่อให้ฟอร์มมีคะแนนครบทุกคอลัมน์ (E, F, H, K, N) ค่าที่ผู้ใช้กรอกเองมาก่อนเสมอ
        Fr032Rubric.ApplyAutoScores(categories, report.MaterialityThreshold);

        const string COL_INFLUENCE = "E";
        const string COL_RISK      = "F";
        const string COL_SECTOR    = "H";
        const string COL_OUTSOURCE = "K";
        const string COL_ENGAGE    = "N";
        const string COL_REMARK    = "O";

        decimal threshold = report.MaterialityThreshold;

        foreach (var item in categories)
        {
            if (item.CategoryNo < 1 || item.CategoryNo > 15) continue;
            int row = dataStartRow + item.CategoryNo - 1;

            var eval = Fr032Rubric.Evaluate(item, threshold);

            // คอลัมน์ C (Source of GHG) และ D (Magnitude)
            SetTick(ws, $"{COL_HAS_SOURCE}{row}", item.HasSource);
            ws.Cell($"{COL_MAGNITUDE}{row}").Value = (double)(item.MagnitudePercent / 100m);

            // คอลัมน์ E, F, H, K, N
            SetTick(ws, $"{COL_OUTSOURCE}{row}", (item.OutsourcingScore ?? 0) >= Fr032Rubric.TickScoreThreshold);
            SetTick(ws, $"{COL_ENGAGE}{row}", (item.EngagementScore ?? 0) >= Fr032Rubric.TickScoreThreshold);

            SetScorePercent(ws, $"{COL_INFLUENCE}{row}", item.InfluenceScore);
            SetScorePercent(ws, $"{COL_RISK}{row}", item.RiskScore);
            SetScorePercent(ws, $"{COL_SECTOR}{row}", item.SectorGuidanceScore);

            // หมายเหตุ: ใช้ข้อความของผู้ใช้ก่อน ถ้าเว้นว่างจึงเขียนสรุปคะแนนให้ผู้ทวนสอบเห็นที่มา
            string remark = !string.IsNullOrWhiteSpace(item.Remark)
                ? item.Remark!
                : BuildFr032Remark(eval, item.IsAutoScored);

            if (!string.IsNullOrWhiteSpace(remark)) ws.Cell($"{COL_REMARK}{row}").Value = remark;
            else ws.Cell($"{COL_REMARK}{row}").Clear(XLClearOptions.Contents);
        }

        WriteFr032RubricDescription(ws, threshold);

        // [2026-09-17][Claude] ช่องติ๊กฟอนต์ Wingdings 2 — ติ๊ก = "P", ไม่ติ๊ก = เว้นว่าง (ห้ามใช้อักขระ Unicode)
        static void SetTick(IXLWorksheet sheet, string address, bool isChecked)
        {
            if (isChecked) sheet.Cell(address).Value = WINGDINGS2_CHECK;
            else sheet.Cell(address).Clear(XLClearOptions.Contents);
        }

        // [2026-09-17][Claude] ระดับ 1..5 -> สัดส่วนของคะแนนเต็ม (เซลล์รูปแบบ 0.00%)
        static void SetScorePercent(IXLWorksheet sheet, string address, int? score)
        {
            if (score.HasValue && score.Value >= 1 && score.Value <= 5)
                sheet.Cell(address).Value = (double)(score.Value / 5m);
            else
                sheet.Cell(address).Clear(XLClearOptions.Contents);
        }
    }

    /// <summary>[2026-09-17][Claude] สรุปคะแนนลงคอลัมน์ Remark เพื่อให้ผู้ทวนสอบเห็นที่มาของการตัดสิน</summary>
    private static string BuildFr032Remark(Fr032EvaluationResult eval, bool isAutoScored = false)
    {
        if (!eval.IsAssessed) return "ไม่มีแหล่งปล่อยในหมวดนี้";

        string verdict = eval.IsSignificant ? "มีนัยสำคัญ" : "ไม่มีนัยสำคัญ";
        string reason = eval.ForcedByMagnitude ? " (เข้าเกณฑ์ขนาดการปล่อยโดยอัตโนมัติ)" : string.Empty;
        string incomplete = eval.IsComplete ? string.Empty : " [ให้คะแนนไม่ครบ — คิดจากหัวข้อที่เลือกไว้]";
        // [2026-09-25][Antigravity] ปรับข้อความหมายเหตุการประเมินให้สุภาพ เป็นทางการ และเป็นมิตรต่อผู้ใช้งานตามเกณฑ์มาตรฐาน TGO Fr-03.2 Rubric
        string auto = isAutoScored ? " [ประเมินตามเกณฑ์มาตรฐาน TGO Fr-03.2 Rubric]" : string.Empty;

        // แสดง % คู่กับคะแนนเต็ม 5 เพื่อให้อ่านเข้ากับคอลัมน์ E/F/H ที่เป็น % ตามรูปแบบเซลล์ของเทมเพลต
        decimal percent = Fr032Rubric.WeightedScoreToPercent(eval.WeightedScore);

        return $"คะแนนถ่วงน้ำหนัก {percent:0}% ({eval.WeightedScore:0.00}/5.00) — {verdict}{reason}{incomplete}{auto}";
    }

    /// <summary>
    /// [2026-09-17][Claude] เขียนเกณฑ์ประเมินลงใต้หัวข้อ A35 "อธิบาย หลักเกณฑ์ในการประเมินความมีนัยสำคัญ"
    /// เป็น matrix จริงในชีต: แถว = 6 เกณฑ์, คอลัมน์ = ระดับคะแนน 1..5 (ไม่ใช่ข้อความยาวบรรทัดละเกณฑ์)
    /// เทมเพลตมีคอลัมน์ A..P กว้างไม่เท่ากัน จึง merge เป็น 7 ช่องให้ความกว้างใกล้เคียงกัน
    /// ทั้งบล็อกอยู่ใต้แถว 33 ทั้งหมด จึงไม่กระทบตารางประเมินด้านบน
    /// </summary>
    private static void WriteFr032RubricDescription(IXLWorksheet ws, decimal thresholdPercent)
    {
        const int headlineRow  = 36;
        const int headerRow    = 37;
        const int firstDataRow = 38;
        const int lastBlockRow = 60;   // ช่วงที่เคลียร์ก่อนเขียนใหม่ (ใต้ตารางประเมิน ไม่มีข้อมูลของเทมเพลต)
        const int firstCol     = 1;    // A
        const int lastCol      = 16;   // P

        // ช่องของ matrix: ชื่อเกณฑ์ (A:B) | น้ำหนัก (C) | ระดับ 1..5 (D:E, F:H, I:K, L:N, O:P)
        int[,] blocks = { { 1, 2 }, { 3, 3 }, { 4, 5 }, { 6, 8 }, { 9, 11 }, { 12, 14 }, { 15, 16 } };
        int blockCount = blocks.GetLength(0);

        var matrix = Fr032Rubric.GetRubricMatrix(thresholdPercent);
        var footer = Fr032Rubric.GetRubricFooterLines(thresholdPercent);

        // 1) เคลียร์พื้นที่เดิมก่อน (กันข้อความรุ่นก่อนค้าง)
        for (int r = headlineRow; r <= lastBlockRow; r++)
        {
            CleanMerge(ws, r, firstCol, lastCol);
            ws.Range(r, firstCol, r, lastCol).Clear(XLClearOptions.Contents);
        }

        // 2) หัวเรื่องของตาราง
        ws.Cell(headlineRow, firstCol).Value = Fr032Rubric.GetRubricHeadline(thresholdPercent);
        ws.Cell(headlineRow, firstCol).Style.Font.SetBold(true);
        ws.Cell(headlineRow, firstCol).Style.Font.SetFontSize(10);
        ws.Cell(headlineRow, firstCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Cell(headlineRow, firstCol).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(headlineRow, firstCol).Style.Alignment.WrapText = true;
        ws.Range(headlineRow, firstCol, headlineRow, lastCol).Merge();
        ws.Row(headlineRow).Height = 30;

        // 3) แถวหัวตาราง
        // [2026-09-18][Claude] กำกับ % ที่จะถูกบันทึกลงคอลัมน์ E/F/H ไว้ที่หัวตาราง ให้ผู้ทวนสอบเทียบตัวเลขในฟอร์มได้ทันที
        string[] headers =
        {
            "เกณฑ์ (คอลัมน์ในฟอร์ม)",
            "น้ำหนัก",
            Fr032Rubric.GetScoreLabel(1),
            Fr032Rubric.GetScoreLabel(2),
            Fr032Rubric.GetScoreLabel(3),
            Fr032Rubric.GetScoreLabel(4),
            Fr032Rubric.GetScoreLabel(5)
        };
        for (int c = 0; c < blockCount; c++)
        {
            int c1 = blocks[c, 0];
            int c2 = blocks[c, 1];

            ws.Cell(headerRow, c1).Value = headers[c];
            ws.Cell(headerRow, c1).Style.Font.SetBold(true);
            ws.Cell(headerRow, c1).Style.Font.SetFontSize(9);
            ws.Cell(headerRow, c1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(headerRow, c1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(headerRow, c1).Style.Alignment.WrapText = true;
            ws.Range(headerRow, c1, headerRow, c2).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xE7, 0xE6, 0xE6));

            if (c2 > c1) ws.Range(headerRow, c1, headerRow, c2).Merge();
            ws.Range(headerRow, c1, headerRow, c2).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        }
        ws.Row(headerRow).Height = 20;

        // 4) แถวข้อมูล 6 เกณฑ์
        for (int i = 0; i < matrix.Count; i++)
        {
            int row = firstDataRow + i;
            var item = matrix[i];

            for (int c = 0; c < blockCount; c++)
            {
                int c1 = blocks[c, 0];
                int c2 = blocks[c, 1];

                if (c == 0)
                {
                    ws.Cell(row, c1).Value = $"{item.Criterion}  [{item.Column}]";
                    ws.Cell(row, c1).Style.Font.SetBold(true);
                    ws.Cell(row, c1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }
                else if (c == 1)
                {
                    ws.Cell(row, c1).Value = (double)item.Weight;   // เก็บเป็นสัดส่วน แล้วจัดรูปแบบเป็น %
                    ws.Cell(row, c1).Style.NumberFormat.Format = "0%";
                    ws.Cell(row, c1).Style.Font.SetBold(true);
                    ws.Cell(row, c1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                else
                {
                    ws.Cell(row, c1).Value = item.Levels[c - 2];
                    ws.Cell(row, c1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                ws.Cell(row, c1).Style.Font.SetFontSize(9);
                ws.Cell(row, c1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
                ws.Cell(row, c1).Style.Alignment.WrapText = true;

                if (c2 > c1) ws.Range(row, c1, row, c2).Merge();
                ws.Range(row, c1, row, c2).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            }

            ws.Row(row).Height = 42;
        }

        // 5) บรรทัดสรุปใต้ตาราง (สูตร / กฎการตัดสิน / การบันทึกลงแบบฟอร์ม / ที่มาของเกณฑ์)
        int footerRow = firstDataRow + matrix.Count + 1;
        foreach (var line in footer)
        {
            if (footerRow > lastBlockRow) break;

            ws.Cell(footerRow, firstCol).Value = line;
            ws.Cell(footerRow, firstCol).Style.Font.SetFontSize(9);
            ws.Cell(footerRow, firstCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(footerRow, firstCol).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Cell(footerRow, firstCol).Style.Alignment.WrapText = true;
            ws.Range(footerRow, firstCol, footerRow, lastCol).Merge();
            ws.Row(footerRow).Height = 26;
            footerRow++;
        }
    }

    /// <summary>
    /// [2026-09-17][Claude] หาแถวใน Fr-03.2 จากเลขหมวด Scope 3 (1..15) โดยอ่านจาก Code เป็นหลัก
    /// (เช่น "0307", "S3-07", "3.7") และใช้ตาราง ActivityGroupId เดิมเป็น fallback เท่านั้น
    /// </summary>
    private static int ResolveFr032Row(ActivityGroupAuditDto ag)
    {
        int category = ParseScope3Category(ag.Code);
        if (category >= 1 && category <= 15)
        {
            return 18 + category;
        }

        return Scope3ActivityGroupIdToFr032RowMap.TryGetValue(ag.ActivityGroupId, out var mappedRow)
            ? mappedRow
            : 0;
    }

    // [2026-09-23][Claude] internal: ใช้ร่วมกับ CfoWordReportService (ความสำคัญของแหล่งปล่อย Scope 3 ตามผล Fr-03.2)
    internal static int ParseScope3Category(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;

        var c = code.Trim();

        // "0307" / "0315"
        var m = Regex.Match(c, @"^0?3(\d{2})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n1)) return n1;

        // "S3-07" / "s3-15" / "3.7" / "3-7"
        m = Regex.Match(c, @"^[sS]?3[\.\-](\d{1,2})$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n2)) return n2;

        return 0;
    }

    // [2026-09-25][Gemini] เก็บพิกัดแถวของ Fr-04.1/Fr-04.2 ที่ขยายแบบ dynamic เพื่อรองรับข้อมูลกิจกรรมครบทุกแถวโดยไม่ยุบรวม
    internal record Fr04BlockLayout(
        int S1Start, int S1End, int S1TotalRow,
        int SepStart, int SepEnd, int SepTotalRow,
        int S2Start, int S2End, int S2TotalRow,
        int S3Start, int S3End, int S3TotalRow,
        int S1S2TotalRow, int GrossTotalRow, int SignerRow
    );

    private Fr04BlockLayout PopulateFr04Sheet(XLWorkbook workbook, string sheetName, CfoAuditReportDto report, int year)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
        if (ws == null)
        {
            return new Fr04BlockLayout(10, 23, 24, 25, 29, 30, 31, 34, 35, 36, 42, 43, 44, 45, 46);
        }

        var auditLines = report.AuditLines ?? new List<ActivityAuditLineDto>();
        var s1Lines = auditLines.Where(l => l.ScopeNo == eTgoScopeNo.Scope1).ToList();
        var sepLines = auditLines.Where(l => (int)l.ScopeNo == 4).ToList();
        var s2Lines = auditLines.Where(l => l.ScopeNo == eTgoScopeNo.Scope2).ToList();
        var s3Lines = auditLines.Where(l => l.ScopeNo == eTgoScopeNo.Scope3).ToList();

        // [2026-09-25][Gemini] คำนวณจำนวนแถวส่วนเกินเพื่อแทรกแถวจากล่างขึ้นบน (Bottom-Up)
        // ป้องกันไม่ให้การแทรกแถวก่อนหน้าไปเปลี่ยน index ของแถวถัดไป
        const int s1Cap = 14;  // แถวเดิม 10..23
        const int sepCap = 5;  // แถวเดิม 25..29
        const int s2Cap = 4;   // แถวเดิม 31..34
        const int s3Cap = 7;   // แถวเดิม 36..42

        int s3Extra = Math.Max(0, s3Lines.Count - s3Cap);
        int s2Extra = Math.Max(0, s2Lines.Count - s2Cap);
        int sepExtra = Math.Max(0, sepLines.Count - sepCap);
        int s1Extra = Math.Max(0, s1Lines.Count - s1Cap);

        // แทรกแถวจากล่างขึ้นบน
        if (s3Extra > 0) ws.Row(42).InsertRowsBelow(s3Extra);
        if (s2Extra > 0) ws.Row(34).InsertRowsBelow(s2Extra);
        if (sepExtra > 0) ws.Row(29).InsertRowsBelow(sepExtra);
        if (s1Extra > 0) ws.Row(23).InsertRowsBelow(s1Extra);

        // คำนวณพิกัดแถวใหม่ทั้งหมด
        int s1Start = 10;
        int s1RowCount = Math.Max(s1Cap, s1Lines.Count);
        int s1End = s1Start + s1RowCount - 1;
        int s1TotalRow = s1End + 1;

        int sepStart = s1TotalRow + 1;
        int sepRowCount = Math.Max(sepCap, sepLines.Count);
        int sepEnd = sepStart + sepRowCount - 1;
        int sepTotalRow = sepEnd + 1;

        int s2Start = sepTotalRow + 1;
        int s2RowCount = Math.Max(s2Cap, s2Lines.Count);
        int s2End = s2Start + s2RowCount - 1;
        int s2TotalRow = s2End + 1;

        int s3Start = s2TotalRow + 1;
        int s3RowCount = Math.Max(s3Cap, s3Lines.Count);
        int s3End = s3Start + s3RowCount - 1;
        int s3TotalRow = s3End + 1;

        int s1s2TotalRow = s3TotalRow + 1;
        int grossTotalRow = s3TotalRow + 2;
        int signerRow = s3TotalRow + 3;

        var layout = new Fr04BlockLayout(
            s1Start, s1End, s1TotalRow,
            sepStart, sepEnd, sepTotalRow,
            s2Start, s2End, s2TotalRow,
            s3Start, s3End, s3TotalRow,
            s1s2TotalRow, grossTotalRow, signerRow
        );

        // 1. Scope 1: เขียนข้อมูลกิจกรรมทุกรายการแบบ 1 แถวต่อ 1 รายการ ไม่มีการยุบรวม
        for (int i = 0; i < s1RowCount; i++)
        {
            int r = s1Start + i;
            if (i < s1Lines.Count)
            {
                PopulateSingleRow(ws, r, s1Lines[i], isScope1: true, isSeparate: false, layout);
            }
            else
            {
                ClearUnusedRow(ws, r, isScope1: true, isSeparate: false, layout);
            }
        }
        // Scope 1 Total Row
        for (int c = 27; c <= 41; c++)
        {
            var colLetter = ws.Cell(s1TotalRow, c).Address.ColumnLetter;
            ws.Cell(s1TotalRow, c).FormulaA1 = $"=SUM({colLetter}{s1Start}:{colLetter}{s1End})";
        }
        ws.Cell(s1TotalRow, 42).FormulaA1 = $"=SUM(AP{s1Start}:AP{s1End})";
        CleanRangeMerge(ws, s1TotalRow, 5, s1TotalRow, 17);
        ws.Range(s1TotalRow, 5, s1TotalRow, 17).Merge();

        // 2. Separate Report (Biogenic):
        for (int i = 0; i < sepRowCount; i++)
        {
            int r = sepStart + i;
            if (i < sepLines.Count)
            {
                PopulateSingleRow(ws, r, sepLines[i], isScope1: false, isSeparate: true, layout);
            }
            else
            {
                ClearUnusedRow(ws, r, isScope1: false, isSeparate: true, layout);
            }
        }
        // Separate Total Row
        ws.Cell(sepTotalRow, 27).FormulaA1 = $"=SUM(AA{sepStart}:AA{sepEnd})";
        ws.Cell(sepTotalRow, 40).FormulaA1 = $"=SUM(AN{sepStart}:AN{sepEnd})";
        ws.Cell(sepTotalRow, 41).FormulaA1 = $"=SUM(AO{sepStart}:AO{sepEnd})";
        ws.Cell(sepTotalRow, 42).FormulaA1 = $"=SUM(AP{sepStart}:AP{sepEnd})";
        CleanRangeMerge(ws, sepTotalRow, 2, sepTotalRow, 17);
        ws.Range(sepTotalRow, 2, sepTotalRow, 17).Merge();

        // 3. Scope 2:
        CleanRangeMerge(ws, s2Start, 5, s2End, 16);
        ws.Range(s2Start, 5, s2End, 16).Merge();
        ws.Cell(s2Start, 5).Value = "-";
        ws.Cell(s2Start, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s2Start, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        CleanRangeMerge(ws, s2Start, 27, s2End, 40);
        ws.Range(s2Start, 27, s2End, 40).Merge();
        ws.Cell(s2Start, 27).Value = "-";
        ws.Cell(s2Start, 27).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s2Start, 27).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        for (int i = 0; i < s2RowCount; i++)
        {
            int r = s2Start + i;
            if (i < s2Lines.Count)
            {
                PopulateSingleRow(ws, r, s2Lines[i], isScope1: false, isSeparate: false, layout);
            }
            else
            {
                ClearUnusedRow(ws, r, isScope1: false, isSeparate: false, layout);
            }
        }
        // Scope 2 Total Row
        ws.Cell(s2TotalRow, 41).FormulaA1 = $"=SUM(AO{s2Start}:AO{s2End})";
        ws.Cell(s2TotalRow, 42).FormulaA1 = $"=SUM(AP{s2Start}:AP{s2End})";
        CleanRangeMerge(ws, s2TotalRow, 2, s2TotalRow, 17);
        ws.Range(s2TotalRow, 2, s2TotalRow, 17).Merge();

        // 4. Scope 3:
        CleanRangeMerge(ws, s3Start, 5, s3End, 16);
        ws.Range(s3Start, 5, s3End, 16).Merge();
        ws.Cell(s3Start, 5).Value = "-";
        ws.Cell(s3Start, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s3Start, 5).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        CleanRangeMerge(ws, s3Start, 27, s3End, 40);
        ws.Range(s3Start, 27, s3End, 40).Merge();
        ws.Cell(s3Start, 27).Value = "-";
        ws.Cell(s3Start, 27).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s3Start, 27).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        for (int i = 0; i < s3RowCount; i++)
        {
            int r = s3Start + i;
            if (i < s3Lines.Count)
            {
                PopulateSingleRow(ws, r, s3Lines[i], isScope1: false, isSeparate: false, layout);
            }
            else
            {
                ClearUnusedRow(ws, r, isScope1: false, isSeparate: false, layout);
            }
        }
        // Scope 3 Total Row
        ws.Cell(s3TotalRow, 41).FormulaA1 = $"=SUM(AO{s3Start}:AO{s3End})";
        ws.Cell(s3TotalRow, 42).FormulaA1 = $"=SUM(AP{s3Start}:AP{s3End})";
        CleanRangeMerge(ws, s3TotalRow, 2, s3TotalRow, 17);
        ws.Range(s3TotalRow, 2, s3TotalRow, 17).Merge();

        // 5. Summary Rows (Scope 1+2 Total, Gross Total, Signer)
        int[] gasCols = { 27, 28, 30, 31, 32, 33, 35, 36, 37, 38, 39, 40 };
        foreach (int c in gasCols)
        {
            var colLetter = ws.Cell(s1s2TotalRow, c).Address.ColumnLetter;
            ws.Cell(s1s2TotalRow, c).FormulaA1 = $"={colLetter}{s1TotalRow}+{colLetter}{s2TotalRow}";
            ws.Cell(grossTotalRow, c).FormulaA1 = $"={colLetter}{s1TotalRow}+{colLetter}{s2TotalRow}+{colLetter}{s3TotalRow}";
        }
        ws.Cell(s1s2TotalRow, 41).FormulaA1 = $"=AO{s1TotalRow}+AO{s2TotalRow}";
        ws.Cell(s1s2TotalRow, 43).FormulaA1 = $"=SUM(AQ{s1Start}:AQ{s1End})+SUM(AQ{s2Start}:AQ{s2End})";
        ws.Cell(s1s2TotalRow, 44).FormulaA1 = $"=SUM(AR{s1Start}:AR{s1End})+SUM(AR{s2Start}:AR{s2End})+SUM(AR{s3Start}:AR{s3End})";

        ws.Cell(grossTotalRow, 41).FormulaA1 = $"=AO{s1TotalRow}+AO{s2TotalRow}+AO{s3TotalRow}";

        // Signer row
        CleanRangeMerge(ws, signerRow, 2, signerRow, 17);
        ws.Range(signerRow, 2, signerRow, 17).Merge();
        ws.Cell(signerRow, 1).Value = "จัดทำโดย";
        ws.Cell(signerRow, 2).FormulaA1 = "='Fr-01'!G4";

        // 6. Merge Column A for each Scope block (Data rows + Total row)
        CleanRangeMerge(ws, s1Start, 1, s1TotalRow, 1);
        ws.Range(s1Start, 1, s1TotalRow, 1).Merge();
        ws.Cell(s1Start, 1).Value = "ขอบเขต 1";
        ws.Cell(s1Start, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s1Start, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        CleanRangeMerge(ws, sepStart, 1, sepTotalRow, 1);
        ws.Range(sepStart, 1, sepTotalRow, 1).Merge();
        ws.Cell(sepStart, 1).Value = "การปล่อย GHG โดยตรงที่ทำการรายงานแยก";
        ws.Cell(sepStart, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(sepStart, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        CleanRangeMerge(ws, s2Start, 1, s2TotalRow, 1);
        ws.Range(s2Start, 1, s2TotalRow, 1).Merge();
        ws.Cell(s2Start, 1).Value = "ขอบเขต 2";
        ws.Cell(s2Start, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s2Start, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        CleanRangeMerge(ws, s3Start, 1, s3TotalRow, 1);
        ws.Range(s3Start, 1, s3TotalRow, 1).Merge();
        ws.Cell(s3Start, 1).Value = "ขอบเขต 3";
        ws.Cell(s3Start, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(s3Start, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        return layout;
    }

    private void PopulateSingleRow(IXLWorksheet ws, int row, ActivityAuditLineDto line, bool isScope1, bool isSeparate, Fr04BlockLayout layout)
    {
        // [B-03] Adjusted Activity Quantity: Quantity * Ratio * (Share% / 100)
        decimal adjustedQty = line.Quantity * line.Ratio * (line.SharePercent / 100m);

        ws.Cell(row, 2).Value = $"{line.ActivityGroupCode} - {line.TopicTitle}";
        ws.Cell(row, 3).Value = line.UnitName;
        ws.Cell(row, 4).Value = (double)adjustedQty;

        if (isScope1)
        {
            // [GHG-01] คอลัมน์ก๊าซต้องตรงกับ header row 9 ของ template:
            // E=CO2, F=FossilCH4, G=CH4, H=N2O, I=SF6, J=NF3, K=HFCs, L=PFCs, M=gwpHFCs, N=gwpPFCs
            ws.Cell(row,  5).Value = (double)line.CO2;        // E
            ws.Cell(row,  6).Value = (double)line.FossilCH4;  // F
            ws.Cell(row,  7).Value = (double)line.CH4;        // G
            ws.Cell(row,  8).Value = (double)line.N2O;        // H
            ws.Cell(row,  9).Value = (double)line.SF6;        // I
            ws.Cell(row, 10).Value = (double)line.NF3;        // J
            ws.Cell(row, 11).Value = (double)line.HFCs;       // K
            ws.Cell(row, 12).Value = (double)line.PFCs;       // L
            ws.Cell(row, 13).Value = (double)line.gwpHFCs;    // M
            ws.Cell(row, 14).Value = (double)line.gwpPFCs;    // N
            ws.Cell(row, 17).FormulaA1 = $"=(E{row}*$AV$8)+(F{row}*$AV$9)+(G{row}*$AV$10)+(H{row}*$AV$11)+(I{row}*$AV$12)+(J{row}*$AV$13)+(K{row}*M{row})+(L{row}*N{row})";

            // Gas Breakdown Columns AA..AM (27..39)
            ws.Cell(row, 27).FormulaA1 = $"=D{row}*E{row}/1000";
            ws.Cell(row, 28).FormulaA1 = $"=D{row}*F{row}/1000";
            ws.Cell(row, 29).FormulaA1 = $"=D{row}*G{row}/1000";
            ws.Cell(row, 30).FormulaA1 = $"=D{row}*H{row}/1000";
            ws.Cell(row, 31).FormulaA1 = $"=D{row}*I{row}/1000";
            ws.Cell(row, 32).FormulaA1 = $"=D{row}*J{row}/1000";
            ws.Cell(row, 33).FormulaA1 = $"=D{row}*F{row}*$AV$9/1000";
            ws.Cell(row, 34).FormulaA1 = $"=D{row}*G{row}*$AV$10/1000";
            ws.Cell(row, 35).FormulaA1 = $"=D{row}*H{row}*$AV$11/1000";
            ws.Cell(row, 36).FormulaA1 = $"=D{row}*I{row}*$AV$12/1000";
            ws.Cell(row, 37).FormulaA1 = $"=D{row}*J{row}*$AV$13/1000";
            ws.Cell(row, 38).FormulaA1 = $"=D{row}*K{row}*M{row}/1000";
            ws.Cell(row, 39).FormulaA1 = $"=D{row}*L{row}*N{row}/1000";

            // Ratios
            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";
            ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1TotalRow}";
            ws.Cell($"AQ{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1S2TotalRow}";
            ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
        }
        else if (isSeparate)
        {
            ws.Cell(row, 5).Value = (double)line.EF;
            ws.Cell(row, 17).FormulaA1 = $"=(E{row}*$AV$8)+(O{row}*P{row})";

            ws.Cell(row, 27).FormulaA1 = $"=D{row}*E{row}*$AV$8/1000";
            ws.Cell(row, 40).FormulaA1 = $"=D{row}*O{row}*P{row}/1000";

            // Ratios
            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";
            ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.SepTotalRow}";
            ws.Cell($"AQ{row}").Clear(XLClearOptions.Contents);
            ws.Cell($"AR{row}").Clear(XLClearOptions.Contents);
        }
        else
        {
            // Scope 2 or Scope 3
            ws.Cell(row, 17).Value = (double)line.EF;
            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";

            bool isScope2 = row >= layout.S2Start && row <= layout.S2End;
            if (isScope2)
            {
                ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S2TotalRow}";
                ws.Cell($"AQ{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1S2TotalRow}";
                ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
            }
            else
            {
                ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S3TotalRow}";
                ws.Cell($"AQ{row}").Clear(XLClearOptions.Contents);
                ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
            }
        }

        // [GHG-20] คอลัมน์ "ที่มา" ของ template: R=Self collct, S=Supplier, T=TH LCI DB,
        //          U=TGO EF, V=Thai Res., W=Int. DB, X=Other  (ฟอนต์ Tahoma ใช้ "✓" ได้ปกติ)
        const string MARK = "✓";
        if (line.SrcSelfCollect) ws.Cell(row, 18).Value = MARK;  // R
        if (line.SrcSupplier)    ws.Cell(row, 19).Value = MARK;  // S
        if (line.SrcThLciDb)     ws.Cell(row, 20).Value = MARK;  // T
        if (line.SrcTgoEf)       ws.Cell(row, 21).Value = MARK;  // U
        if (line.SrcThaiRes)     ws.Cell(row, 22).Value = MARK;  // V
        if (line.SrcIntDb)       ws.Cell(row, 23).Value = MARK;  // W
        if (line.SrcOther)       ws.Cell(row, 24).Value = MARK;  // X

        // ไม่มีธงใดถูกตั้งเลย = EF ที่ผู้ใช้สร้างเองโดยไม่ระบุที่มา → ลง Other
        if (!line.SrcSelfCollect && !line.SrcSupplier && !line.SrcThLciDb && !line.SrcTgoEf
            && !line.SrcThaiRes && !line.SrcIntDb && !line.SrcOther)
        {
            ws.Cell(row, 24).Value = MARK;  // X = Other
        }

        // [GHG-21] Y = Subsitute, Z = แหล่งอ้างอิง (template มีคอลัมน์รองรับแต่เดิมไม่เคยเขียน)
        if (line.SrcSubstitute) ws.Cell(row, 25).Value = MARK;               // Y
        if (!string.IsNullOrWhiteSpace(line.EfRef))
            ws.Cell(row, 26).Value = line.EfRef.Length > 255                  // Z
                ? line.EfRef.Substring(0, 252) + "..."
                : line.EfRef;
    }

    private void ClearUnusedRow(IXLWorksheet ws, int row, bool isScope1, bool isSeparate, Fr04BlockLayout layout)
    {
        ws.Cell(row, 2).Clear(XLClearOptions.Contents);
        ws.Cell(row, 3).Clear(XLClearOptions.Contents);
        ws.Cell(row, 4).Value = 0;

        if (isScope1)
        {
            ws.Cell(row, 5).Clear(XLClearOptions.Contents);
            for (int c = 6; c <= 16; c++) ws.Cell(row, c).Clear(XLClearOptions.Contents);
            ws.Cell(row, 17).FormulaA1 = $"=(E{row}*$AV$8)+(F{row}*$AV$9)+(G{row}*$AV$10)+(H{row}*$AV$11)+(I{row}*$AV$12)+(J{row}*$AV$13)+(K{row}*M{row})+(L{row}*N{row})";

            ws.Cell(row, 27).FormulaA1 = $"=D{row}*E{row}/1000";
            ws.Cell(row, 28).FormulaA1 = $"=D{row}*F{row}/1000";
            ws.Cell(row, 29).FormulaA1 = $"=D{row}*G{row}/1000";
            ws.Cell(row, 30).FormulaA1 = $"=D{row}*H{row}/1000";
            ws.Cell(row, 31).FormulaA1 = $"=D{row}*I{row}/1000";
            ws.Cell(row, 32).FormulaA1 = $"=D{row}*J{row}/1000";
            ws.Cell(row, 33).FormulaA1 = $"=D{row}*F{row}*$AV$9/1000";
            ws.Cell(row, 34).FormulaA1 = $"=D{row}*G{row}*$AV$10/1000";
            ws.Cell(row, 35).FormulaA1 = $"=D{row}*H{row}*$AV$11/1000";
            ws.Cell(row, 36).FormulaA1 = $"=D{row}*I{row}*$AV$12/1000";
            ws.Cell(row, 37).FormulaA1 = $"=D{row}*J{row}*$AV$13/1000";
            ws.Cell(row, 38).FormulaA1 = $"=D{row}*K{row}*M{row}/1000";
            ws.Cell(row, 39).FormulaA1 = $"=D{row}*L{row}*N{row}/1000";

            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";
            ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1TotalRow}";
            ws.Cell($"AQ{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1S2TotalRow}";
            ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
        }
        else if (isSeparate)
        {
            ws.Cell(row, 5).Clear(XLClearOptions.Contents);
            ws.Cell(row, 17).FormulaA1 = $"=(E{row}*$AV$8)+(O{row}*P{row})";

            ws.Cell(row, 27).FormulaA1 = $"=D{row}*E{row}*$AV$8/1000";
            ws.Cell(row, 40).FormulaA1 = $"=D{row}*O{row}*P{row}/1000";

            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";
            ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.SepTotalRow}";
            ws.Cell($"AQ{row}").Clear(XLClearOptions.Contents);
            ws.Cell($"AR{row}").Clear(XLClearOptions.Contents);
        }
        else
        {
            ws.Cell(row, 17).Clear(XLClearOptions.Contents);
            ws.Cell($"AO{row}").FormulaA1 = $"=D{row}*Q{row}/1000";

            bool isScope2 = row >= layout.S2Start && row <= layout.S2End;
            if (isScope2)
            {
                ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S2TotalRow}";
                ws.Cell($"AQ{row}").FormulaA1 = $"=AO{row}/$AO${layout.S1S2TotalRow}";
                ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
            }
            else
            {
                ws.Cell($"AP{row}").FormulaA1 = $"=AO{row}/$AO${layout.S3TotalRow}";
                ws.Cell($"AQ{row}").Clear(XLClearOptions.Contents);
                ws.Cell($"AR{row}").FormulaA1 = $"=AO{row}/$AO${layout.GrossTotalRow}";
            }
        }

        // [GHG-20] [GHG-21] ล้างคอลัมน์ที่มาและอ้างอิงให้ครอบคลุม R..Z (18..26)
        for (int c = 18; c <= 26; c++) ws.Cell(row, c).Clear(XLClearOptions.Contents);
    }

    /// <summary>
    /// [2026-09-18][Claude] เขียนคอลัมน์ AS ของ Fr-04.2 "ระบุความแตกต่างระหว่างการรายงานปริมาณก๊าซเรือนกระจก
    /// ของปีฐานและปีปัจจุบัน" เป็นข้อความสรุปรายแถว (ปริมาณ / EF / ปริมาณการปล่อย) ไม่ใช่ตัวเลขผลต่างอย่างเดียว
    /// เพราะ อบก. ต้องการเหตุผลของความแตกต่าง ผู้ใช้แก้ทับข้อความนี้ได้เองใน Excel
    /// </summary>
    private void WriteFr042DiffColumn(IXLWorksheet ws, CfoAuditReportDto baseReport, CfoAuditReportDto? currentReport, Fr04BlockLayout baseLayout)
    {
        const int COL_DIFF = 45;   // AS
        int summaryRow = baseLayout.GrossTotalRow;

        (int start, int end, Func<ActivityAuditLineDto, bool> filter)[] blocks =
        {
            (baseLayout.S1Start, baseLayout.S1End, l => l.ScopeNo == eTgoScopeNo.Scope1),
            (baseLayout.SepStart, baseLayout.SepEnd, l => (int)l.ScopeNo == 4),
            (baseLayout.S2Start, baseLayout.S2End, l => l.ScopeNo == eTgoScopeNo.Scope2),
            (baseLayout.S3Start, baseLayout.S3End, l => l.ScopeNo == eTgoScopeNo.Scope3),
        };

        foreach (var (start, end, _) in blocks)
        {
            for (int r = start; r <= end; r++) ws.Cell(r, COL_DIFF).Clear(XLClearOptions.Contents);
        }
        ws.Cell(summaryRow, COL_DIFF).Clear(XLClearOptions.Contents);

        if (currentReport == null) return;

        // ไม่ได้เลือกปีฐาน → ระบบใช้ผลของปีคำนวณเป็นปีฐาน ทั้งชีตจึงเหมือนกันทุกช่อง
        if (ReferenceEquals(baseReport, currentReport) || baseReport.Year == currentReport.Year)
        {
            ws.Cell(summaryRow, COL_DIFF).Value =
                $"องค์กรไม่ได้กำหนดปีฐาน ระบบจึงใช้ข้อมูลปีรายงาน {currentReport.Year} เป็นปีฐาน " +
                "บัญชีรายการของทั้งสองปีจึงเหมือนกันทุกรายการ และไม่มีความแตกต่างให้รายงาน";
            return;
        }

        static string Key(ActivityAuditLineDto l) => $"{l.TopicId}|{l.ActivityGroupId}|{l.SourceId}";

        var currentByKey = new Dictionary<string, ActivityAuditLineDto>();
        foreach (var l in currentReport.AuditLines) currentByKey[Key(l)] = l;

        var matchedKeys = new HashSet<string>();

        foreach (var (start, end, filter) in blocks)
        {
            var lines = baseReport.AuditLines.Where(filter).ToList();
            int row = start;

            // [2026-09-25][Gemini] ทุกบรรทัดถูกกระจาย 1 แถวต่อ 1 รายการโดยไม่มีการยุบรวม จึงเทียบรายบรรทัดได้ 100%
            for (int i = 0; i < lines.Count; i++, row++)
            {
                var baseLine = lines[i];
                var key = Key(baseLine);

                if (!currentByKey.TryGetValue(key, out var currentLine))
                {
                    ws.Cell(row, COL_DIFF).Value =
                        $"ไม่มีรายการนี้ในปีรายงาน {currentReport.Year} — ยกเลิกกิจกรรมหรือไม่มีข้อมูลในรอบรายงาน " +
                        $"(ปีฐาน {baseLine.Total_tCO2e:N4} tCO2e)";
                    continue;
                }

                matchedKeys.Add(key);
                ws.Cell(row, COL_DIFF).Value = BuildFr042DiffText(baseLine, currentLine);
            }
        }

        // สรุปภาพรวม + รายการที่มีเฉพาะปีรายงาน
        var newLines = currentReport.AuditLines.Where(l => !matchedKeys.Contains(Key(l))).ToList();
        decimal baseTotal = baseReport.TotalGrossEmissions_tCO2e;
        decimal curTotal = currentReport.TotalGrossEmissions_tCO2e;

        string summary =
            $"ภาพรวม: ปีฐาน {baseReport.Year} {baseTotal:N4} tCO2e → ปีรายงาน {currentReport.Year} {curTotal:N4} tCO2e " +
            $"({FormatSignedPercent(baseTotal, curTotal)})";

        if (newLines.Count > 0)
        {
            var names = string.Join(", ", newLines.Take(5).Select(l => $"{l.ActivityGroupCode} - {l.TopicTitle}"));
            string more = newLines.Count > 5 ? $" และอีก {newLines.Count - 5} รายการ" : string.Empty;
            summary += $" | รายการที่มีเฉพาะปีรายงาน {newLines.Count} รายการ: {names}{more}";
        }

        ws.Cell(summaryRow, COL_DIFF).Value = summary;
    }

    /// <summary>[2026-09-18][Claude] ข้อความเทียบรายบรรทัดระหว่างปีฐานกับปีรายงาน (ปริมาณ / EF / ปริมาณการปล่อย)</summary>
    private static string BuildFr042DiffText(ActivityAuditLineDto baseLine, ActivityAuditLineDto currentLine)
    {
        string unit = !string.IsNullOrWhiteSpace(baseLine.UnitName) ? baseLine.UnitName : currentLine.UnitName;

        // [2026-09-18][Claude] ต้องใช้ "ปริมาณที่ปรับแล้ว" (Quantity x Ratio x Share%) ให้ตรงกับคอลัมน์ D ของชีตเดียวกัน
        decimal baseQty = baseLine.Quantity * baseLine.Ratio * (baseLine.SharePercent / 100m);
        decimal currentQty = currentLine.Quantity * currentLine.Ratio * (currentLine.SharePercent / 100m);

        string qtyPart = $"ปริมาณ {baseQty:N2} → {currentQty:N2} {unit} ({FormatSignedPercent(baseQty, currentQty)})";

        string efPart = baseLine.EF == currentLine.EF
            ? $"EF เท่าเดิม {baseLine.EF:N6}"
            : $"EF {baseLine.EF:N6} → {currentLine.EF:N6} ({FormatSignedPercent(baseLine.EF, currentLine.EF)})";

        if (!string.Equals(baseLine.EfRef?.Trim(), currentLine.EfRef?.Trim(), StringComparison.Ordinal))
        {
            efPart += " [เปลี่ยนแหล่งอ้างอิง EF]";
        }

        string emissionPart = $"การปล่อย {baseLine.Total_tCO2e:N4} → {currentLine.Total_tCO2e:N4} tCO2e ({FormatSignedPercent(baseLine.Total_tCO2e, currentLine.Total_tCO2e)})";

        return $"{qtyPart} | {efPart} | {emissionPart}";
    }

    /// <summary>[2026-09-18][Claude] % การเปลี่ยนแปลงพร้อมเครื่องหมาย รองรับกรณีฐานเป็น 0</summary>
    private static string FormatSignedPercent(decimal baseValue, decimal currentValue)
    {
        if (baseValue == 0m) return currentValue == 0m ? "ไม่เปลี่ยนแปลง" : "เพิ่มจาก 0";

        decimal pct = (currentValue - baseValue) * 100m / baseValue;
        if (pct == 0m) return "ไม่เปลี่ยนแปลง";

        string sign = pct > 0m ? "+" : string.Empty;
        return $"{sign}{pct:N2}%";
    }

    private void ClearFr04Sheet(XLWorkbook workbook, string sheetName, int baseYear)
    {
        var ws = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals(sheetName, StringComparison.OrdinalIgnoreCase));
        if (ws == null) return;

        var defaultLayout = new Fr04BlockLayout(10, 23, 24, 25, 29, 30, 31, 34, 35, 36, 42, 43, 44, 45, 46);

        // [2026-09-18][Claude] ล้างคอลัมน์ AS (ระบุความแตกต่างปีฐาน/ปีปัจจุบัน) ของรอบก่อนหน้าด้วย
        for (int r = 10; r <= 42; r++) ws.Cell(r, 45).Clear(XLClearOptions.Contents);
        ws.Cell(45, 45).Clear(XLClearOptions.Contents);

        // Clear Scope 1 (10..23)
        for (int r = 10; r <= 23; r++)
        {
            ClearUnusedRow(ws, r, isScope1: true, isSeparate: false, defaultLayout);
        }

        // Clear Separate (25..29)
        for (int r = 25; r <= 29; r++)
        {
            ClearUnusedRow(ws, r, isScope1: false, isSeparate: true, defaultLayout);
        }

        // Clear Scope 2 (31..34)
        for (int r = 31; r <= 34; r++)
        {
            ClearUnusedRow(ws, r, isScope1: false, isSeparate: false, defaultLayout);
        }

        // Clear Scope 3 (36..42)
        for (int r = 36; r <= 42; r++)
        {
            ClearUnusedRow(ws, r, isScope1: false, isSeparate: false, defaultLayout);
        }
    }

    #region [GHG-02] Post-Export Verification
    private void VerifyWorkbookScopeTotals(XLWorkbook workbook, CfoAuditReportDto calcReport, CfoAuditReportDto? baseReport, int baseYear, Fr04BlockLayout calcLayout, Fr04BlockLayout? baseLayout)
    {
        var wsCalc = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-04.1", StringComparison.OrdinalIgnoreCase));
        if (wsCalc != null)
        {
            AssertScopeTotals(wsCalc, calcLayout.S1Start, calcLayout.S1End, calcReport.TotalScope1_tCO2e, "Scope 1", "Fr-04.1", calcReport);
            AssertScopeTotals(wsCalc, calcLayout.SepStart, calcLayout.SepEnd, calcReport.TotalSeparate_tCO2e, "Separate Report", "Fr-04.1", calcReport);
            AssertScopeTotals(wsCalc, calcLayout.S2Start, calcLayout.S2End, calcReport.TotalScope2_tCO2e, "Scope 2", "Fr-04.1", calcReport);
            AssertScopeTotals(wsCalc, calcLayout.S3Start, calcLayout.S3End, calcReport.TotalScope3_tCO2e, "Scope 3", "Fr-04.1", calcReport);
        }

        if (baseReport != null && baseReport.AuditLines.Any() && baseLayout != null)
        {
            var wsBase = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Fr-04.2", StringComparison.OrdinalIgnoreCase));
            if (wsBase != null)
            {
                AssertScopeTotals(wsBase, baseLayout.S1Start, baseLayout.S1End, baseReport.TotalScope1_tCO2e, "Scope 1", "Fr-04.2", baseReport);
                AssertScopeTotals(wsBase, baseLayout.SepStart, baseLayout.SepEnd, baseReport.TotalSeparate_tCO2e, "Separate Report", "Fr-04.2", baseReport);
                AssertScopeTotals(wsBase, baseLayout.S2Start, baseLayout.S2End, baseReport.TotalScope2_tCO2e, "Scope 2", "Fr-04.2", baseReport);
                AssertScopeTotals(wsBase, baseLayout.S3Start, baseLayout.S3End, baseReport.TotalScope3_tCO2e, "Scope 3", "Fr-04.2", baseReport);
            }
        }
    }

    private void AssertScopeTotals(IXLWorksheet ws, int startRow, int endRow, decimal expectedTotalTon, string scopeLabel, string sheetName, CfoAuditReportDto report)
    {
        decimal actualSumTon = 0m;
        bool hasComputableValue = false;

        for (int r = startRow; r <= endRow; r++)
        {
            var cell = ws.Cell($"AO{r}");
            try
            {
                if (cell.TryGetValue(out decimal cellVal))
                {
                    actualSumTon += cellVal;
                    hasComputableValue = true;
                }
                else if (cell.TryGetValue(out double dblVal))
                {
                    actualSumTon += (decimal)dblVal;
                    hasComputableValue = true;
                }
                else if (!cell.IsEmpty() && decimal.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVal))
                {
                    actualSumTon += parsedVal;
                    hasComputableValue = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{Sheet}] Could not evaluate formula for cell AO{Row}", sheetName, r);
            }
        }

        if (!hasComputableValue && expectedTotalTon > 0)
        {
            string msg = $"[{sheetName}] {scopeLabel} UNVERIFIED: ClosedXML returned no computable values for column AO ({startRow}..{endRow}). Expected: {expectedTotalTon:N4} tCO2e.";
            _logger.LogError(msg);
            MarkExportUnverified(report, msg);
            return;
        }

        decimal diff = Math.Abs(actualSumTon - expectedTotalTon);
        decimal tolerance = Math.Max(0.01m, expectedTotalTon * 0.001m);

        if (diff > tolerance)
        {
            string msg = $"[{sheetName}] {scopeLabel} Export Verification MISMATCH: Expected {expectedTotalTon:N4} tCO2e, Actual in Excel column AO sum {actualSumTon:N4} tCO2e (Diff: {diff:N4} > Tolerance: {tolerance:N4})";
            _logger.LogError(msg);
            MarkExportUnverified(report, msg);
        }
        else
        {
            _logger.LogInformation("[{Sheet}] {Scope} Export Verification PASS: Expected {Expected:N4} tCO2e, Actual {Actual:N4} tCO2e", sheetName, scopeLabel, expectedTotalTon, actualSumTon);
        }
    }
    #endregion

    private void PopulateAuditRegistrySheet(XLWorkbook workbook, CfoAuditReportDto report)
    {
        var wsAudit = workbook.Worksheets.FirstOrDefault(w => w.Name.Equals("Activity Data Audit Registry", StringComparison.OrdinalIgnoreCase));
        if (wsAudit == null)
        {
            wsAudit = workbook.Worksheets.Add("Activity Data Audit Registry");
        }
        else
        {
            // [GHG-22] ข้อยกเว้นที่ตั้งใจ: ชีตนี้ระบบสร้างขึ้นเองทั้งชีต ไม่ใช่ชีตของ template
            //           จึงล้างทั้งหมด (XLClearOptions.All) ได้ตามเดิม
            wsAudit.Clear();
        }

        wsAudit.ShowGridLines = true;
        wsAudit.Cell(1, 1).Value = $"Activity Data Audit Registry | Organization: {report.OrganizationTitle} | Year: {(report.Year > 0 ? report.Year.ToString() : "All")} | Consolidation: {report.ConsolidationApproach} | GWP: {report.GwpVersionDisplay}";
        wsAudit.Range(1, 1, 1, 23).Merge();
        wsAudit.Cell(1, 1).Style.Font.Bold = true;
        wsAudit.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");

        int ar = 2;
        wsAudit.Cell(ar, 1).Value = "#";
        wsAudit.Cell(ar, 2).Value = "Scope";
        wsAudit.Cell(ar, 3).Value = "ISO Cat";
        wsAudit.Cell(ar, 4).Value = "Activity Group Code";
        wsAudit.Cell(ar, 5).Value = "Activity Group Title";
        wsAudit.Cell(ar, 6).Value = "Topic / Activity Name";
        wsAudit.Cell(ar, 7).Value = "Master Emission Source";
        wsAudit.Cell(ar, 8).Value = "Recorded Qty";
        wsAudit.Cell(ar, 9).Value = "Activity Unit";
        wsAudit.Cell(ar, 10).Value = "Conversion Ratio";
        wsAudit.Cell(ar, 11).Value = "EF Unit";
        wsAudit.Cell(ar, 12).Value = "Converted Qty";
        wsAudit.Cell(ar, 13).Value = "EF (kgCO2e/unit)";
        wsAudit.Cell(ar, 14).Value = "Share %";
        wsAudit.Cell(ar, 15).Value = "Emissions (kgCO2e)";
        wsAudit.Cell(ar, 16).Value = "Emissions (tCO2e)";
        wsAudit.Cell(ar, 17).Value = "EF Valid From";
        wsAudit.Cell(ar, 18).Value = "EF Valid To";
        wsAudit.Cell(ar, 19).Value = "EF Validity Status";
        wsAudit.Cell(ar, 20).Value = "EF Reference";
        wsAudit.Cell(ar, 21).Value = "EF Database Origin";
        wsAudit.Cell(ar, 22).Value = "Attached Files";
        wsAudit.Cell(ar, 23).Value = "Reconcile Diff";

        wsAudit.Range(ar, 1, ar, 23).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
        wsAudit.Range(ar, 1, ar, 23).Style.Font.FontColor = XLColor.White;
        wsAudit.Range(ar, 1, ar, 23).Style.Font.Bold = true;

        int rowIdx = 1;
        int dataStartRow = ar + 1;
        foreach (var line in report.AuditLines)
        {
            ar++;
            wsAudit.Cell(ar, 1).Value = rowIdx++;
            wsAudit.Cell(ar, 2).Value = line.ScopeNo.ToString();
            wsAudit.Cell(ar, 3).Value = $"Cat {line.IsoCategoryNo}";
            wsAudit.Cell(ar, 4).Value = line.ActivityGroupCode;
            wsAudit.Cell(ar, 5).Value = line.ActivityGroupTitle;
            wsAudit.Cell(ar, 6).Value = line.TopicTitle;
            wsAudit.Cell(ar, 7).Value = line.SourceName;
            wsAudit.Cell(ar, 8).Value = line.Quantity;
            wsAudit.Cell(ar, 8).Style.NumberFormat.Format = "#,##0.00";
            wsAudit.Cell(ar, 9).Value = line.ActivityUnitName;
            wsAudit.Cell(ar, 10).Value = line.Ratio;
            wsAudit.Cell(ar, 10).Style.NumberFormat.Format = "#,##0.0000";
            wsAudit.Cell(ar, 11).Value = line.EfUnitName;
            wsAudit.Cell(ar, 12).FormulaA1 = $"H{ar}*J{ar}*N{ar}";
            wsAudit.Cell(ar, 12).Style.NumberFormat.Format = "#,##0.00";
            wsAudit.Cell(ar, 13).Value = line.EF;
            wsAudit.Cell(ar, 13).Style.NumberFormat.Format = "#,##0.0000";
            wsAudit.Cell(ar, 14).Value = line.SharePercent / 100m;
            wsAudit.Cell(ar, 14).Style.NumberFormat.Format = "0.0%";
            wsAudit.Cell(ar, 15).Value = line.KgCO2e;
            wsAudit.Cell(ar, 15).Style.NumberFormat.Format = "#,##0.00";
            wsAudit.Cell(ar, 16).Value = line.Total_tCO2e;
            wsAudit.Cell(ar, 16).Style.NumberFormat.Format = "#,##0.0000";
            wsAudit.Cell(ar, 17).Value = line.EfValidFrom.HasValue ? line.EfValidFrom.Value.ToString("dd/MM/yyyy") : "-";
            wsAudit.Cell(ar, 18).Value = line.EfValidTo.HasValue ? line.EfValidTo.Value.ToString("dd/MM/yyyy") : "-";
            wsAudit.Cell(ar, 19).Value = line.EfValidityStatus;
            wsAudit.Cell(ar, 20).Value = line.EfRef;
            wsAudit.Cell(ar, 21).Value = line.OriginSource;
            wsAudit.Cell(ar, 22).Value = line.AttachedFilesCount;
            wsAudit.Cell(ar, 23).FormulaA1 = $"(L{ar}*M{ar})-O{ar}";
            wsAudit.Cell(ar, 23).Style.NumberFormat.Format = "#,##0.0000";

            if (line.EfValidityStatus != "Valid" && line.EfValidityStatus != "NoValidityDates")
            {
                wsAudit.Cell(ar, 19).Style.Font.FontColor = XLColor.Red;
                wsAudit.Cell(ar, 19).Style.Font.Bold = true;
            }
        }

        // Summary row
        if (ar >= dataStartRow)
        {
            ar++;
            wsAudit.Cell(ar, 6).Value = "TOTAL SUMMARY";
            wsAudit.Cell(ar, 6).Style.Font.Bold = true;
            wsAudit.Cell(ar, 15).FormulaA1 = $"SUM(O{dataStartRow}:O{ar - 1})";
            wsAudit.Cell(ar, 15).Style.NumberFormat.Format = "#,##0.00";
            wsAudit.Cell(ar, 15).Style.Font.Bold = true;
            wsAudit.Cell(ar, 16).FormulaA1 = $"SUM(P{dataStartRow}:P{ar - 1})";
            wsAudit.Cell(ar, 16).Style.NumberFormat.Format = "#,##0.0000";
            wsAudit.Cell(ar, 16).Style.Font.Bold = true;
            wsAudit.Cell(ar, 23).FormulaA1 = $"SUM(W{dataStartRow}:W{ar - 1})";
            wsAudit.Cell(ar, 23).Style.NumberFormat.Format = "#,##0.0000";
            wsAudit.Cell(ar, 23).Style.Font.Bold = true;
            wsAudit.Range(ar, 1, ar, 23).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
        }

        // [GHG-12] Record EF Validity Acknowledgment Reason in Audit Registry
        if (!string.IsNullOrWhiteSpace(report.ExpiryAcknowledgmentReason))
        {
            ar += 2;
            wsAudit.Cell(ar, 1).Value = $"[Audit Note] EF Validity Acknowledgment (เหตุผลการยอมรับการใช้ EF): {report.ExpiryAcknowledgmentReason}";
            wsAudit.Cell(ar, 1).Style.Font.Italic = true;
            wsAudit.Cell(ar, 1).Style.Font.FontColor = XLColor.DarkSlateGray;
            wsAudit.Range(ar, 1, ar, 23).Merge();
        }

        wsAudit.Columns(1, 23).AdjustToContents();
    }

    private byte[] GenerateFallbackWorkbook(CfoAuditReportDto report)
    {
        using var workbook = new XLWorkbook();

        // 1. Executive Cover & Summary Sheet
        var wsCover = workbook.Worksheets.Add("CFO Executive Summary");
        wsCover.ShowGridLines = true;

        // Title Block
        wsCover.Cell("B2").Value = "CARBON FOOTPRINT FOR ORGANIZATION (CFO) & ISO 14064-1 REPORT";
        wsCover.Cell("B2").Style.Font.Bold = true;
        wsCover.Cell("B2").Style.Font.FontSize = 16;
        wsCover.Cell("B2").Style.Font.FontColor = XLColor.FromHtml("#1b4d3e");

        wsCover.Cell("B3").Value = $"Organization: {report.OrganizationTitle} ({report.OrganizationCode}) | Level: {report.LevelDisplay}";
        wsCover.Cell("B3").Style.Font.Bold = true;
        wsCover.Cell("B3").Style.Font.FontSize = 12;

        wsCover.Cell("B4").Value = $"Reporting Inventory Year: {(report.Year > 0 ? report.Year.ToString() : "All Historical Years")} | Consolidation Approach: {report.ConsolidationApproach}";
        wsCover.Cell("B4").Style.Font.Italic = true;

        // Key Metrics Summary Table
        int r = 6;
        wsCover.Cell(r, 2).Value = "GHG Scope / Category";
        wsCover.Cell(r, 3).Value = "Emissions (tCO2e)";
        wsCover.Cell(r, 4).Value = "Share (%)";
        wsCover.Range(r, 2, r, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#003366");
        wsCover.Range(r, 2, r, 4).Style.Font.FontColor = XLColor.White;
        wsCover.Range(r, 2, r, 4).Style.Font.Bold = true;

        r++;
        wsCover.Cell(r, 2).Value = "Scope 1 (Direct GHG Emissions)";
        wsCover.Cell(r, 3).Value = report.TotalScope1_tCO2e;
        wsCover.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        wsCover.Cell(r, 4).Value = report.TotalGrossEmissions_tCO2e > 0 ? (report.TotalScope1_tCO2e / report.TotalGrossEmissions_tCO2e) : 0m;
        wsCover.Cell(r, 4).Style.NumberFormat.Format = "0.0%";

        r++;
        wsCover.Cell(r, 2).Value = "Scope 2 (Indirect Energy GHG Emissions)";
        wsCover.Cell(r, 3).Value = report.TotalScope2_tCO2e;
        wsCover.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        wsCover.Cell(r, 4).Value = report.TotalGrossEmissions_tCO2e > 0 ? (report.TotalScope2_tCO2e / report.TotalGrossEmissions_tCO2e) : 0m;
        wsCover.Cell(r, 4).Style.NumberFormat.Format = "0.0%";

        r++;
        wsCover.Cell(r, 2).Value = "Scope 3 (Value Chain / Other Indirect)";
        wsCover.Cell(r, 3).Value = report.TotalScope3_tCO2e;
        wsCover.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        wsCover.Cell(r, 4).Value = report.TotalGrossEmissions_tCO2e > 0 ? (report.TotalScope3_tCO2e / report.TotalGrossEmissions_tCO2e) : 0m;
        wsCover.Cell(r, 4).Style.NumberFormat.Format = "0.0%";

        r++;
        wsCover.Cell(r, 2).Value = "TOTAL GROSS GHG EMISSIONS";
        wsCover.Cell(r, 3).Value = report.TotalGrossEmissions_tCO2e;
        wsCover.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        wsCover.Cell(r, 4).Value = 1.0m;
        wsCover.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
        wsCover.Range(r, 2, r, 4).Style.Font.Bold = true;
        wsCover.Range(r, 2, r, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");

        r++;
        wsCover.Cell(r, 2).Value = "Separate Report (Biogenic / Offsets)";
        wsCover.Cell(r, 3).Value = report.TotalSeparate_tCO2e;
        wsCover.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
        wsCover.Cell(r, 4).Value = "-";
        wsCover.Range(r, 2, r, 4).Style.Font.Italic = true;

        wsCover.Columns(2, 5).AdjustToContents();

        // 2. Greenhouse Gas Inventory Matrix Sheet
        var wsGas = workbook.Worksheets.Add("7 Gases Inventory Matrix");
        int gr = 2;
        wsGas.Cell(gr, 1).Value = "Greenhouse Gas";
        wsGas.Cell(gr, 2).Value = "Formula";
        wsGas.Cell(gr, 3).Value = "GWP (IPCC)";
        wsGas.Cell(gr, 4).Value = "Scope 1 (tCO2e)";
        wsGas.Cell(gr, 5).Value = "Scope 2 (tCO2e)";
        wsGas.Cell(gr, 6).Value = "Scope 3 (tCO2e)";
        wsGas.Cell(gr, 7).Value = "Total (tCO2e)";
        wsGas.Cell(gr, 8).Value = "Share (%)";
        wsGas.Range(gr, 1, gr, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#1b4d3e");
        wsGas.Range(gr, 1, gr, 8).Style.Font.FontColor = XLColor.White;
        wsGas.Range(gr, 1, gr, 8).Style.Font.Bold = true;

        foreach (var gas in report.GasInventory)
        {
            gr++;
            wsGas.Cell(gr, 1).Value = gas.GasName;
            wsGas.Cell(gr, 2).Value = gas.ChemicalFormula;
            wsGas.Cell(gr, 3).Value = gas.GwpFactor;
            wsGas.Cell(gr, 4).Value = gas.Scope1_tCO2e;
            wsGas.Cell(gr, 4).Style.NumberFormat.Format = "#,##0.000";
            wsGas.Cell(gr, 5).Value = gas.Scope2_tCO2e;
            wsGas.Cell(gr, 5).Style.NumberFormat.Format = "#,##0.000";
            wsGas.Cell(gr, 6).Value = gas.Scope3_tCO2e;
            wsGas.Cell(gr, 6).Style.NumberFormat.Format = "#,##0.000";
            wsGas.Cell(gr, 7).Value = gas.Total_tCO2e;
            wsGas.Cell(gr, 7).Style.NumberFormat.Format = "#,##0.000";
            wsGas.Cell(gr, 8).Value = gas.SharePercent / 100m;
            wsGas.Cell(gr, 8).Style.NumberFormat.Format = "0.0%";
        }

        wsGas.Columns(1, 8).AdjustToContents();

        // 3. Activity Audit Trail Sheet
        PopulateAuditRegistrySheet(workbook, report);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
    #endregion
}
