# แผนทดสอบระบบเต็มรูปแบบเพื่อขึ้น Production — CARBON ONE (TU.ACAMP.20-002)

- **วันที่:** 2026-09-26 · **ผู้สั่ง:** BOSS
- **ทิศทาง:** ยกเลิกการรื้อ Clean Architecture / Api แยก / CIP / CarbonX → ใช้ระบบปัจจุบัน (Classic) ทดสอบให้ครบทุกสถานการณ์แล้วขึ้น Production
- **หลัก:** Feature Freeze ตลอดแผน — แก้ได้เฉพาะ defect ที่พบจากการทดสอบ และงานพัฒนาที่ BOSS อนุมัติในข้อ 0

---

## 0. งานพัฒนาที่อนุมัติ (ทำตามลำดับ)

| # | งาน | สถานะ | เงื่อนไขเริ่ม |
|---|---|---|---|
| D1 | **Executive Dashboard** (`/dashboard`, `ExecutiveDashboard2.razor`) — ทำให้เสร็จสมบูรณ์ รวมตัวกรองเดือนเริ่มปีบัญชี (2026-09-26) | กำลังทำ | — |
| D2 | **Net-Zero Pathway Dashboard** (`/dashboard/net-zero`, `NetZeroPathwayDashboard.razor`) — page + widget ใหม่สำหรับข้อมูลเชิงลึกด้าน Net-Zero โดย Executive Dashboard คงเดิม — แนวทาง: `PLAN_NETZERO_DASHBOARD_2026-09-26.md` | Phase 1 (KPI + Pathway + Scope รายปี) ทำแล้ว 2026-09-26 ตามคำสั่ง BOSS — รอ build/test; P2–P3 รอ | — |

- เมนู Dashboard แบ่งเป็น 2 เมนูย่อย: Executive Dashboard / Net-Zero Pathway
- งาน D1–D2 ต้องผ่านการทดสอบตามแผนนี้ (L1–L6) ก่อนนับว่าเสร็จ

---

## 1. ขอบเขตระบบที่ต้องทดสอบ (สแกนจากโค้ดจริง)

| กลุ่ม | ขอบเขต |
|---|---|
| หน้าจอ | เมนูใน `MainLayout` 25 รายการ, route ที่ active ~40 หน้า (Account/Identity แยก) |
| API v2 | 40 endpoint / 7 controller — auth ด้วย `X-API-Key`, rate limit `PlanRateLimiterService` |
| สิทธิ์ | System roles: Administrator, Staff, User, Corporate, SubscriptionManager · Subscription roles ตาม `eSubscriptionRole` (Owner, Admin, Editor, Consultant, …) · Plan: Basic / Pro / Premium / Enterprise + quota ใน `TbSubscription` |
| งานเบื้องหลัง | `GhgTargetAlertWorker` (อีเมลเตือน >80%), `EventStagingJanitorWorker`, SignalR `/hubs/event-notification` |
| เอกสารออก | Excel import/export ทุกโมดูล, CFO AR5 Excel + Word (เทมเพลต อบก.), ISSB Word, Dashboard Excel |
| Automated test | 152 test ใน 26 ไฟล์ (xUnit + InMemory) |

---

## 2. Gate 0 — ต้องแก้ก่อนเริ่มทดสอบ (พบจากการสแกนโค้ด)

| # | ประเด็น | หลักฐาน | ระดับ |
|---|---|---|---|
| G1 | SignalR Hub ไม่มี `[Authorize]` และ `JoinSubscriptionGroup(int)` ให้ใครก็ได้เข้ากลุ่ม `sub_{id}` → รับแจ้งเตือน Event ของ Subscription อื่นได้ | `Hubs/EventNotificationHub.cs` | Critical |
| G2 | `DatabaseSeeder7` (ข้อมูลองค์กรจำลอง) ถูกเรียกทุกครั้งที่ start → ข้อมูลทดสอบเข้า DB production | `DependencyInjection.cs:230` | Critical |
| G3 | Secret อยู่ใน `appsettings.json` (DB connection, SMTP password) และ `Files/JSON/system_users.json` มีรหัสผ่าน plaintext 7 user | ไฟล์ config / JSON | Critical |
| G4 | Swagger เปิดทุก environment | `DependencyInjection.cs:182` | High |
| G5 | `DetailedErrors = true` ใน `appsettings.json` (ไม่ใช่ไฟล์ Development) | `appsettings.json` | High |
| G6 | ไม่ได้ตั้งค่า DataProtection key storage → ลิงก์เข้ารหัส (`id/sid/ret/SecureId`) และ cookie login อาจใช้ไม่ได้หลัง restart/deploy ขึ้นกับ host | ไม่พบ `AddDataProtection` | High |
| G7 | ไฟล์แนบ/รายงานเก็บใต้ `ContentRootPath/Files` → หายเมื่อ deploy ทับโฟลเดอร์ ถ้าไม่แยกออก | `FileStorageService.cs:44` | High |
| G8 | เมนู "Mail to Users" ชี้ `/admin/mail-to-users` ที่ไม่มีหน้า | `MainLayout.razor:71` | Medium |
| G9 | Route เวอร์ชันเก่ายังเปิดอยู่: `/home1–3`, `/dashboard-v1`, `/organization/company-v2`, `/reports/cfo-reports` (V1) → ตัดสินใจปิด เพื่อลดขอบเขตทดสอบ | `@page` ที่ active | Medium |
| G10 | ใช้ `DateTime.Now/UtcNow/Today` ตรง 110 จุด ขัดกฎ `AppTimeZone` → คัดจุดที่กระทบวันที่ทางธุรกิจ | grep ทั้ง solution | Medium |
| G11 | ยังไม่มีผลรัน `dotnet test` ทั้งชุด; Tests ไม่อยู่ใน slnx | Daily Log 09-23 ถึง 09-25 | Blocker ของการเริ่ม |

**เสร็จเมื่อ:** G1–G7 และ G11 ปิด, G8–G10 มีมติ (แก้/ไม่แก้พร้อมเหตุผล)

---

## 3. สภาพแวดล้อมและข้อมูลทดสอบ
- **Staging** ตั้งค่าเหมือน production (OS/host, MySQL version, HTTPS, SMTP จริงแบบบัญชีทดสอบ, `Environment=Production`)
- **ชุดข้อมูล:** (1) DB เปล่า + master data (EF จาก JSON TGO) (2) `DatabaseSeeder5` 3 ปี 1,044 event (3) `DatabaseSeeder7` (4) ไฟล์ Excel จริงใน `Temp/ทดสอบ-รายงาน/` (Topics 5 สาขา + Events)
- **บัญชีทดสอบ:** 1 คนต่อ 1 role × อย่างน้อย 2 Subscription (ใช้ทดสอบการแยก tenant)

---

## 4. ระดับการทดสอบ

| ระดับ | สิ่งที่ทดสอบ | เกณฑ์ผ่าน |
|---|---|---|
| L1 Automated | 152 test เดิม ทั้งชุด | ผ่าน 100% |
| L2 Functional รายโมดูล | CRUD, ค้นหา/กรอง, Import/Export, Delete Guard, State Persistence (F5 / Back) ของทุกหน้าในเมนู | ทุก test case ผ่าน |
| L3 End-to-End | กระบวนการธุรกิจเต็มสาย (ข้อ 5) | ครบทุกสถานการณ์ |
| L4 สิทธิ์ / Multi-tenant | ทุก role × ทุกเมนู/ปุ่ม, เปลี่ยน `sid`/`id` ใน URL, API Key ข้าม Subscription, Hub | ไม่มีข้อมูลรั่วข้าม tenant แม้แต่กรณีเดียว |
| L5 API v2 | 40 endpoint: สำเร็จ, 400/401/403/404/409/429, idempotency (`externalRefId`), ไฟล์แนบ | ตรงสัญญาใน Api Spec |
| L6 ความถูกต้องตัวเลข | kgCO2e = Qty × Ratio × EF × Share, GWP AR5, ยอด Scope 1/2/3/แยก ตรงกันระหว่าง หน้าจอ ↔ Dashboard ↔ Excel ↔ Word ↔ ISSB | คลาดเคลื่อน ≤ 0.01 tCO2e |
| L7 Non-functional | ปริมาณข้อมูลสูง, ผู้ใช้พร้อมกัน, Browser (Chrome/Edge), การต่อ circuit ใหม่, อีเมล | ไม่มี error ใน log, เวลาตอบสนองยอมรับได้ (BOSS กำหนดค่า) |
| L8 Deploy / Operation | migrate DB เปล่า + DB เดิม, backup/restore (DB + `Files/`), restart, rollback, log rotation 90 วัน | ทำซ้ำได้ตามคู่มือ |

---

## 5. สถานการณ์ทดสอบ (L3 + กรณีผิดปกติ)

| กลุ่ม | สถานการณ์ |
|---|---|
| เริ่มใช้งานใหม่ | สมัคร → ยืนยันอีเมล → Admin Activate Subscription → สร้าง Corporate/Company/Facility → Topics → Events → Dashboard → CFO Report |
| รอบรายงานหลายปี | มี/ไม่มีปีฐาน, ปีฐาน = ปีรายงาน, ปีที่ไม่มีข้อมูล, Create Revision ของรายงานที่ล็อกแล้ว |
| นำเข้า Excel ผิดรูปแบบ | คอลัมน์สลับ, หัวไทย/อังกฤษ, Topic ซ้ำ (`Already Exists`), หน่วยไม่ตรง, EF หาไม่เจอ, ไฟล์ว่าง/ใหญ่เกิน quota |
| Emission Factor | EF หมดอายุ (ต้องกรอกเหตุผล), GWP ไม่ใช่ AR5, Auto-mapping WTT/Biogenic ถูก prefix |
| ลบข้อมูล | ลบ Unit/Group/Source/Topic/Organization ที่ถูกใช้อยู่ → ต้องถูกกัน (`DeleteGuard`) |
| Subscription | หมดอายุ, ต่ออายุ, เกิน quota (Corporate/Company/Facility/Topic/Event/พื้นที่ไฟล์), เปลี่ยน Plan |
| สิทธิ์ผู้ใช้ | ถูกถอด role ระหว่างใช้งาน, ผู้ใช้หลาย Subscription, Viewer พยายาม Export/แก้ไข |
| API | ingest ซ้ำ (idempotent), เกิน 500 รายการ/batch, เกิน rate limit (429), Key หมดอายุ/ถูกเพิกถอน |
| แจ้งเตือน | ยอดแตะ 80% / 100% ของเป้า Net-Zero, cooldown ไม่ส่งซ้ำ, SMTP ล่ม |
| ระบบขัดข้อง | restart ระหว่างสร้างรายงาน, DB หลุดชั่วคราว, ผู้ใช้ 2 คนแก้ข้อมูลเดียวกันพร้อมกัน, กด F5/Back ทุกขั้นของ Wizard |
| เวลา | ข้อมูลช่วงข้ามปี/ข้ามเดือน, เขตเวลา Asia/Bangkok, ชื่อไฟล์ Export ตามมาตรฐาน `{entity}_{name}_{date}_{time}` |

---

## 6. การจัดการ Defect และเกณฑ์ขึ้น Production
- ระดับ: **Critical** (ข้อมูลผิด/รั่ว/ระบบใช้ไม่ได้) · **High** (ฟังก์ชันหลักใช้ไม่ได้ มีทางเลี่ยง) · **Medium** · **Low**
- ทุก defect = 1 ใบงานใน `Documents/agent-jobs/` พร้อมขั้นตอนทำซ้ำ + test ที่แดงก่อนแก้
- **Go-live เมื่อ:** Gate 0 ปิด · L1–L8 ผ่าน · Critical/High = 0 · Medium ทุกข้อมีมติ · ซ้อม deploy + backup/restore สำเร็จ 1 รอบ · มีคู่มือผู้ใช้ของโมดูลในเมนู

## 7. ลำดับดำเนินการ
1. Gate 0 → 2. L1 baseline → 3. เขียน test case รายโมดูลเป็น markdown ที่ `Documents/test-cases/` (ใช้เมนูเป็นสารบัญ) → 4. L2 → L4 → L5 → L6 → L3 → 5. L7, L8 บน Staging → 6. สรุปผล + ตัดสินใจ Go-live

## 8. จุดตัดสินใจของ BOSS
- เกณฑ์เวลาตอบสนองและปริมาณข้อมูลสูงสุดที่ใช้ทดสอบ L7
- G9: route เวอร์ชันเก่าปิดหรือคงไว้
