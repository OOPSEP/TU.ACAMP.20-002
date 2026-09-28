namespace TU.ACAMP.WebApp.Services.Reports;

/// <summary>
/// [2026-09-23][Claude] ปริมาณการปล่อยแยกรายก๊าซ (tCO2e) ของ audit line หนึ่งรายการ
/// </summary>
public readonly record struct CfoGasBreakdown(
    decimal CO2,
    decimal FossilCH4,
    decimal CH4,
    decimal N2O,
    decimal SF6,
    decimal NF3,
    decimal HFCs,
    decimal PFCs)
{
    public decimal Total => CO2 + FossilCH4 + CH4 + N2O + SF6 + NF3 + HFCs + PFCs;

    public static CfoGasBreakdown operator +(CfoGasBreakdown a, CfoGasBreakdown b) => new(
        a.CO2 + b.CO2,
        a.FossilCH4 + b.FossilCH4,
        a.CH4 + b.CH4,
        a.N2O + b.N2O,
        a.SF6 + b.SF6,
        a.NF3 + b.NF3,
        a.HFCs + b.HFCs,
        a.PFCs + b.PFCs);
}

/// <summary>
/// [2026-09-23][Claude] ตัวคำนวณกลาง: tCO2e แยกรายก๊าซ ต่อ 1 audit line
/// สูตรเดียวกับสูตรในเทมเพลต Excel Fr-04.1 คอลัมน์ Q:
///   ปริมาณที่แปลงแล้ว (Quantity x Ratio x Share%) x EF รายก๊าซ x GWP / 1000
/// ใช้ค่า GWP ที่ติดมากับ line (ค่า default AR5 ถูกเติมไว้ตอน GenerateReportAsync แล้ว)
/// Pure function ไม่แตะฐานข้อมูล จึงใช้ได้ทั้งตอนสร้างจากข้อมูลสดและจาก snapshot JSON
/// </summary>
public static class CfoGasEmissionCalculator
{
    private const decimal KgPerTon = 1000m;

    public static CfoGasBreakdown Calculate(CfoReportService.ActivityAuditLineDto line)
    {
        var qty = line.ConvertedQuantity;

        return new CfoGasBreakdown(
            CO2: qty * line.CO2 * line.gwpCO2 / KgPerTon,
            FossilCH4: qty * line.FossilCH4 * line.gwpFossilCH4 / KgPerTon,
            CH4: qty * line.CH4 * line.gwpCH4 / KgPerTon,
            N2O: qty * line.N2O * line.gwpN2O / KgPerTon,
            SF6: qty * line.SF6 * line.gwpSF6 / KgPerTon,
            NF3: qty * line.NF3 * line.gwpNF3 / KgPerTon,
            HFCs: qty * line.HFCs * line.gwpHFCs / KgPerTon,
            PFCs: qty * line.PFCs * line.gwpPFCs / KgPerTon);
    }

    public static CfoGasBreakdown Sum(IEnumerable<CfoReportService.ActivityAuditLineDto> lines)
    {
        var total = new CfoGasBreakdown();
        foreach (var line in lines)
        {
            total += Calculate(line);
        }
        return total;
    }
}
