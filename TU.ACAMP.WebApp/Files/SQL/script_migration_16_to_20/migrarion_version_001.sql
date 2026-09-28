
-- ดึงค่า subscription id โดยใช่ขื่อ organization
use `acamp_production_v010600`;
SELECT Id,Title,SubscribeId FROM acamp_production_v010600.tb_workspace_organization where title='บริษัท บางซื่อโรงสีไฟเจียเม้ง จำกัด (BSCM)';
-- 08df03d7-bd61-455d-8875-d9bfdf1528c5

-- ดึงค่า missionsource จาก ตาราง V16 เพื่อเตรียม import to V20
 use `acamp_production_v010600`;
SELECT 
    ROW_NUMBER() OVER (ORDER BY s.Id) AS `Index`,
    s.title AS Title,
    g.title AS Emission,
    sc.Title AS Scope,
    u.Title AS Unit,
    s.CO2 as CO2,
    s.FossilCH4 as FossilCH4,
s.CH4  as CH4,
s.N2O as N2O,
s.SF6 as SF6,
s.NF3 as NF3,
s.HFCs as HFCs,
s.PFCs as PFCs,
s.Ref as Reference

FROM tb_emission_source s
LEFT JOIN    tb_emission_group g 
    ON s.GroupId = g.Id
LEFT JOIN    tb_activities_scope sc 
    ON g.ScopeId = sc.Id
LEFT JOIN    tb_emission_unit u 
    ON s.UnitId = u.Id
WHERE s.SubscribeId = '08df03d7-bd61-455d-8875-d9bfdf1528c5';

-- ดึงค่า mission unit จาก ตาราง V16 เพื่อเตรียม import to V20
use `acamp_production_v010600`;
SELECT
 s.Title AS Title,
 s.Memo as Remark
FROM    tb_emission_unit s
WHERE  SubscribeId = '08df03d7-bd61-455d-8875-d9bfdf1528c5';


-- ดึงค่า topic จาก ตาราง V16 เพื่อเตรียม import to V20
USE acamp_production_v010600;

SELECT 
ROW_NUMBER() OVER (ORDER BY t.Id) AS `Index`,
    t.title AS Title,    
    s.Title AS Source,
    eg.Title AS Emission,
    ag.Title AS Activity,
    un.Title AS Unit,
    _as.Title AS Scope,
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
WHERE t.SubscribeId = '08df03d7-bd61-455d-8875-d9bfdf1528c5' and t.FacilityId =199; --สาขาที่ 00001: ศรีสะเกษ


-- ปัญหาคือเรา map กับ SubscribeId ไม่ได้ ต้อง map กับ facilityId ของ organization