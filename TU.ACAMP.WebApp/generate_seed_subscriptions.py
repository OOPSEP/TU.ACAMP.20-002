import json
import os
import random

# Seed for reproducibility
random.seed(2026)

plans = ["Basic", "Pro", "Premium", "Enterprise"]
owner_types = ["Juristic", "Individual"]

industries = [
    ("พลังงานสะอาดและพลังงานหมุนเวียน", "Clean Energy & Renewable", "Energy"),
    ("เกษตรกรรมและอาหารแปรรูป", "Agriculture & Food Processing", "Food & Agri"),
    ("อุตสาหกรรมยานยนต์และชิ้นส่วน", "Automotive & Parts Manufacturing", "Auto"),
    ("โลจิสติกส์และการขนส่ง", "Logistics & Supply Chain", "Logistics"),
    ("เคมีภัณฑ์และปิโตรเคมี", "Chemicals & Petrochemicals", "Chemical"),
    ("ก่อสร้างและวัสดุก่อสร้าง", "Construction & Building Materials", "Construction"),
    ("เทคโนโลยีและดิจิทัลเซอร์วิส", "Technology & Digital Services", "Tech"),
    ("โรงแรมและการบริการที่เป็นมิตรต่อสิ่งแวดล้อม", "Green Hospitality & Tourism", "Hospitality"),
    ("สิ่งทอและเครื่องนุ่งห่ม", "Textile & Apparel", "Textile"),
    ("สุขภาพและการแพทย์", "Healthcare & Medical Services", "Healthcare"),
    ("บรรจุภัณฑ์และกระดาษ", "Packaging & Paper Products", "Packaging"),
    ("ค้าปลีกและศูนย์การค้า", "Retail & Shopping Centers", "Retail")
]

prefixes_th = ["บริษัท", "กลุ่มบริษัท", "ห้างหุ้นส่วนจำกัด", "วิสาหกิจชุมชน", "สหกรณ์การเกษตร"]
company_names_th = [
    "สยามพรีเมียร์", "ไทยรุ่งเรือง", "กรุงเทพกรีนเทค", "อีสเทิร์นอีโค", "เชียงใหม่นวัตกรรม",
    "เอเชียคาร์บอน", "ธนบุรีอินดัสทรี", "สมุทรปราการซัพพลาย", "ระยองเคมิคอล", "ชลบุรีแมนูแฟคเจอริ่ง",
    "ภูเก็ตอีโคการ์เด้น", "ขอนแก่นไบโอพาวเวอร์", "นครราชสีมาโซลาร์", "อยุธยาพลาสติก", "สระบุรีซีเมนต์กรุ๊ป",
    "สุราษฎร์ปาล์มออยล์", "สงขลาพอร์ตเซอร์วิส", "ลำพูนอิเล็กทรอนิกส์", "ปราจีนการเกษตร", "ฉะเชิงเทราโลจิสติกส์",
    "เพชรบุรีฟู้ดส์", "ราชบุรีเอเนอร์ยี่", "นครปฐมฟาร์มา", "สมุทรสาครซีฟู้ด", "นนทบุรีเทคโนโลยี",
    "ปทุมธานีแพคเกจจิ้ง", "อุดรธานีกรีนเอนเนอร์ยี่", "อุบลไบโอแก๊ส", "พิษณุโลกพรีซีชั่น", "นครสวรรค์โฮลดิ้ง"
]

first_names_th = [
    "สมชาย", "สมศักดิ์", "สมศรี", "วิภา", "กิตติ", "ประสิทธิ์", "อนุชา", "ประเสริฐ", "วรวุฒิ", "นภัสสร",
    "ธนพล", "พงษ์ศักดิ์", "ศิริพร", "รัตนา", "ชัชวาล", "ณัฐพงษ์", "สุชาติ", "ปิยะ", "เกรียงศักดิ์", "ทิพวรรณ",
    "ชาญชัย", "พิมพา", "อัครเดช", "เบญจวรรณ", "กมลวรรณ", "ธีรพงศ์", "กานดา", "จตุพร", "ธนาคาร", "ศุภชัย"
]

last_names_th = [
    "ใจดี", "สุขสมบูรณ์", "รักสงบ", "มั่นคง", "เจริญผล", "มีทรัพย์", "พัฒนกิจ", "รุ่งเรือง", "วัฒนากูล", "ตั้งมั่น",
    "ประเสริฐสุข", "เลิศวิไล", "มั่งคั่ง", "วิเศษสุข", "ศรีสมบัติ", "พรประเสริฐ", "ชัยชนะ", "เกียรติขจร", "มงคลกุล", "สิริโชค"
]

provinces = [
    ("กรุงเทพมหานคร", "10110", "ถนนสุขุมวิท แขวงคลองเตย เขตคลองเตย"),
    ("กรุงเทพมหานคร", "10120", "ถนนสาทรใต้ แขวงทุ่งมหาเมฆ เขตสาทร"),
    ("กรุงเทพมหานคร", "10900", "ถนนพหลโยธิน แขวงลาดยาว เขตจตุจักร"),
    ("สมุทรปราการ", "10270", "นิคมอุตสาหกรรมบางปู ตำบลแพรกษา อำเภอเมือง"),
    ("ระยอง", "21150", "นิคมอุตสาหกรรมมาบตาพุด ตำบลมาบตาพุด อำเภอเมือง"),
    ("ชลบุรี", "20230", "นิคมอุตสาหกรรมแหลมฉบัง ตำบลทุ่งสุขลา อำเภอศรีราชา"),
    ("เชียงใหม่", "50200", "ถนนนิมมานเหมินท์ ตำบลสุเทพ อำเภอเมือง"),
    ("ขอนแก่น", "40000", "ถนนมิตรภาพ ตำบลในเมือง อำเภอเมือง"),
    ("นครราชสีมา", "30000", "ถนนสุรนารายณ์ ตำบลในเมือง อำเภอเมือง"),
    ("ภูเก็ต", "83000", "ถนนเทพกระษัตรี ตำบลตลาดใหญ่ อำเภอเมือง"),
    ("สระบุรี", "18000", "ถนนพหลโยธิน ตำบลปากเพรียว อำเภอเมือง"),
    ("พระนครศรีอยุธยา", "13000", "นิคมอุตสาหกรรมโรจนะ ตำบลคานหาม อำเภออุทัย")
]

subscriptions = []
subscription_users = []

total_count = 120

for i in range(1, total_count + 1):
    sub_id = i
    plan = plans[(i - 1) % len(plans)]
    
    # Approx 20% Individual, 80% Juristic
    is_individual = (i % 5 == 0)
    owner_type = "Individual" if is_individual else "Juristic"
    
    # Industry & naming
    ind_th, ind_en, ind_code = industries[(i - 1) % len(industries)]
    c_name = company_names_th[(i - 1) % len(company_names_th)]
    p_th = prefixes_th[(i - 1) % len(prefixes_th)]
    
    fn = first_names_th[(i - 1) % len(first_names_th)]
    ln = last_names_th[(i - 1) % len(last_names_th)]
    contact_person = f"{fn} {ln}"
    
    if is_individual:
        title = f"ฟาร์ม/ธุรกิจ {contact_person} ({ind_code})"
        owner_name = contact_person
    else:
        title = f"{p_th} {c_name} จำกัด ({ind_code})"
        owner_name = f"{p_th} {c_name} จำกัด"
        
    tax_id = f"0105558{i:06d}"
    owner_email = f"corp{i:03d}@acamp-demo.com" if not is_individual else f"user{i:03d}@acamp-demo.com"
    phone_prefix = "02" if not is_individual and i % 2 == 0 else "08"
    owner_phone = f"{phone_prefix}-{random.randint(100, 999)}-{random.randint(1000, 9999)}"
    
    prov, postcode, street = provinces[(i - 1) % len(provinces)]
    billing_address = f"เลขที่ {random.randint(1, 999)}/{random.randint(1, 99)} {street} จังหวัด{prov} {postcode}"
    
    # Dates & Status:
    # 1-90 Active (valid in future)
    # 91-110 Expired (expiry in past)
    # 111-120 Inactive (IsActive = False)
    is_active = True
    if i > 110:
        is_active = False
        start_offset_months = -random.randint(6, 12)
        exp_offset_months = random.randint(1, 6)
        remarks = f"Subscription inactive / suspended (Test Case #{i:03d})"
    elif i > 90:
        is_active = True
        start_offset_months = -random.randint(13, 24)
        exp_offset_months = -random.randint(1, 5) # Expired in the past!
        remarks = f"Expired subscription awaiting contract renewal (Test Case #{i:03d})"
    else:
        is_active = True
        start_offset_months = -random.randint(1, 6)
        exp_offset_months = random.randint(3, 18)
        remarks = f"Active standard enterprise accounting tenant - {ind_en} (Test Case #{i:03d})"
        
    sub_data = {
        "Id": sub_id,
        "Title": title,
        "TaxId": tax_id,
        "OwnerName": owner_name,
        "OwnerEmail": owner_email,
        "OwnerPhone": owner_phone,
        "ContactPerson": contact_person,
        "BillingAddress": billing_address,
        "SubscriptionPlan": plan,
        "OwnerType": owner_type,
        "StartDateOffsetMonths": start_offset_months,
        "ExpiryDateOffsetMonths": exp_offset_months,
        "IsActive": is_active,
        "Remarks": remarks
    }
    subscriptions.append(sub_data)
    
    # Also create 1-3 subscription users per subscription
    roles = ["Owner", "Admin", "Editor", "Consultant", "Viewer"]
    user_count = 1 if plan == "Basic" else (2 if plan == "Pro" else 3)
    
    # First user is Owner
    subscription_users.append({
        "Email": owner_email,
        "Role": "Owner",
        "SubscriptionId": sub_id,
        "Remarks": "Primary Account Owner"
    })
    
    if user_count >= 2:
        editor_email = f"editor{i:03d}@acamp-demo.com"
        subscription_users.append({
            "Email": editor_email,
            "Role": "Editor",
            "SubscriptionId": sub_id,
            "Remarks": "Data Input Officer"
        })
        
    if user_count >= 3:
        auditor_email = f"auditor{i:03d}@acamp-demo.com"
        subscription_users.append({
            "Email": auditor_email,
            "Role": "Consultant",
            "SubscriptionId": sub_id,
            "Remarks": "External ESG Auditor"
        })

output_dir = r"D:\2026\Projects\ACAMP\V20\CODES\TU.ACAMP.20-002\TU.ACAMP.WebApp\Files\JSON"
os.makedirs(output_dir, exist_ok=True)

subs_file = os.path.join(output_dir, "system_subscriptions.json")
users_file = os.path.join(output_dir, "system_subscription_users.json")

with open(subs_file, "w", encoding="utf-8") as f:
    json.dump(subscriptions, f, ensure_ascii=False, indent=2)

with open(users_file, "w", encoding="utf-8") as f:
    json.dump(subscription_users, f, ensure_ascii=False, indent=2)

print(f"Successfully generated {len(subscriptions)} subscriptions to {subs_file}")
print(f"Successfully generated {len(subscription_users)} subscription users to {users_file}")
