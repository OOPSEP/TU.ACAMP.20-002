using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;

namespace TU.ACAMP.Domain.Models.Library;

[Table("tb_emission_group", Schema = "emissions")]
public class TbEmissionGroup : AuditableEntity<int>
{
    #region class field  
    private static IReadOnlyCollection<TbEmissionGroup>? _masterListCache;
    #endregion
    #region class properties 
    public eTgoScopeNo TgoScopeNo { get; set; }
    public eIsoCategoryNo IsoCategoryNo { get; set; }
    [Required]
    public string Title { get; set; } = string.Empty;
    [StringLength(250)]
    public string TitleTh { get; set; } = string.Empty;
    [StringLength(250)]
    public string Remark { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public eSourceType SourceType { get; set; } = eSourceType.Custom;
    public int? SubscriptionId { get; set; }
    [ForeignKey(nameof(SubscriptionId))]
    public virtual TbSubscription? Subscription { get; set; }
    #endregion
    #region class method 
    public override string ToString()
    {
        return $"Emission Group : {Title} ";
    }

    public static TbEmissionGroup Duplicate(TbEmissionGroup source, int targetSubscriptionId)
    {
        #region hide [2026-08-17]
        if (source is null) throw new ArgumentNullException(nameof(source));
        return new TbEmissionGroup
        {
            Code            = source.Code,
            Title           = source.Title,
            TitleTh         = source.TitleTh,
            TgoScopeNo      = source.TgoScopeNo,
            IsoCategoryNo   = source.IsoCategoryNo,
            Remark          = source.Remark,
            SubscriptionId  = targetSubscriptionId,
            IsReadOnly      = false,
            SourceType      = eSourceType.Custom
        };
        #endregion
    }

    public static IReadOnlyCollection<TbEmissionGroup> GetMasterList()
    {
        #region hide [2026-07-05]
        if (_masterListCache == null)
        {
            var list = new List<TbEmissionGroup>
            {
                // ==================== SCOPE 1 (CFO) ====================
                new() { Id = 1, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope1, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0101", Title = "Stationary Combustion", TitleTh = "การเผาไหม้กับที่", Remark = "การเผาไหม้ของเชื้อเพลิงในเครื่องจักรหรืออุปกรณ์ที่อยู่กับที่ เช่น Boiler, Generator, และ Furnace" },
                new() { Id = 2, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope1, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0102", Title = "Mobile Combustion (On road)", TitleTh = "การเผาไหม้ในยานพาหนะทางบก", Remark = "การเผาไหม้ของเชื้อเพลิงในยานพาหนะขององค์กรที่ใช้วิ่งบนถนนสาธารณะ เช่น รถยนต์บริษัท, รถขนส่ง" },
                new() { Id = 3, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope1, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0103", Title = "Mobile Combustion (Off road)", TitleTh = "การเผาไหม้ในเครื่องจักรกลนอกถนน", Remark = "การเผาไหม้ในยานพาหนะหรือเครื่องจักรกลหนักที่ใช้งานภายในพื้นที่ปิด เช่น Forklift, Backhoe, และเครื่องจักรเกษตร" },
                new() { Id = 4, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope1, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0104", Title = "Refrigerants (สารทำความเย็น)", TitleTh = "สารทำความเย็น", Remark = "ปริมาณการเติมทดแทนสารทำความเย็นที่รั่วไหลจากระบบปรับอากาศและเครื่องทำความเย็นขององค์กร" },
                new() { Id = 5, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope1, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0105", Title = "Fugitive (การรั่วไหล และอื่นๆ)", TitleTh = "การรั่วไหลและอื่นๆ", Remark = "การรั่วไหลของก๊าซเรือนกระจกจากกระบวนการอื่น ๆ เช่น ระบบบำบัดน้ำเสีย, ถังดับเพลิง CO2, หรือกิจกรรมเชื่อมโลหะ" },

                // ==================== SCOPE 2 (CFO) - ADDITIONAL IMPORTED ENERGY ====================
                new() { Id = 6, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope2, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0201", Title = "Electricity, grid mix", TitleTh = "ไฟฟ้าจากสายส่งหลัก", Remark = "การซื้อไฟฟ้าจากการไฟฟ้านครหลวง (MEA) หรือการไฟฟ้าส่วนภูมิภาค (PEA) คำนวณตามค่า Grid Emission Factor ของประเทศ" },
                new() { Id = 34, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope2, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0202", Title = "Purchased Electricity (Private Power Plant)", TitleTh = "ไฟฟ้าซื้อจากโรงไฟฟ้าเอกชน", Remark = "การซื้อไฟฟ้าจากโรงไฟฟ้าเอกชนภายนอกโดยตรง (Direct PPA) ที่ไม่ใช่ไฟฟ้าสายส่งหลักของประเทศ" },
                new() { Id = 35, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope2, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0203", Title = "Purchased Thermal Energy, Steam, and Utilities", TitleTh = "พลังงานความร้อน ไอน้ำ และสาธารณูปโภคซื้อเข้า", Remark = "การนำเข้าพลังงานทางอ้อมในรูปแบบอื่น ๆ จากผู้ให้บริการรายอื่นในนิคมอุตสาหกรรม" },
                
                // ==================== SCOPE 3 (CFP / VALUE CHAIN) ====================
                new() { Id = 7, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0301", Title = "Petrochemicals", TitleTh = "ปิโตรเคมี", Remark = "การจัดซื้อวัตถุดิบต้นน้ำจากอุตสาหกรรมปิโตรเคมี เช่น เม็ดพลาสติก (PE, PP, PET), สารเคมีพื้นฐาน" },
                new() { Id = 8, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0302", Title = "Natural Gas Products", TitleTh = "ผลิตภัณฑ์ก๊าซธรรมชาติ", Remark = "การจัดซื้อวัตถุดิบหรือเชื้อเพลิงต้นน้ำที่แปรรูปมาจากก๊าซธรรมชาติในห่วงโซ่อุปทาน" },
                new() { Id = 9, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0303", Title = "Energy: Liquid and Solid Fuels", TitleTh = "พลังงาน: เชื้อเพลิงเหลวและของแข็ง", Remark = "กิจกรรมการผลิตและการขนส่ง (Well-to-Tank) ของเชื้อเพลิงที่องค์กรซื้อเข้ามาใช้งาน" },
                new() { Id = 10, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0304", Title = "Electricity Group", TitleTh = "การสูญเสียในระบบส่งจ่ายไฟฟ้า", Remark = "การสูญเสียในระบบส่งและจ่ายกระแสไฟฟ้า (T&D Losses) จากพลังงานไฟฟ้าที่ซื้อมาใช้งานใน Scope 2" },
                new() { Id = 11, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0305", Title = "Tap Water and Industrial Water", TitleTh = "น้ำประปาและน้ำอุตสาหกรรม", Remark = "การใช้น้ำประปาและน้ำดิบเพื่อการอุตสาหกรรมภายนอกกระบวนการผลิตหลัก (คำนวณคาร์บอนจากขั้นตอนผลิตน้ำประปา)" },
                new() { Id = 12, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category3, Code = "0306", Title = "Truck and Other Transportations", TitleTh = "การขนส่งทางรถและอื่นๆ", Remark = "การจ้างเหมาขนส่งภายนอก (Outsourced Logistics) ทั้งวัตถุดิบขาเข้า (Inbound) และสินค้าขาออก (Outbound)" },
                new() { Id = 13, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0307", Title = "Textile", TitleTh = "สิ่งทอและเครื่องนุ่งห่ม", Remark = "การจัดซื้อวัตถุดิบกลุ่มเส้นใย, ผ้าผืน, เสื้อผ้าสำเร็จรูป หรือวัสดุเหลือทิ้งจากอุตสาหกรรมสิ่งทอ" },
                new() { Id = 14, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0308", Title = "Natural Rubber Industry", TitleTh = "อุตสาหกรรมยางพาราธรรมชาติ", Remark = "การจัดซื้อยางพาราแปรรูปขั้นต้น เช่น ยางแผ่นรมควัน, ยางแท่ง STR, น้ำยางข้น" },
                new() { Id = 15, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0309", Title = "Wood Processing: Para-wood", TitleTh = "การแปรรูปไม้: ไม้ยางพารา", Remark = "แปรรูปและอบไม้ยางพาราที่เป็นวัตถุดิบในห่วงโซ่อุปทานการผลิตเฟอร์นิเจอร์หรือพาเลทไม้" },
                new() { Id = 16, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0310", Title = "Oil Palm", TitleTh = "ปาล์มน้ำมัน", Remark = "การซื้อน้ำมันปาล์มดิบหรืออนุพันธ์ปาล์มเพื่ออุตสาหกรรมอาหาร ไบโอดีเซล หรือเคมีภัณฑ์" },
                new() { Id = 17, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0311", Title = "Animal Feed", TitleTh = "อาหารสัตว์", Remark = "การจัดหาและใช้วัตถุดิบอาหารสัตว์สำเร็จรูปหรือหัวอาหารสัตว์ในธุรกิจปศุสัตว์" },
                new() { Id = 18, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0312", Title = "Food and Agricultural Products", TitleTh = "อาหารและผลิตภัณฑ์ทางการเกษตร", Remark = "วัตถุดิบพืชผลทางการเกษตร, บรรจุภัณฑ์อาหาร หรือผลิตภัณฑ์อาหารแปรรูปในกระบวนการซัพพลายเชน" },
                new() { Id = 19, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0313", Title = "Livestock", TitleTh = "ปศุสัตว์", Remark = "สิ่งปฏิกูลและการย่อยอาหารของสัตว์เคี้ยวเอื้อง (Methane) ภายใต้ขอบเขตการควบคุมอ้อมในฟาร์มคู่ค้า" },
                new() { Id = 20, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0314", Title = "Animal and Agricultural Derived Products", TitleTh = "ผลิตภัณฑ์จากสัตว์และเกษตรกรรม", Remark = "การจัดซื้อเนื้อสัตว์, นม, ไข่ หรือวัสดุพลอยได้ทางการเกษตรมาแปรรูปต่อ" },
                new() { Id = 21, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0315", Title = "Agricultural Machinery", TitleTh = "เครื่องจักรการเกษตร", Remark = "สินค้าทุน (Capital Goods) กลุ่มรถแทรกเตอร์หรือเครื่องจักรการเกษตรที่องค์กรจัดซื้อเข้ามาใช้งาน" },
                new() { Id = 22, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0316", Title = "Municipal Waste and Wastewater Management", TitleTh = "ขยะเทศบาลและการจัดการน้ำเสีย", Remark = "การส่งขยะไปฝังกลบหรือส่งน้ำเสียไปบำบัดยังหน่วยงานภายนอกของเทศบาล (ขยะและน้ำเสียจากการดำเนินงาน)" },
                new() { Id = 23, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0317", Title = "Pulp and Paper", TitleTh = "เยื่อและกระดาษ", Remark = "การจัดซื้อกระดาษสำนักงาน, บรรจุภัณฑ์กล่องกระดาษลูกฟูก หรือม้วนกระดาษวัตถุดิบ" },
                new() { Id = 24, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0318", Title = "Chemicals", TitleTh = "สารเคมี", Remark = "การจัดซื้อสารเคมีอุตสาหกรรม, สารเติมแต่ง (Additives), ตัวทำละลาย (Solvent) ที่ใช้ในกระบวนการผลิต" },
                new() { Id = 25, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0319", Title = "Ferrous Metals", TitleTh = "โลหะกลุ่มเหล็ก", Remark = "การจัดซื้อเหล็กโครงสร้าง, เหล็กแผ่น, ลวดเหล็ก หรือชิ้นส่วนโลหะที่มีเหล็กเป็นส่วนประกอบหลัก" },
                new() { Id = 26, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0320", Title = "Non-Ferrous Metals", TitleTh = "โลหะนอกกลุ่มเหล็ก", Remark = "การจัดซื้อชิ้นส่วนอะลูมิเนียม, ทองแดง, สแตนเลส หรือโลหะผสมอื่นๆ ที่ไม่ใช่เหล็ก" },
                new() { Id = 27, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0321", Title = "Process Emissions", TitleTh = "การปล่อยจากกระบวนการผลิต", Remark = "การปล่อยก๊าซจากกิจกรรมอ้อมในซัพพลายเชนที่เกิดปฏิกิริยาเคมีโดยตรงของคู่ค้าต้นน้ำ" },
                new() { Id = 28, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0322", Title = "Construction Materials", TitleTh = "วัสดุก่อสร้าง", Remark = "การจัดซื้อปูนซีเมนต์, คอนกรีตผสมเสร็จ, อิฐ, กระเบื้อง เพื่อใช้ในการก่อสร้างอาคารหรือปรับปรุงสินทรัพย์" },
                new() { Id = 29, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0323", Title = "Fertilizers", TitleTh = "ปุ๋ย", Remark = "การปล่อยคาร์บอนจากปุ๋ยเคมีและปุ๋ยอินทรีย์ที่คู่ค้าต้นน้ำใช้ในขั้นตอนการเพาะปลูกพืชวัตถุดิบ" },
                new() { Id = 30, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0324", Title = "Waste Landfills", TitleTh = "การฝังกลบขยะ", Remark = "การคำนวณวงจรชีวิตคาร์บอนฟุตพริ้นท์ของสินค้า (CFP) ในขั้นตอนการกำจัดซากผลิตภัณฑ์โดยการฝังกลบ" },
                new() { Id = 31, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0325", Title = "Glass and Mirror", TitleTh = "แก้วและกระจก", Remark = "การจัดซื้อบรรจุภัณฑ์ขวดแก้ว, กระจกแผ่นอุตสาหกรรม หรือวัสดุใยแก้วในผลิตภัณฑ์" },
                new() { Id = 32, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0326", Title = "Sericulture", TitleTh = "การปลูกหม่อนเลี้ยงไหม", Remark = "กิจกรรมและผลิตภัณฑ์ในกระบวนการปลูกหม่อนเลี้ยงไหม สาวไหม จนถึงรังไหมวัตถุดิบ" },
                new() { Id = 33, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0327", Title = "Others Additional", TitleTh = "กลุ่มอื่นๆ เพิ่มเติม", Remark = "กลุ่มกิจกรรมอ้อมอื่น ๆ นอกเหนือจากกลุ่มอ้างอิงของ อบก. สำหรับใช้เก็บตารางค่า Emission Factor เพิ่มเติม" },
                
                // ==================== SEPARATE REPORT (Biogenic / IPCC) ====================
                new() { Id = 36, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.SeparateReport, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0402-BIO", Title = "Biogenic CO2 (Stationary)", TitleTh = "ก๊าซคาร์บอนไดออกไซด์ชีวภาพ (อุปกรณ์อยู่กับที่)", Remark = "การปล่อยก๊าซ CO2 จากเชื้อเพลิงชีวมวล (Biomass) อยู่กับที่ เช่น ฟืน, แกลบ, กะลาปาล์ม (ต้องรายงานแยกต่างหาก)" },
                new() { Id = 37, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.SeparateReport, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0404-BIO", Title = "Biogenic CO2 (Mobile On-road)", TitleTh = "ก๊าซคาร์บอนไดออกไซด์ชีวภาพ (ยานพาหนะทางบก)", Remark = "การปล่อยก๊าซ CO2 ส่วนที่เป็นชีวภาพในยานพาหนะทางบก เช่น ส่วนผสมของ Ethanol ในแก๊สโซฮอล์ หรือ Biodiesel" },
                new() { Id = 38, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.SeparateReport, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0405-BIO", Title = "Biogenic CO2 (Mobile Off-road)", TitleTh = "ก๊าซคาร์บอนไดออกไซด์ชีวภาพ (เครื่องจักรกลนอกถนน)", Remark = "การปล่อยก๊าซ CO2 ส่วนที่เป็นชีวภาพจากเครื่องจักรกลหนักนอกถนนสาธารณะที่ใช้เชื้อเพลิงชีวภาพ" },
                new() { Id = 39, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.SeparateReport, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0401-IPCC", Title = "Refrigerants (IPCC)", TitleTh = "สารทำความเย็น (IPCC)", Remark = "การปล่อยก๊าซเรือนกระจกกลุ่มสารเคมีทำความเย็นตามบัญชีมาตรฐานเทคโนโลยี IPCC (รายงานในส่วนรายงานแยก)" },
                new() { Id = 40, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.SeparateReport, IsoCategoryNo = eIsoCategoryNo.Category1, Code = "0403-IPCC", Title = "Fugitive and Others (IPCC)", TitleTh = "การรั่วไหลและอื่นๆ (IPCC)", Remark = "สถิติก๊าซเรือนกระจกกลุ่มกระบวนการรั่วไหลที่อ้างอิงตามเกณฑ์ IPCC สำหรับจำแนกประเภทการตรวจสอบสิทธิ์ระดับระบบ" },

                // ==================== ADDITIONAL SCOPE 3 GROUPS ====================
                new() { Id = 41, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category2, Code = "0328", Title = "Energy and Fuels", TitleTh = "พลังงานและเชื้อเพลิง", Remark = "เพิ่มจาก gemini-code-cfp.json" },
                new() { Id = 42, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0329", Title = "Waste Management (Landfills)", TitleTh = "การจัดการขยะ (ฝังกลบ)", Remark = "เพิ่มจาก gemini-code-cfp.json" },
                new() { Id = 43, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0330", Title = "Plantation of Plants", TitleTh = "การเพาะปลูกพืช", Remark = "เพิ่มจาก gemini-code-cfp.json" },
                new() { Id = 44, IsReadOnly = true, SourceType = eSourceType.Default, TgoScopeNo = eTgoScopeNo.Scope3, IsoCategoryNo = eIsoCategoryNo.Category4, Code = "0331", Title = "Feed for Organic Dairy Cattle", TitleTh = "อาหารโคนมอินทรีย์", Remark = "เพิ่มจาก gemini-code-cfp.json" },
            };
            _masterListCache = new ReadOnlyCollection<TbEmissionGroup>(list);
        }
        return _masterListCache;
        #endregion
    }
    #endregion
}
