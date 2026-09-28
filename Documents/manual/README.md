# คู่มือผู้ใช้งาน ACAMP 2.0 (User Manual) — สารบัญและแผนการจัดทำ

เอกสารชุดนี้เป็น **คู่มือผู้ใช้งาน (End User)** เขียนเป็น Markdown ทำ **ทีละโมดูล**
โดยยึดตามเมนูจริงใน `Components/Layout/MainLayout.razor` เป็นโครงสารบัญ (เมนูคือสิ่งที่ผู้ใช้เห็น)

## วิธีทำงาน (1 โมดูล = 1 ไฟล์ = 1 รอบงาน)

1. คัดลอก `_TEMPLATE.md` เป็น `NN-ชื่อโมดูล.md`
2. เดินหน้าจอจริงตามลำดับเมนู จับภาพหน้าจอเก็บไว้ที่ `images/` ใช้ชื่อ `<module>-NN.png`
3. เขียนตามหัวข้อในเทมเพลตให้ครบ แล้วปิดโมดูลนั้นก่อนขึ้นโมดูลถัดไป
4. ห้ามเขียนสิ่งที่ยังไม่ได้ทดลองกดจริง ถ้าฟีเจอร์ยังไม่เสร็จให้ข้ามและบันทึกไว้ในตาราง "สถานะ" ด้านล่าง

## ลำดับที่แนะนำ (จากพื้นฐานไปซับซ้อน / จากนิ่งไปยังเปลี่ยนบ่อย)

| ลำดับ | โมดูล | เมนู | URL | ไฟล์ | สถานะ |
|---|---|---|---|---|---|
| 01 | เริ่มต้นใช้งาน (สมัคร/เข้าสู่ระบบ/2FA/โปรไฟล์/รหัสผ่าน) | Account, Settings > Manage Account | `/Account/Login`, `/Account/Manage` | 01-getting-started.md | ยังไม่เริ่ม |
| 02 | คลังหน่วยวัด (Units) | Library > Units | `/library/emission-units` | 02-library-units.md | ยังไม่เริ่ม |
| 03 | คลังแหล่งปล่อย (Sources) + Emission Factor | Library > Sources | `/library/emission-sources` | 03-library-sources.md | ยังไม่เริ่ม |
| 04 | คลังกลุ่มการปล่อย (Emissions) | Library > Emissions | `/library/emission-groups` | 04-library-emissions.md | ยังไม่เริ่ม |
| 05 | คลังกลุ่มกิจกรรม (Activities) | Library > Activities | `/library/activity-groups` | 05-library-activities.md | ยังไม่เริ่ม |
| 06 | คลังหัวข้อกิจกรรม (Topics) + นำเข้า Excel | Library > Topics | `/library/activity-topics` | 06-library-topics.md | ยังไม่เริ่ม |
| 07 | โครงสร้างองค์กร (Corporate / Company / Facility) | Organization | `/organization/corporate` | 07-organization.md | ยังไม่เริ่ม |
| 08 | ข้อมูลกิจกรรมและการวิเคราะห์ | Reports > Activity Data Analytics | `/organization/activity-analytics` | 08-activity-analytics.md | ยังไม่เริ่ม |
| 09 | แดชบอร์ดผู้บริหาร | Dashboard | `/dashboard` | 09-dashboard.md | ยังไม่เริ่ม |
| 10 | รายงาน CFO / ISO 14064 (ทะเบียนรายงาน + Wizard) | Reports > CFO & ISO 14064 Audit Report | `/reports/cfo-reports` | 10-cfo-report.md | ยังไม่เริ่ม |
| 11 | สิทธิ์และสมาชิก (Subscriptions / API Key) | Settings > Subscriptions | `/subscriptions` | 11-subscription.md | ยังไม่เริ่ม |
| 12 | งานผู้ดูแลระบบ (Roles / Confirm Emails / Activate-Expire Subscriptions) | Settings > Admin | `/admin/roles` | 12-admin.md | ยังไม่เริ่ม |

หมายเหตุ: โมดูล 10 (CFO Report) ยังมีการแก้ไขอยู่ ควรทำเป็นลำดับท้าย ๆ เพื่อไม่ต้องเขียนซ้ำ
