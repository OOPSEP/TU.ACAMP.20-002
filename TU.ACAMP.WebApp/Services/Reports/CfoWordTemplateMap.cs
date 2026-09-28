namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-23][Claude] ตำแหน่งในเทมเพลต Word ของ อบก. "รายงานการปล่อยและดูดกลับก๊าซเรือนกระจกขององค์กร (AR5) Update.docx"
/// เทมเพลตไม่มี placeholder / content control จึงระบุตารางด้วย "ข้อความหัวข้อก่อนตาราง" หรือ "ข้อความแถวหัวตาราง"
/// แทนลำดับตาราง (index) เพื่อไม่ให้พังเมื่อ อบก. แทรกตาราง/ย่อหน้าใหม่
/// ค่าคงที่ทั้งหมดอยู่ไฟล์นี้ไฟล์เดียว ห้าม hardcode กระจายใน CfoWordReportService
/// </summary>
public static class CfoWordTemplateMap
{
    public const string OutputFileName = "report_cfo.docx";
    public const string TemplateConfigKey = "CfoReport:WordTemplatePath";
    public const string TemplateSearchPattern = "*.docx";
    public const string TemplateNameMarker = "AR5";

    // ── หน้าปก: ย่อหน้าที่ขึ้นต้นด้วยข้อความเหล่านี้ (ก่อนตารางแรก) จะถูกต่อท้ายด้วยค่า ──
    public const string CoverOrganizationLabel = "ชื่อองค์กร";
    public const string CoverAddressLabel = "ที่อยู่/สถานที่ตั้งองค์กร";
    public const string CoverReportDateLabel = "วันที่รายงานผล";
    public const string CoverPeriodLabel = "ระยะเวลาในการติดตามผล";

    // ── ข้อ 1 บทนำ: หัวข้อ "บทนำ" ตามด้วยย่อหน้าคำแนะนำ (สีน้ำเงิน) ที่จะถูกแทนด้วยบทนำ 3 ย่อหน้า ──
    public const string IntroductionHeading = "บทนำ";
    public const string IntroductionGuidancePrefix = "อธิบายความเป็นมา";
    public const string IntroductionFirstLineIndent = "720";   // 0.5 นิ้ว (twips)
    public const string IntroductionSpacingAfter = "120";      // 6 pt (twips)

    // ── รูปภาพ: กรอบสี่เหลี่ยมเปล่า (wps shape) ในเทมเพลต ถูกแทนด้วยรูปที่ผู้ใช้แนบหรือรูป default ──
    // กรอบที่อยู่ก่อนหัวข้อ "บทนำ" = หน้าปก, นอกนั้นระบุด้วยหัวข้อที่อยู่ก่อนกรอบ
    public const string WordprocessingShapeUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    public const string ImageStructureHeadingPrefix = "3.1.1";
    public const string ImagePlantLayoutHeadingPrefix = "3.1.2";
    public const string ImageProcessFlowHeadingPrefix = "3.1.3";
    public const string ImageDataQualityHeadingPrefix = "7.2";

    // ไฟล์ใน wwwroot/images และ Files/Images (ชุดเดียวกับ Excel + 3 รูปใหม่สำหรับ Word)
    public const string DefaultCoverImage = "default_org_facility.jpg";          // = Excel Fr-01
    public const string DefaultStructureImage = "default_org_structure.png";     // = Excel Fr-03.1
    public const string DefaultPlantLayoutImage = "default_plant_layout.png";    // 3.1.2 (Word เท่านั้น)
    public const string DefaultProcessFlowImage = "default_process_flow.png";    // 3.1.3 (Word เท่านั้น)
    public const string DefaultDataQualityImage = "default_data_quality.png";    // 7.2 (Word เท่านั้น)

    // ── ข้อ 6.1: ย่อหน้าเส้นประแรกหลังหัวข้อนี้ ──
    public const string BaseYearHeadingPrefix = "6.1";
    public const char DottedLineChar = '.';

    // ── ตาราง ──
    // HeadingPrefix: ย่อหน้าที่ไม่ว่าง 1 ใน 2 ย่อหน้าก่อนตาราง ต้องขึ้นต้นด้วยข้อความนี้ (normalize ช่องว่าง/แท็บแล้ว)
    // HeaderContains: ข้อความแถวแรกของตาราง (ต่อทุกเซลล์) ต้องมีข้อความนี้
    // DataStartRow: แถวแรกของข้อมูล (0-based) / FooterRows: จำนวนแถวท้ายที่ไม่ใช่ข้อมูล (เช่น "รวมทั้งหมด")
    public static readonly TableAnchor GeneralInfo = new("2. ข้อมูลทั่วไป", null, "2.1 ชื่อองค์กร", 0, 0);
    public static readonly TableAnchor OrganizationBoundary = new("3.1 ขอบเขตขององค์กร", null, "แนวทางที่ใช้กำหนดขอบเขตองค์กร", 0, 0);
    public static readonly TableAnchor ActivitiesByFacility = new("3.1.4 กิจกรรมแยกราย Facility", "3.1.4", null, 2, 0);
    public static readonly TableAnchor GreenhouseGases = new("3.2 ชนิดก๊าซ", null, "ก๊าซเรือนกระจกที่พิจารณา", 0, 0);
    public static readonly TableAnchor Scope1Sources = new("3.2.1 แหล่งปล่อย Scope 1", "3.2.1", null, 1, 0);
    public static readonly TableAnchor BiomassSources = new("3.2.2 แหล่งปล่อยชีวมวล/ก๊าซชีวภาพ", "3.2.2", null, 1, 0);
    public static readonly TableAnchor SeparateSources = new("3.2.3 แหล่งปล่อยรายงานแยก", "3.2.3", null, 1, 0);
    public static readonly TableAnchor Scope2Sources = new("3.2.4 แหล่งปล่อย Scope 2", "3.2.4", null, 1, 0);
    public static readonly TableAnchor Scope3Sources = new("3.2.6 แหล่งปล่อย Scope 3", "3.2.6", null, 1, 0);
    public static readonly TableAnchor Scope1Monitoring = new("4.1 การติดตามผล Scope 1", "4.1", null, 3, 0);
    public static readonly TableAnchor Scope2Monitoring = new("4.2 การติดตามผล Scope 2", "แหล่งปล่อยก๊าซเรือนกระจก จากขอบเขตการดำเนินงานประเภทที่ 2", null, 3, 0);
    public static readonly TableAnchor Scope3Monitoring = new("4.3 การติดตามผล Scope 3", "แหล่งปล่อยก๊าซเรือนกระจก จากขอบเขตการดำเนินงานประเภทที่ 3", null, 3, 0);
    public static readonly TableAnchor SeparateMonitoring = new("4.4 การติดตามผลรายงานแยก", "4.4", null, 3, 0);
    public static readonly TableAnchor Scope1Summary = new("5.1 สรุป Scope 1 แยกชนิดก๊าซ", "5.1", null, 2, 1);
    public static readonly TableAnchor Scope2Summary = new("5.2 สรุป Scope 2", "5.2", null, 3, 1);
    public static readonly TableAnchor Scope3Summary = new("5.3 สรุป Scope 3", "5.3", null, 3, 1);
    public static readonly TableAnchor SeparateSummary = new("5.4 สรุปรายงานแยก", "5.4", null, 3, 1);
    public static readonly TableAnchor CarbonIntensity = new("5.5 Carbon Intensity", "5.5", null, 1, 0);
    public static readonly TableAnchor BaseYearBoundary = new("6.2 ขอบเขตในปีฐาน", "6.2", null, 1, 0);
    public static readonly TableAnchor UncertaintyResult = new("8. ผลการประเมินความไม่แน่นอน", "ตารางแสดงผลการประเมินความไม่แน่นอน", null, 1, 0);

    public static IReadOnlyList<TableAnchor> All { get; } = new[]
    {
        GeneralInfo, OrganizationBoundary, ActivitiesByFacility, GreenhouseGases,
        Scope1Sources, BiomassSources, SeparateSources, Scope2Sources, Scope3Sources,
        Scope1Monitoring, Scope2Monitoring, Scope3Monitoring, SeparateMonitoring,
        Scope1Summary, Scope2Summary, Scope3Summary, SeparateSummary,
        CarbonIntensity, BaseYearBoundary, UncertaintyResult
    };

    // ── คอลัมน์ ──
    // ข้อ 2 (GeneralInfo) แถวตามลำดับเลขข้อ 2.1 .. 2.9
    public const int GeneralInfoOrganizationRow = 0;   // 2.1
    public const int GeneralInfoAddressRow = 1;        // 2.2
    public const int GeneralInfoPeriodRow = 5;         // 2.6
    public const int GeneralInfoMaterialityRow = 8;    // 2.9
    public const int GeneralInfoValueCol = 1;

    // ข้อ 3.1
    public const int BoundaryApproachRow = 0;
    public const int BoundaryFacilitiesRow = 1;
    public const int BoundaryValueCol = 1;

    // ข้อ 3.1.4: Facility | Scope 1 | Scope 2 | Scope 3
    public const int ActivitiesFacilityCol = 0;
    public const int ActivitiesScope1Col = 1;
    public const int ActivitiesScope2Col = 2;
    public const int ActivitiesScope3Col = 3;

    // ข้อ 3.2 ชนิดก๊าซ: แถว "ก๊าซเรือนกระจกที่พิจารณาอื่น ๆ เพิ่มเติม" (มีตัวอย่าง HCFC-22 ต้องล้าง)
    public const int GasesOtherRow = 1;
    public const int GasesValueCol = 1;

    // ข้อ 3.2.x: Facility | แหล่งปล่อย | ที่ตั้ง | Capacity | ใช้ภายใน | จำหน่ายภายนอก | ความสำคัญ
    public const int SourcesFacilityCol = 0;
    public const int SourcesNameCol = 1;
    public const int SourcesLocationCol = 2;   // [2026-09-23][Claude] ที่ตั้ง/ตำแหน่ง = หน่วยงานที่หัวข้อกิจกรรมผูกอยู่
    public const int SourcesSignificanceCol = 6; // [2026-09-23][Claude] ความสำคัญ (มีนัยสำคัญมาก หรือ น้อย)
    public const string SignificantHigh = "มีนัยสำคัญมาก";
    public const string SignificantLow = "มีนัยสำคัญน้อย";

    // [2026-09-23][Claude] Prefix มาตรฐานของ child topic Biogenic CO2 (GEMINI.md: "[Biogenic] {Topic Name}")
    // ใช้แยกแหล่งปล่อยชีวมวลไปข้อ 3.2.2 (ข้อ 4.4 / 5.4 ยังรวมอยู่ในรายงานแยกตามเดิม)
    public const string BiogenicTitlePrefix = "[Biogenic]";

    // ── หัว/ท้ายกระดาษ: เซลล์ถัดจากป้ายนี้ในตาราง header/footer ──
    public const string HeaderOrganizationLabel = "องค์กร";
    public const string FooterPreparedByLabel = "จัดทำโดย";

    // ── ข้อ 3.1.5 / 6.3: ย่อหน้าแรกหลังหัวข้อ (บรรทัดเส้นประ / คำแนะนำ) ถูกแทนด้วยข้อความจากข้อมูล ──
    public const string BoundaryChangeHeadingPrefix = "3.1.5";
    public const string BaseYearDifferenceHeadingPrefix = "6.3";

    // ── ข้อ 8 ตารางผลประเมินความไม่แน่นอน: ประเภท | รายการ | A | B | AxB | ระดับ ──
    public const int UncertaintyScopeCol = 0;
    public const int UncertaintyNameCol = 1;
    public const int UncertaintyEfScoreCol = 3;
    // คะแนนค่า EF (B) ตามตารางอ้างอิงในเทมเพลต: C=4 วัดเองที่มีคุณภาพ, D=3 ผู้ผลิต/ระดับประเทศ, E=2 ระดับภูมิภาค, F=1 ระดับสากล
    public const int EfScoreMeasured = 4;
    public const int EfScoreSupplierOrNational = 3;
    public const int EfScoreInternational = 1;

    // ข้อ 4.x: แหล่งปล่อย | ลักษณะข้อมูลกิจกรรม | จุดตรวจวัด | ตรวจวัด | ใบเสร็จ | ประมาณค่า | หลักฐาน | ที่มาของค่า EF
    public const int MonitoringSourceCol = 0;
    public const int MonitoringActivityCol = 1;
    public const int MonitoringPointCol = 2;   // [2026-09-23][Claude] จุดที่ตรวจวัด/จุดที่บันทึก = หน่วยงานที่บันทึกข้อมูลกิจกรรม
    public const int MonitoringEvidenceCol = 6;
    public const int MonitoringEfCol = 7;

    // ข้อ 5.1: ลำดับ | แหล่งปล่อย | CO2 | Fossil CH4 | CH4 | N2O | SF6 | NF3 | HFCs | PFCs | รวม
    // แถว "รวมทั้งหมด" เซลล์แรก span 2 คอลัมน์ ดัชนีคอลัมน์ก๊าซจึงเลื่อนไป 1
    public const int Scope1SummaryIndexCol = 0;
    public const int Scope1SummaryNameCol = 1;
    public const int Scope1SummaryFirstGasCol = 2;
    public const int Scope1SummaryTotalCol = 10;
    public const int Scope1SummaryFooterFirstGasCol = 1;
    public const int Scope1SummaryFooterTotalCol = 9;

    // ข้อ 5.2 - 5.4: แหล่งปล่อย | tCO2e
    public const int SummaryNameCol = 0;
    public const int SummaryValueCol = 1;

    // ข้อ 5.5: แถว 1..8 ตามลำดับในเทมเพลต
    public const int IntensityScope1Row = 1;
    public const int IntensityScope2Row = 2;
    public const int IntensityScope3Row = 3;
    public const int IntensityScope12Row = 4;
    public const int IntensityScope123Row = 5;
    public const int IntensityProductionRow = 6;
    public const int IntensityPerScope12Row = 7;
    public const int IntensityPerScope123Row = 8;
    public const int IntensityValueCol = 1;
    public const int IntensityUnitCol = 2;

    // ข้อ 6.2: ขอบเขต | แหล่งปล่อย | tCO2e ปีฐาน | หมายเหตุ
    public const int BaseYearScopeCol = 0;
    public const int BaseYearNameCol = 1;
    public const int BaseYearValueCol = 2;
    public const int BaseYearRemarkCol = 3;
    public const string BaseYearScope1Label = "ขอบเขตที่ 1";
    public const string BaseYearScope2Label = "ขอบเขตที่ 2";
    public const string BaseYearScope3Label = "ขอบเขตที่ 3";
    public const string BaseYearSeparateLabel = "รายงานแยกอื่น ๆ";
}

/// <summary>
/// [2026-09-23][Claude] ตำแหน่งรูปในเทมเพลต Word
/// </summary>
public enum CfoWordImageSlot
{
    Cover,                  // หน้าปก — รูปองค์กร (Fr-01)
    OrganizationStructure,  // 3.1.1 โครงสร้างขององค์กร (Fr-03.1)
    PlantLayout,            // 3.1.2 แผนผังของโรงงาน
    ProcessFlow,            // 3.1.3 แผนผังกระบวนการผลิต
    DataQuality             // 7.2 แผนผังการจัดการคุณภาพของข้อมูล
}

/// <summary>
/// [2026-09-23][Claude] ตัวระบุตารางในเทมเพลต Word
/// </summary>
public sealed record TableAnchor(
    string Name,
    string? HeadingPrefix,
    string? HeaderContains,
    int DataStartRow,
    int FooterRows);
