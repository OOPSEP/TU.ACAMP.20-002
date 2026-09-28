# แนวทาง Net-Zero Pathway Dashboard (D2) — 2026-09-26

- **หน้า:** `/dashboard/net-zero` (`NetZeroPathwayDashboard.razor` — ตอนนี้เป็นหน้าว่าง)
- **เริ่มได้เมื่อ:** D1 Executive Dashboard เสร็จ (ตาม `PLAN_PROJECT_DIRECTION_2026-09-26.md` ข้อ 0)
- **หลักแบ่งหน้าที่:** Executive = ภาพรวม **ปีเดียว** / Net-Zero = ความคืบหน้า **หลายปี** จากปีฐาน → ปีเป้าหมาย และ "ทำไมถึงห่างเป้า"

---

## 1. ข้อมูลที่มีอยู่แล้ว (ตรวจจากโค้ด)

| แหล่ง | ใช้ทำอะไร |
|---|---|
| `TbNetZeroTarget` (BaseYear, TargetYear, TargetReductionPercent, Alert*) ผ่าน `NetZeroTargetService.GetAsync(subId, corpId)` | เส้นเป้าหมาย |
| Events + `EmissionCalculator.CalculateKgCO2e` (สูตรเดียวกับ Executive) | ยอดจริงรายปี / Scope / Activity Group / Facility |
| `ExecutiveDashboardService2.ToFiscalPeriod` | รองรับปีบัญชีไม่เริ่ม ม.ค. |
| `TbGhgTargetAlertLog` (ReportingYear, CurrentEmissionsTon, TargetCapTon, ConsumptionPercent, AlertTier, DeliveryStatus) | ประวัติการแจ้งเตือน |
| CFO Report snapshot (ผลผลิต Fr-01, ปีปฏิทิน) | Intensity รายปี |
| **ยังไม่มี:** Offsets ที่บันทึกถาวร, เป้ารายปีแบบกำหนดเอง | ไม่อยู่ในขอบเขตแรก |

## 2. Widget (เรียงตามลำดับทำ)

| # | Widget | เนื้อหา | Phase |
|---|---|---|---|
| W1 | `NzKpiWidget` | ยอดปีฐาน · ยอดล่าสุด · ลดได้แล้ว % เทียบปีฐาน · ต้องลดถึง % ในปีนี้ · ส่วนต่างถึงเป้าปีเป้าหมาย (tCO₂e) · ปีที่เหลือ · อัตราลดที่ต้องทำต่อปีจากนี้ | P1 |
| W2 | `NzPathwayChartWidget` | แท่งยอดจริงรายปี (ปีฐาน → ปีล่าสุด) + เส้นเป้าหมายเส้นตรง (ปีฐาน → ปีเป้าหมาย) + เส้นคาดการณ์ถึงปีเป้าหมาย (ระบุชัดว่าเป็นค่าประมาณ) | P1 |
| W3 | `NzScopeTrendWidget` | ยอดรายปีแยก Scope 1/2/3 (stacked) — ดูว่า Scope ไหนดึงไม่ให้ลด | P1 |
| W4 | `NzGapDriversWidget` | Waterfall ปีฐาน → ปีที่เลือก แยกตาม Activity Group: อะไรเพิ่ม/ลด | P2 |
| W5 | `NzOrgContributionWidget` | ตาราง Facility/Company: ยอดปีฐาน vs ปีที่เลือก, Δ, % ต่อการลดรวม | P2 |
| W6 | `NzAlertHistoryWidget` | ตารางจาก `TbGhgTargetAlertLog` | P3 |
| W7 | `NzIntensityTrendWidget` | tCO₂e/หน่วยผลผลิตรายปี เฉพาะปีที่มี CFO snapshot และเป็นปีปฏิทิน | P3 |

## 3. โครงสร้างโค้ด
- **Service ใหม่** `Services/Dashboard/NetZeroDashboardService.cs` + `NetZeroDashboardModels.cs` — **ไม่แก้** `ExecutiveDashboardService2`
  - คิวรีเดียว: GroupBy (TopicId, Year, Month) ช่วงปีฐาน → ปีล่าสุด แล้วคำนวณรายปีใน memory ด้วย `EmissionCalculator` (แบบเดียวกับ Executive)
  - ตรวจสิทธิ์ fail-closed ด้วย `allowedSubscriptionIds` + Level/TargetId เหมือน Executive; cache 3 นาที + Refresh bypass
- **Widget** ที่ `Components/Pages/Dashboard/NetZero/Widgets/` รับ DTO อย่างเดียว ไม่เรียก DB
- **หน้า** ตัวกรอง: Corporate/Company/Facility + เดือนเริ่มปีบัญชี, State Persistence ผ่าน query (`cid/cmp/fac/fym`), ปุ่ม Annual Target Settings ใช้ `NetZeroSettingDialog2` ตัวเดิม
- UI ภาษาอังกฤษ, ตัวเลข N2, ห้าม Raw SQL, ใช้ `AppTimeZone`

## 4. กฎความถูกต้อง (ใช้เป็นเกณฑ์ทดสอบ)
1. ยอดรายปีของหน้า Net-Zero = Gross (Scope 1+2+3) ของ Executive Dashboard ปีเดียวกัน ตัวกรองเดียวกัน (≤ 0.01 tCO₂e) — ไม่รวม Separate/Biogenic
2. เป้าหมายปี Y = ยอดปีฐาน × (1 − % ลด × min(1, (Y − ปีฐาน) ÷ (ปีเป้าหมาย − ปีฐาน))) — สูตรเดียวกับ Executive และ `NetZeroSettingDialog2`
3. ปีฐานไม่มีข้อมูล → ไม่แสดงเส้นเป้าหมาย/KPI ที่ต้องหารด้วยปีฐาน พร้อมข้อความแจ้ง
4. ปีที่ข้อมูลยังไม่ครบ 12 เดือน (ปีปัจจุบัน) → ติดป้าย "Year to date"
5. Unit test: สูตรเป้าหมาย, อัตราลดที่ต้องทำต่อปี, ยอดรายปีตรงกับ Executive

## 5. จุดตัดสินใจของ BOSS (ก่อนเริ่ม P1)
- วิธีคาดการณ์ใน W2: **(แนะนำ)** เส้นตรงจากแนวโน้มยอดจริงทุกปีที่มี / หรือไม่แสดงเส้นคาดการณ์
