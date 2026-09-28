# PLAN: ย้ายข้อมูล ACAMP V16 → V20 (อ่าน DB V16 → ยิง API V20)

- **วันที่:** 2026-09-23
- **เป้าหมาย:** MigrationTool อ่าน V16 (MySQL) ตรง แล้วสร้างข้อมูลใน V20 ผ่าน `api/v2/*` เท่านั้น (ไม่เขียน DB V20 ตรง) — ทำซ้ำได้ (idempotent), ต่อจากจุดที่ค้างได้ (resumable), ตรวจยอดได้ (verifiable)
- **ขอบเขต:** 1 รอบการรัน = 1 V16 Subscription → 1 V20 Subscription
- **นอกขอบเขต:** Users/Roles, ไฟล์แนบของ Event, รายงาน CFO

---

## 1. สถานะปัจจุบัน (ตรวจจากโค้ดจริง)

| ข้อมูล | อ่าน V16 | API V20 | สถานะ |
|---|---|---|---|
| Subscription | มี | ไม่มี | V20 สร้างด้วยมือ; API Key ผูก Subscription อยู่แล้ว → ใช้ Key เป็นตัวกำหนดปลายทาง |
| Emission Unit / Group / Source (custom) | มี | **ไม่มี** | ต้องพัฒนา (G1) |
| Corporate / Company / Facility | มี | มี `api/v2/corporates|companies|facilities` (CRUD, ใช้ Key ไม่ใช้ int Id) | ใช้ได้ |
| Topic | มี | มี `api/v2/topics` แต่รับ `targetId` เป็น **int** ขณะที่ Org API คืนแต่ Key | ต้องแก้ (G2) |
| Event | มี (+ รวมยอดรายเดือนแล้ว) | มี `POST api/v2/events/ingest` (≤500/ครั้ง, `externalRefId` unique ต่อ Subscription) | ใช้ได้ |
| API Key | – | `PageCreateApiKey` hardcode `subscriptionId = 1` | ต้องแก้ (G3) |

## 2. ข้อตกลงการออกแบบ (ปิดประเด็นแล้ว)

1. **ปลายทาง:** V20 Subscription สร้างล่วงหน้าโดย Admin + ออก API Key ระดับ Subscription (ไม่ scope) 1 ดอกต่อการ migrate
2. **Mapping องค์กร:** ใช้ไฟล์ `mapping.json` ที่คนยืนยันก่อน push เสมอ
   - Tool สร้าง draft: จับคู่กับของที่มีใน V20 จาก `Code` ก่อน แล้วค่อยเสนอจากชื่อ (เป็นข้อเสนอเท่านั้น)
   - รายการที่ไม่มีคู่ → `action: create` โดยใช้ `Code = V16-{ORG|SUB|FAC}-{V16Id}` และชื่อตาม V16
   - มีรายการ `action` ว่าง → หยุดทั้งรอบ ห้าม fallback จับด้วยชื่อ
3. **Idempotency:**
   - Library / Org / Topic: ถ้าได้ 409 `*_already_exists` → GET หา Key เดิมแล้วใช้ต่อ
   - Event: `externalRefId = v16:{subIdN8}:{v16TopicId}:{yyyy-MM}` (เพราะรวมยอดรายเดือนแล้ว 1 แถว = 1 เดือน)
4. **State:** `D:\temp\output\{sub}\migration-state.json` เก็บ V16 Id → V20 Key ทุกระดับ + สถานะรายรายการ ใช้ resume
5. **ลำดับโหลด:** Units → Groups → Sources → Corporate → Company → Facility → Topics → Events

## 3. งานพัฒนาฝั่ง V20 (Gaps)

### G1 — Emission Library API
- `GET/POST api/v2/emission-units`, `api/v2/emission-groups`, `api/v2/emission-sources` (custom ของ Subscription ของ Key)
- ใช้กฎตรวจเดียวกับ Excel Import (Title ≤200 unique, Group จับด้วย Title/TitleTh/Code + Scope, Unit จับด้วย Title) — แยก validation ออกจาก `*ExcelService` เป็น service กลาง ห้ามเขียนซ้ำ
- Source รับค่าก๊าซ 8 ตัว **และค่า GWP 8 ตัว + GwpVersion** (optional, default AR5) เพื่อให้ EF ตรงกับ V16 — แก้ปัญหา EF เพี้ยนที่พบใน Excel Import
- 409 `*_already_exists` เมื่อ Title ซ้ำ

### G2 — Topic API
- `POST api/v2/topics` รับ `targetKey` (`crp_`/`cmp_`/`fac_`) แทน `level + targetId` (คง int ไว้เพื่อ backward-compat)
- รับ `orderId` (optional) เพื่อคงลำดับตาม V16
- Response คืน `activityGroup` ที่ระบบเลือกให้ (จาก `TbEmissionMapping`) เพื่อให้ Tool รายงานเทียบกับ V16

### G3 — API Key สำหรับ Subscription ใดก็ได้
- `PageCreateApiKey` เลือก Subscription ได้ (สิทธิ์ Admin) แทน hardcode `1`
- ตั้ง `customRateLimit` และ `expiresAt` ได้ (Key สำหรับ migration ควรหมดอายุสั้น)

## 4. งานพัฒนาฝั่ง MigrationTool

| # | งาน | รายละเอียด |
|---|---|---|
| M1 | `V20ApiClient` | HttpClient + `X-API-Key`, retry 429 ตาม `Retry-After`, retry 5xx (backoff), timeout, log request/response ลงไฟล์ |
| M2 | Config | `V20Api:BaseUrl`, `V20Api:ApiKey` (อ่านจาก user-secrets/env ห้ามเก็บใน appsettings), ลบ `V20Connection` ที่ไม่ได้ใช้ |
| M3 | โหมดคำสั่ง | `--mode export` (ของเดิม) / `plan` / `push` / `verify` และ `--dry-run` |
| M4 | `plan` | อ่าน V16 + GET V20 → สร้าง `mapping.json` (draft) + รายงานสิ่งที่จะสร้าง/ข้าม/ชน |
| M5 | `push` | โหลดตามลำดับข้อ 2.5 จาก `mapping.json` + `migration-state.json`; Event ส่งทีละ ≤500 ต่อ batch; เก็บผลรายรายการ |
| M6 | `verify` | เทียบ V16 vs V20 ผ่าน GET: จำนวน Source/Topic/Event ต่อ Facility และ **ผลรวม Quantity ต่อ Topic ต่อเดือน** → `verify-report.xlsx` |
| M7 | Refactor | แยก `Program.cs` (≈1,100 บรรทัด) เป็น `Extract/`, `Load/`, `Mapping/`, `Reporting/` ก่อนเริ่ม M4 |

## 5. ลำดับการพัฒนา (Jobs)

1. **G3** API Key เลือก Subscription ได้ — ปลดล็อกการทดสอบทุกอย่าง
2. **G2** Topic API รับ `targetKey` + `orderId`
3. **G1** Emission Library API
4. **M7 → M1 → M2 → M3**
5. **M4** plan + mapping.json
6. **M5** push (ทำทีละระดับ: Library → Org → Topic → Event และทดสอบทีละระดับ)
7. **M6** verify
8. **Dry run จริง:** BSCM (`08ddef52-…`) → `acamp_V20_002` (Seeder6 มีโครงสร้าง BSCM 6 สาขาอยู่แล้ว = ทดสอบโหมด mapping กับของที่มีอยู่)

แต่ละข้อแตกเป็นใบงานใน `Documents/agent-jobs/` ตามกติกา README ได้ทันที

## 6. ความเสี่ยงที่รู้แล้ว

- **Activity Group:** V20 เลือก Activity Group ให้อัตโนมัติจาก `TbEmissionMapping` อาจไม่ตรง V16 → รายงานใน `verify` ไม่แก้อัตโนมัติ
- **ชื่อ Topic ซ้ำใน Facility เดียวกันของ V16:** V20 ปฏิเสธ (`topic_already_exists`) → `plan` ต้องรายงานก่อน push
- **Event ไม่ rollback ทั้ง batch:** รายการที่ถูก reject ต้องอยู่ใน state และ push ซ้ำได้
- **Rate limit:** default 600 req/min — Event ใช้ batch จึงไม่เป็นปัญหา; Library/Topic ส่งทีละรายการ

## 7. Definition of Done (ทั้งแผน)

- [ ] รัน `plan → push → verify` กับ BSCM แล้ว `verify-report` ตรง 100% (จำนวน + ผลรวม Quantity ต่อ Topic ต่อเดือน)
- [ ] รัน `push` ซ้ำรอบที่ 2 ไม่เกิดข้อมูลซ้ำ (ทุกรายการเป็น exists/replayed)
- [ ] หยุดกลางทาง (kill process) แล้วรันต่อได้จาก `migration-state.json`
- [ ] Unit test ครอบคลุม G1/G2/G3 และ `V20ApiClient` (retry/429)
