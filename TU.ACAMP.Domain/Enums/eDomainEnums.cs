namespace TU.ACAMP.Domain.Enums;

public enum eStorageProvider
{
    Local = 0,
    URL = 1,
    GoogleDrive = 2
}

public enum eSubscriptionPlan
{
    Basic = 0,//2,900 ฿
    Pro = 1,//9,000 
    Premium = 2,//19,900 
    Enterprise = 3//49,000
}; public enum eSubscriptionOwnerType
{
    //บุคคลธรรมดา:
    Individual = 0,
    //นิติบุคคล:
    Juristic = 1
}/// <summary>
/// กำหนดบทบาทและขอบเขตสิทธิ์การเข้าถึงข้อมูลระบบบริหารจัดการคาร์บอนฟุตพริ้นท์ (PRISM-X)
/// </summary>
public enum eSubscriptionRole
{
    /// <summary>
    /// เจ้าของบัญชีสิทธิ์ (Tenant Owner):
    /// มีอำนาจสูงสุดในการบริหารจัดการกลุ่มทุน (Subscription) สามารถลบบัญชี,
    /// จัดการข้อมูลการชำระเงิน, และมีสิทธิ์ขาดเหนือข้อมูลองค์กรทั้งหมด
    /// </summary>
    Owner = 1,

    /// <summary>
    /// ผู้ดูแลระบบประจำองค์กร (Subscription Admin):
    /// มีสิทธิ์จัดการโครงสร้างพื้นฐานภายใน Tenant เช่น เพิ่ม/ลบ สมาชิกผู้ใช้,
    /// จัดตั้งโรงงาน/ไซต์งาน (TbFacility), และควบคุมการตั้งค่าระบบทั่วไป
    /// </summary>
    Admin = 2,

    /// <summary>
    /// ผู้บันทึกและจัดการข้อมูล (Data Editor):
    /// มีสิทธิ์ในการเพิ่ม แก้ไข และลบข้อมูลกิจกรรม (Activity Data) ในระดับไซต์งาน 
    /// เช่น ปริมาณการใช้ไฟฟ้า เชื้อเพลิง (Scope 1, 2) แต่ไม่มีสิทธิ์จัดการสมาชิกผู้ใช้
    /// </summary>
    Editor = 3,

    /// <summary>
    /// ที่ปรึกษาด้านความยั่งยืน (Sustainability Consultant):
    /// มีสิทธิ์เข้าถึงข้อมูลเชิงลึกเพื่อตรวจสอบ (Audit) ตรวจสอบความถูกต้องของสูตรคำนวณ 
    /// และเรียกดูรายงาน Carbon Footprint เพื่อประเมิน Net Zero Pathway (ไม่มีสิทธิ์แก้ไขข้อมูลกิจกรรม)
    /// </summary>
    Consultant = 4,

    /// <summary>
    /// ผู้เข้าชมข้อมูล (Data Viewer):
    /// สิทธิ์ระดับอ่านอย่างเดียว (Read-Only) สำหรับผู้บริหารหรือผู้ประเมินภายนอก 
    /// สามารถดูแดชบอร์ดสรุปผลและส่งออกรายงานได้เท่านั้น ไม่สามารถแก้ไขใด ๆ ในระบบได้
    /// </summary>
    Viewer = 5
};
/// <summary>
/// เมธอดช่วยตรวจสอบขอบเขตสิทธิ์ของ eSubscriptionRole
/// </summary>
public static class eSubscriptionRoleExtensions
{
    /// <summary>Editor หรือสูงกว่า (Owner, Admin, Editor) เท่านั้นที่แก้ไขข้อมูลได้</summary>
    public static bool CanEdit(this eSubscriptionRole role) => role <= eSubscriptionRole.Editor;

    /// <summary>ทุกบทบาทที่ถูกกำหนดไว้ (รวมถึง Viewer) มีสิทธิ์เข้าดูสัญญานี้ได้</summary>
    public static bool CanView(this eSubscriptionRole role) => true;

    /// <summary>Owner และ Admin เท่านั้นที่จัดการสมาชิกผู้ใช้ได้ (Editor แก้ข้อมูลกิจกรรมได้ แต่จัดการสมาชิกไม่ได้)</summary>
    public static bool CanManageUsers(this eSubscriptionRole role) => role <= eSubscriptionRole.Admin;
}
/// <summary>
/// กำหนดบทบาทและขอบเขตสิทธิ์การเข้าถึงข้อมูลในระดับกลุ่มบริษัท/เครือข่ายธุรกิจ (Corporate Level)
/// </summary>
public enum eCorporateRole
{
    /// <summary>
    /// ผู้จัดการและควบคุมกลุ่มบริษัท (Corporate Manager):
    /// มีอำนาจสูงสุดในเลเยอร์นี้ สามารถเพิ่ม/ลด บริษัทในเครือ (TbCompany), 
    /// โยกย้ายไซต์งาน/โรงงาน (TbFacility), จัดการสิทธิ์ของ Editor/Consultant ภายในกลุ่ม,
    /// และปรับแต่งโครงสร้างการถือหุ้นสัดส่วนควบรวม (Equity Share) ขององค์กร
    /// </summary>
    Manager = 1,

    /// <summary>
    /// ผู้บันทึกและจัดการข้อมูลกลุ่มทุน (Corporate Editor):
    /// มีสิทธิ์เพิ่ม แก้ไข และลบข้อมูลกิจกรรมการปล่อยก๊าซเรือนกระจก (Activity Data) 
    /// ของทุกบริษัทและทุกโรงงานที่อยู่ภายใต้โครงสร้างเครือข่ายธุรกิจนี้ได้ทั้งหมด
    /// </summary>
    Editor = 2,

    /// <summary>
    /// ที่ปรึกษาตรวจสอบข้อมูลเครือข่าย (Corporate Consultant):
    /// มีสิทธิ์เข้าถึงข้อมูลเชิงลึกและรายงาน Carbon Footprint ของทุกบริษัทในเครือ 
    /// เพื่อทำหน้าที่ตรวจสอบความถูกต้อง (Verification) ของค่า Emission Factors 
    /// และประเมินเป้าหมาย Net Zero Pathway ในภาพรวม (ไม่มีสิทธิ์แก้ไขข้อมูล)
    /// </summary>
    Consultant = 3,

    /// <summary>
    /// ผู้เข้าชมข้อมูลกลุ่มธุรกิจ (Corporate Viewer):
    /// สิทธิ์อ่านอย่างเดียว (Read-Only) ในระดับกลุ่มทุน สามารถเปิดดูแดชบอร์ดสรุปผล 
    /// ดูภาพรวมการลดคาร์บอนแยกตาม Scope และส่งออกรายงานในระดับ Corporate ได้เท่านั้น
    /// </summary>
    Viewer = 4
};
/// <summary>
/// เมธอดช่วยตรวจสอบขอบเขตสิทธิ์ของ eCorporateRole
/// </summary>
public static class eCorporateRoleExtensions
{
    // [2026-09-19][Antigravity] Manager and Editor have edit permissions on corporate level
    public static bool CanEdit(this eCorporateRole role) => role <= eCorporateRole.Editor;
}
public enum eSourceType
{
    Default = 0, // System-wide read-only master data (TGO, IPCC, Thai DB, etc.)
    Custom = 1   // Subscription-specific custom data created/duplicated by users
}
public enum eTgoScopeNo
{
    Scope1 = 1,
    Scope2 = 2,
    Scope3 = 3,
    SeparateReport = 4
}
public enum eIsoCategoryNo
{
    //Direct GHG emissions and removals Scope 1 (การปล่อยตรง)
    Category1 = 1, //การเผาไหม้กับที่ (Stationary), การเผาไหม้เคลื่อนที่ (Mobile), สารทำความเย็นรั่วไหล (Fugitive) รวมถึงการดูดกลับก๊าซฯ จากพื้นที่ป่าขององค์กร
    //Indirect GHG emissions from imported energy Scope 2 (การปล่อยทางอ้อมจากพลังงานที่นำเข้า)
    Category2 = 2,        //พลังงานไฟฟ้าที่ซื้อมาใช้งาน, พลังงานความร้อน, ไอน้ำ หรือระบบทำความเย็นที่นำเข้ามาจากภายนอก
                          //Indirect GHG emissions from transportation Scope 3 (Upstream / Downstream)
    Category3 = 3,  //การขนส่งสินค้าดิบเข้าบริษัท, การกระจายสินค้าออก, การเดินทางของพนักงาน (Commuting), และการเดินทางไปติดต่อธุรกิจ (Business Travel)
                    //Indirect GHG emissions from products used by organization Scope 3 (Upstream)
    Category4 = 4,      //สินค้าและบริการที่องค์กรจัดซื้อเข้ามา (Purchased Goods & Services), การจัดการของเสียขององค์กร (Waste), และการเช่าสินทรัพย์ฝั่งต้นน้ำ
    //Indirect GHG emissions associated with the use of products from the organization Scope 3 (Downstream)
    Category5 = 5,//การปล่อยก๊าซฯ จากตัวผลิตภัณฑ์ระหว่างที่ลูกค้าใช้งาน (Use of Sold Products) และการจัดการซากผลิตภัณฑ์หลังสิ้นอายุไข (End-of-life)
                  //Indirect GHG emissions from other sources Scope 3 (อื่นๆ)
    Category6 = 6,//หมวดหมู่เปิดสำหรับกิจกรรมเฉพาะด้านที่สร้างผลกระทบอย่างมีนัยสำคัญ แต่ไม่เข้าพวกกับ 5 หมวดหมู่แรก เช่น การลงทุน (Investments)
}
public enum eTopicLevel
{
    Corporate = 1,
    Company = 2,
    Facility = 3
}

/// <summary>
/// [GHG-07] IPCC Assessment Report GWP Metric Version (ISO 14064-1:2018 §8.2)
/// </summary>
public enum eGwpVersion
{
    AR4 = 4,
    AR5 = 5,
    AR6 = 6
}
