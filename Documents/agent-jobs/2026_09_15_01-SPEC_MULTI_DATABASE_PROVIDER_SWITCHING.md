# SPEC: Multi-Database Provider Switching (MySQL ↔ PostgreSQL)

- **วันที่ออกใบสั่งงาน:** 2026-09-15
- **ผู้ออกใบสั่งงาน:** BOSS
- **ขอบเขต:** ทำให้ `TU.ACAMP.20-002` สลับฐานข้อมูลระหว่าง **MySQL (Pomelo)** และ **PostgreSQL (Npgsql)** ได้ด้วยการตั้งค่าใน `appsettings.json` โดยไม่แก้โค้ด และไม่กระทบระบบเดิมที่รันบน MySQL อยู่

---

## 1. สถานะปัจจุบัน (จากการสแกนโค้ดจริง 2026-09-15)

| หัวข้อ | ผลการตรวจ |
|---|---|
| Provider ที่ใช้ | `Pomelo.EntityFrameworkCore.MySql` 9.0.0 (EF Core 9.0.0, TFM `net10.0`) |
| จุดที่ผูก provider | **2 จุดเท่านั้น** — `Services/Common/DependencyInjection.cs` (บรรทัด ~32) และ `Data/ApplicationDbContextFactory.cs` (บรรทัด ~21) โค้ดซ้ำกันทั้งสองจุด |
| Connection string | `ConnectionStrings:DefaultConnection` (MySQL, port 3306) |
| Raw SQL ในระบบ | **ไม่พบเลย** — สแกน `FromSqlRaw` / `ExecuteSqlRaw` / `SqlQuery<>` / `GetDbConnection` ทั่วทั้ง Services, Controllers, Data, Domain แล้วไม่พบแม้แต่จุดเดียว ทุกอย่างเป็น LINQ ล้วน |
| `HasColumnType` / `HasCharSet` / `Collation` ในโค้ด | **ไม่พบเลย** — model ใช้ convention ล้วน (พบเฉพาะ `[DatabaseGenerated(Identity)]` ใน `EntityBase.cs`) |
| Migration | มีชุดเดียว `20260913031048_v1` (25 ตาราง) และ **ผูก MySQL 100%** — `datetime(6)` ×64, `tinyint(1)` ×53, `longtext` ×25, `decimal(65,30)` ×22, annotation `MySql:CharSet` ×201, `MySql:ValueGenerationStrategy` ×20 |
| `ApplicationDbContextModelSnapshot` | ฝัง `HasColumnType` ของ MySQL ไว้ครบเช่นกัน → **snapshot ชุดเดียวใช้สอง provider ไม่ได้** |
| Tests | `TU.ACAMP.Tests` ใช้ `Microsoft.EntityFrameworkCore.InMemory` → ไม่ผูก provider |

**สรุป:** โค้ดฝั่ง business logic สะอาดมากและแทบไม่ต้องแก้ ปัญหาทั้งหมดกระจุกอยู่ที่ **ชั้น Migration + การลงทะเบียน DbContext + ชนิดข้อมูลบางตัว** เท่านั้น

---

## 2. สถาปัตยกรรมเป้าหมาย

```
TU.ACAMP.WebApp
 └─ Data/Providers/DatabaseProvider.cs            (enum: MySql, PostgreSql)
 └─ Data/Providers/DbContextRegistration.cs       (จุดเดียวที่เลือก provider — ใช้ร่วมกับ design-time factory)
TU.ACAMP.Migrations.MySql        (class library — เก็บ migration + snapshot ของ MySQL)
TU.ACAMP.Migrations.PostgreSql   (class library — เก็บ migration + snapshot ของ PostgreSQL)
```

ตั้งค่าใน `appsettings.json`:

```json
"Database": { "Provider": "MySql" },
"ConnectionStrings": {
  "MySql": "Server=...;Port=3306;...",
  "PostgreSql": "Host=...;Port=5432;Database=acamp_V20_002;Username=...;Password=..."
}
```

> คง `DefaultConnection` เดิมไว้เป็น fallback ในเฟสแรก เพื่อไม่ให้เครื่อง dev ที่ยังไม่ได้แก้ config พัง

---

## 3. แผนดำเนินงาน (6 เฟส)

### Phase 1 — รวมจุดลงทะเบียน DbContext ให้เหลือจุดเดียว (ยังเป็น MySQL ล้วน)
1. สร้าง `Data/Providers/DatabaseProvider.cs` (enum + parser จาก config)
2. สร้าง extension `AddApplicationDbContext(IServiceCollection, IConfiguration)` ที่ `switch` ตาม provider แล้วคืน `DbContextOptionsBuilder` — ย้ายโค้ด `UseMySql(...)` ทั้งบล็อก (รวม `SchemaBehavior(Ignore)` + `UseQuerySplittingBehavior(SplitQuery)`) เข้ามาไว้ที่นี่
3. แก้ `DependencyInjection.cs` และ `ApplicationDbContextFactory.cs` ให้เรียก extension ตัวเดียวกัน — **ลบโค้ดซ้ำทิ้ง**
4. `dotnet build` + `dotnet test` ต้องผ่านเท่าเดิม (ยังไม่มีอะไรเปลี่ยนเชิงพฤติกรรม)

### Phase 2 — แยก Migration Assembly ต่อ provider
1. สร้างโปรเจกต์ `TU.ACAMP.Migrations.MySql` และ `TU.ACAMP.Migrations.PostgreSql` (class library, `net10.0`) ทั้งคู่ reference `TU.ACAMP.WebApp`
2. **ย้าย** ไฟล์ `Migrations/20260913031048_v1.cs`, `.Designer.cs`, `ApplicationDbContextModelSnapshot.cs` เดิมไปไว้ใน `TU.ACAMP.Migrations.MySql` — เปลี่ยนเฉพาะ `namespace` **ห้ามเปลี่ยน MigrationId `20260913031048_v1`** เพราะฐานข้อมูลที่รันอยู่บันทึกไว้ในตาราง `__EFMigrationsHistory` แล้ว ถ้าเปลี่ยนจะถูกมองว่าเป็น migration ใหม่และพยายามสร้างตารางซ้ำ
3. ระบุ `mysqlOptions.MigrationsAssembly("TU.ACAMP.Migrations.MySql")` และ `npgsqlOptions.MigrationsAssembly("TU.ACAMP.Migrations.PostgreSql")` ใน extension ของ Phase 1
4. รันฐานข้อมูล MySQL เดิมยืนยันว่า `MigrateAsync()` **ไม่สร้าง migration ใหม่** (ต้องขึ้น "No migrations were applied")

### Phase 3 — เพิ่ม Npgsql + design-time factory รับ argument
1. เพิ่ม `Npgsql.EntityFrameworkCore.PostgreSQL` เวอร์ชัน `9.0.x` ให้ตรง major กับ EF Core 9.0.0 ที่ใช้อยู่ (**ตรวจเวอร์ชันจริงจาก NuGet ก่อนใส่ ห้ามเดา**)
2. แก้ `ApplicationDbContextFactory` ให้อ่าน provider จาก args ท้ายคำสั่ง:
   `dotnet ef migrations add Init --project TU.ACAMP.Migrations.PostgreSql --startup-project TU.ACAMP.WebApp -- --provider PostgreSql`
3. สร้าง migration แรกของ PostgreSQL (`InitPostgres`) แล้วตรวจไฟล์ที่ได้ว่าไม่มี `MySql:` annotation หลงเหลือ

### Phase 4 — จัดการชนิดข้อมูลที่ต่างกันระหว่าง provider
ทำใน `ApplicationDbContext.OnModelCreating` โดยแยกสาขาด้วย `Database.IsNpgsql()` / `Database.IsMySql()`:

| ประเด็น | MySQL ปัจจุบัน | PostgreSQL | สิ่งที่ต้องทำ |
|---|---|---|---|
| `JsonNode` (4 คอลัมน์: `TbEmissionSource.JsonDataFiles`, `TbActivityTopic.JsonDataFiles`, `TbActivityEvent.JsonDataFiles`, `TbEventStaging.PayloadJson`) | `json` / `longtext` | default เป็น `text` | กำหนด `HasColumnType("jsonb")` เมื่อเป็น Npgsql |
| `decimal` ไม่ระบุ precision | `decimal(65,30)` ×22 คอลัมน์ | `numeric` ไม่จำกัด | กำหนด `HasPrecision(18, 6)` ใน `ConfigureConventions` ให้เท่ากันทั้งสอง provider (ค่า precision ต้องให้ BOSS เคาะก่อน) |
| `bool` | `tinyint(1)` | `boolean` | EF แปลงให้เอง ไม่ต้องทำอะไร |
| `int` identity | `AUTO_INCREMENT` | `GENERATED BY DEFAULT AS IDENTITY` | EF จัดการเอง แต่ต้องทดสอบ seeder ที่กำหนด Id เองใน `DatabaseSeeder3.cs:328` (`Id = 1`) |
| index name | `ux_api_key_prefix` ฯลฯ | ยาวไม่เกิน 63 ตัวอักษร | ผ่าน ไม่ต้องแก้ |

### Phase 5 — แก้ประเด็นพฤติกรรมต่างกัน (สำคัญที่สุด — ดูหัวข้อ 4)
1. แก้เรื่อง `DateTimeKind` ของคอลัมน์ BUSINESS DATE
2. แก้เรื่อง case-sensitivity ของการเทียบสตริงฝั่ง DB
3. เขียน unit test ครอบทั้งสองข้อก่อนแก้ (ต้องเห็นแดงจริงก่อน)

### Phase 6 — ทดสอบและส่งมอบ
1. รัน `dotnet build` + `dotnet test` — 0 Error / 0 Warning
2. ตั้ง `Database:Provider = PostgreSql` ชี้ฐานเปล่า → `MigrateAsync()` + seeder ต้องผ่าน
3. ทดสอบ smoke test บน PostgreSQL: login, หน้า Topics, Import Excel (Topic + Event), หน้า Dashboard, CFO Report, API v2 (`api/v2/topics` CRUD + ingest event)
4. สลับกลับเป็น `MySql` แล้วยืนยันว่าระบบเดิมยังทำงานปกติ 100%
5. เขียน `Logs/Daily-Log-2026-09-15.md` ตามมาตรฐานใหม่

---

## 4. ความเสี่ยงที่ตรวจพบจริงและต้องแก้ (ห้ามข้าม)

### 4.1 [สูงสุด] `DateTimeKind.Unspecified` จะทำให้ Npgsql โยน exception
`AppTimeZone.UtcNow` คืน `DateTime.UtcNow` (Kind = Utc) ซึ่งปลอดภัย **แต่** `AppTimeZone` มีเมธอดที่คืน `DateTimeKind.Unspecified` ออกมาโดยตั้งใจ (บรรทัด 105, 127, 139) สำหรับค่าประเภท BUSINESS DATE — Npgsql จะ map `DateTime` ไปเป็น `timestamp with time zone` และ **โยน `ArgumentException` ทันทีถ้า Kind ไม่ใช่ Utc** ขณะที่ MySQL ไม่สนใจเรื่องนี้เลย
**วิธีแก้:** แยกให้ชัดว่าคอลัมน์ไหนเป็น INSTANT และคอลัมน์ไหนเป็น BUSINESS DATE แล้ว map BUSINESS DATE เป็น `timestamp without time zone` (หรือเปลี่ยนเป็น `DateOnly` ถ้าเป็นวันที่ล้วน) ส่วน INSTANT ใช้ converter `DateTimeUtcValueConverter` เดิมที่บังคับ Kind = Utc อยู่แล้ว

### 4.2 [สูง] การเทียบสตริงฝั่ง DB จะเปลี่ยนพฤติกรรม
MySQL collation `utf8mb4` ค่าเริ่มต้นเทียบแบบ **ไม่สนตัวพิมพ์** แต่ PostgreSQL เทียบแบบ **สนตัวพิมพ์** จุดที่ได้รับผลกระทบโดยตรง เช่น
- `CompanyService.cs:212, 316, 468` — `c.Code == company.Code` (เช็ค duplicate code)
- `FacilityService.cs:230` — `f.Title == title` (วนหาชื่อที่ไม่ซ้ำ)

บน PostgreSQL คำว่า `"ABC"` กับ `"abc"` จะกลายเป็นคนละค่า ทำให้ **สร้างข้อมูลซ้ำได้** และขัดกับกฎ `Already Exists` ใน `GEMINI.md`
**วิธีแก้:** normalize ทั้งสองฝั่งด้วย `ToLower()` ในคิวรี หรือใช้ `EF.Functions.ILike` เฉพาะ Npgsql — ต้องเลือกแนวทางเดียวแล้วใช้ให้เหมือนกันทุกจุด
> หมายเหตุ: ตรรกะจับคู่ใน `ActivityTopicExcelService` ใช้ `StringComparison.OrdinalIgnoreCase` บน collection ใน memory → **ไม่ได้รับผลกระทบ** ตรวจสอบแล้ว

### 4.3 [กลาง] Migration เดิมย้ายผิดวิธี = ข้อมูล production พัง
ห้ามใช้วิธี delete แล้ว `migrations add` ใหม่ ต้องย้ายไฟล์และคง MigrationId เดิมตาม Phase 2 ข้อ 2

### 4.4 [กลาง] `SchemaBehavior(MySqlSchemaBehavior.Ignore)` เป็น API ของ Pomelo เท่านั้น
ต้องอยู่ในสาขา MySQL ของ extension เท่านั้น ถ้าหลุดออกมาระดับบนจะคอมไพล์ไม่ผ่านเมื่อไม่มี Pomelo

### 4.5 [ต่ำ] Seeder ที่กำหนด PK เอง
`DatabaseSeeder3.cs:328` กำหนด `Id = 1` ตรง ๆ — บน PostgreSQL การ insert ทับ identity column ต้องทดสอบว่าลำดับ sequence ไม่เพี้ยนหลัง seed

---

## 5. สิ่งที่ยังต้องให้ BOSS ตัดสินใจก่อนเริ่ม

1. ค่า `HasPrecision` ของคอลัมน์ decimal ทั้ง 22 จุด — ใช้ `(18, 6)` ตามที่เสนอ หรือค่าอื่น
2. ฐาน PostgreSQL ที่จะใช้ทดสอบ — host/port/credential
3. ต้องรองรับ **สลับสองทาง** (ย้ายข้อมูลจริงข้าม provider) หรือแค่ **เลือกตอน deploy** (ฐานใหม่ต่อ provider) — ใบงานนี้ออกแบบไว้แบบหลัง ถ้าต้องย้ายข้อมูลจริงต้องออกใบงานแยกเรื่อง data migration

---

## Definition of Done

- [ ] `appsettings.json` มี `Database:Provider` และ connection string แยกต่อ provider
- [ ] จุดลงทะเบียน DbContext เหลือ **จุดเดียว** ที่ใช้ร่วมกันทั้ง runtime และ design-time
- [ ] มีโปรเจกต์ migration แยก 2 ตัว และ migration เดิมของ MySQL ย้ายเข้าที่ใหม่โดย MigrationId ไม่เปลี่ยน
- [ ] `dotnet ef migrations add` ทำงานได้ทั้งสอง provider ด้วยการส่ง `-- --provider <name>`
- [ ] รันบน MySQL เดิม: `MigrateAsync()` ไม่สร้าง migration ใหม่ และระบบทำงานปกติ 100%
- [ ] รันบน PostgreSQL ฐานเปล่า: migrate + seed + smoke test 6 จุดตาม Phase 6 ข้อ 3 ผ่านทั้งหมด
- [ ] แก้ประเด็น `DateTimeKind` (4.1) และ case-sensitivity (4.2) พร้อม unit test ที่เห็นแดงจริงก่อนแก้
- [ ] `dotnet build` — 0 Error / 0 Warning
- [ ] `dotnet test` — ผ่านครบทุกเคส และระบุจำนวนจริง
- [ ] บันทึก `Logs/Daily-Log-YYYY-MM-DD.md` ตามมาตรฐาน

---

## Result

<!-- agent กรอกตามแม่แบบใน Documents/agent-jobs/README.md เมื่อทำเสร็จ แล้วจึง rename เป็น _ready_<agentname> -->
