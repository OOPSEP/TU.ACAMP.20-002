 USE `acamp_production_v010600`;

-- 1. กำหนดชื่อ Organization ที่ต้องการค้นหา
SET @target_org_title = 'บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)';

-- 2. ดึงค่า SubscribeId ไปเก็บไว้ในตัวแปร @subscribe_id
SELECT SubscribeId INTO @subscribe_id 
FROM tb_workspace_organization 
WHERE Title = @target_org_title 
LIMIT 1;

 -- ตรวจสอบค่าตัวแปร (Optional)
SELECT @subscribe_id AS CurrentSubscribeId;

-- 3. ดึงค่า Mission Unit โดยใช้ตัวแปร @subscribe_id
SELECT
    s.Title AS Title,
    s.Memo AS Remark
FROM tb_emission_unit s
WHERE s.SubscribeId = @subscribe_id;

-- 4. ดึงค่า Emission Source โดยใช้ตัวแปร @subscribe_id
-- กำหนดตัวแปร ScopeId ที่ต้องการ (ระบุ Id ของ tb_activities_scope)
SET @scope_id = 1;
-- ดึงค่า Emission Source กรองตาม SubscribeId และ ScopeId
SELECT 
    ROW_NUMBER() OVER (ORDER BY s.Id) AS `Index`,
     s.Id AS Id,
    s.title AS Title,
    g.title AS Emission,
    sc.Title AS Scope,
    u.Title AS Unit,
    s.CO2 AS CO2,
    s.FossilCH4 AS FossilCH4,
    s.CH4 AS CH4,
    s.N2O AS N2O,
    s.SF6 AS SF6,
    s.NF3 AS NF3,
    s.HFCs AS HFCs,
    s.PFCs AS PFCs,
    s.Ref AS Reference
FROM tb_emission_source s
INNER JOIN tb_emission_group g 
    ON s.GroupId = g.Id
INNER JOIN tb_activities_scope sc 
    ON g.ScopeId = sc.Id
LEFT JOIN tb_emission_unit u 
    ON s.UnitId = u.Id
WHERE s.SubscribeId = @subscribe_id;
--  AND sc.Id = @scope_id;

 
SET @target_facility_title = 'สาขาที่ 00003: นนทบุรี (สถานที่ปฏิบัติงานหลัก)';
-- 2. ดึงค่า SubscribeId ไปเก็บไว้ในตัวแปร @subscribe_id
SELECT Id INTO @facility_id 
FROM tb_workspace_facility 
WHERE Title = @target_facility_title 
LIMIT 1;

 -- ตรวจสอบค่าตัวแปร (Optional)
SELECT @facility_id AS FacilityId;

-- 5 ดึงค่า topic จาก ตาราง V16 เพื่อเตรียม import to V20
SELECT 
ROW_NUMBER() OVER (ORDER BY t.Id) AS `Index`,
    t.title AS Title,    
    s.Title AS Source,
    eg.Title AS Emission,
    ag.Title AS Activity,
   
    _as.Title AS Scope,
    un.Title AS Unit,
    (
        (s.CO2 * s.gwpCO2) +
        (s.FossilCH4 * s.gwpFossilCH4) +
        (s.CH4 * s.gwpCH4) +
        (s.N2O * s.gwpN2O) +
        (s.SF6 * s.gwpSF6) +
        (s.NF3 * s.gwpNF3) +
        (s.HFCs * s.gwpHFCs) +
        (s.PFCs * s.gwpPFCs)
    ) AS EF,
     
    t.EmissionSharePercent as Share,
    t.Memo as Remark 
FROM    tb_activities_topic t
INNER JOIN    tb_activities_group ag
    ON t.ActivityGroupId = ag.Id
INNER JOIN    tb_emission_source s 
    ON t.SourceId = s.Id
INNER JOIN    tb_emission_unit un
    ON s.UnitId = un.Id
INNER JOIN    tb_emission_group eg 
    ON s.GroupId = eg.Id
INNER JOIN    tb_activities_scope _as 
    ON ag.ScopeId = _as.Id
WHERE t.SubscribeId = @subscribe_id  and t.FacilityId =199; 
--สาขาที่ 00001: ศรีสะเกษ