# SPEC: ISSB (IFRS S2) Climate-related Disclosure — เฟส 1

วันที่: 2026-09-24 | ผู้สั่ง: BOSS | ผู้ทำ: Claude

## เป้าหมาย
ออกเอกสาร Word "การเปิดเผยข้อมูลที่เกี่ยวข้องกับสภาพภูมิอากาศ ตาม IFRS S2" สำหรับใส่ในแบบ 56-1 One Report ของบริษัทจดทะเบียน
ตามแนวทาง ก.ล.ต. (Climate-first, ระยะผ่อนปรน 5 ปี: วัด GHG ด้วย GHG Protocol หรือมาตรฐานเทียบเท่า เช่น CFO ของ อบก.)

## ข้อตัดสินใจ
1. ตัวเลขทั้งหมดดึงจาก **CFO Report snapshot ที่ Verified / Locked** ในทะเบียนรายงาน (ไม่คำนวณใหม่) → ตัวเลขใน 56-1 ตรงกับที่ผ่านการทวนสอบ
2. เอกสาร ISSB เก็บเป็นไฟล์แนบของ snapshot นั้น (โฟลเดอร์เดียวกัน): `issb_climate_disclosure.json` + `issb_climate_disclosure.docx`
   ลบ snapshot = ลบเอกสาร ISSB ด้วย / ไม่แก้ schema ฐานข้อมูล
3. โครง 4 หัวข้อ IFRS S2: Governance / Strategy / Risk Management / Metrics & Targets
   - 3 หัวข้อแรกเป็นข้อความที่ผู้ใช้กรอก (ถ้าว่าง ใส่คำแนะนำสิ่งที่ต้องเปิดเผยเป็นตัวเอียงในวงเล็บ)
   - Metrics & Targets ระบบสร้างเอง: Scope 1/2/3 ปีรายงานเทียบปีฐาน, Scope 3 รายหมวด, รายงานแยก (Biogenic / Non-Kyoto) ไม่รวมในยอดรวม,
     Intensity ต่อรายได้ (ผู้ใช้กรอกรายได้), วิธีวัด (Consolidation approach, GWP, Scope 2 location-based), เป้าหมาย Net Zero (TbNetZeroTarget), ผู้ทวนสอบ
4. สิทธิ์: ดูได้เมื่อเป้าหมายอยู่ใน Subscription ของผู้ใช้ / บันทึกและดาวน์โหลดได้เฉพาะผู้มีสิทธิ์แก้ไข (เหมือนกฎ Export เดิม)

## ไฟล์
- `Services/Reports/IssbClimateDisclosureDocument.cs` (model)
- `Services/Reports/IssbClimateDisclosureService.cs` (build / save / load / Word)
- `Components/Pages/Reports/PageIssbClimateDisclosure.razor` (route `/reports/issb-climate`)
- `PageCfoAuditReport.razor` ปุ่ม ISSB ในแถวทะเบียนรายงาน
- `CfoReportStorageService.GetReportDirectory()`

## นอกขอบเขตเฟส 1
- IFRS S1 ส่วนที่ไม่ใช่สภาพภูมิอากาศ, Inline XBRL, Scenario analysis เชิงตัวเลข, Scope 2 market-based, ผลกระทบทางการเงินเชิงตัวเลข
- ต้องยืนยันรูปแบบกับประกาศ ก.ล.ต. ฉบับสุดท้าย (ข้อมูลอ้างอิงเฟสนี้มาจากเอกสารรับฟังความคิดเห็น อสย. 50/2567 และข่าวปรับหลักการปี 2568)
