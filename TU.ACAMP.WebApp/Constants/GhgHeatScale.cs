namespace TU.ACAMP.WebApp.Constants;

/// <summary>
/// [2026-09-24][Claude] สเกลสีแสดงความหนักเบาของปริมาณ GHG เมื่อเทียบกับแถวอื่นในตารางเดียวกัน
/// เบาสุด = เขียว -> ส้มอำพัน -> หนักสุด = แดง, ไม่มีข้อมูล (0) = เทา
/// ทุกเฉดมี contrast กับตัวอักษรสีขาว ≥ 5.1:1
/// </summary>
public static class GhgHeatScale
{
    private static readonly (int R, int G, int B) ColorLow = (0x2E, 0x7D, 0x32);   // เขียว
    private static readonly (int R, int G, int B) ColorMid = (0x9A, 0x5B, 0x00);   // ส้มอำพัน
    private static readonly (int R, int G, int B) ColorHigh = (0xC6, 0x28, 0x28);  // แดง

    public const string NoDataStyle = "background-color: #E0E0E0; color: #424242;";

    /// <summary>ค่าต่ำสุด/สูงสุด นับเฉพาะค่า &gt; 0 (null = ไม่มีแถวที่มีข้อมูล)</summary>
    public static (decimal Min, decimal Max)? GetRange(IEnumerable<decimal> values)
    {
        #region hide [2026-09-24]
        var list = values.Where(v => v > 0m).ToList();
        if (list.Count == 0) return null;
        return (list.Min(), list.Max());
        #endregion
    }

    /// <summary>สัดส่วนความหนัก 0..1 เทียบกับช่วงค่า (null = ไม่มีข้อมูล)</summary>
    public static double? GetIntensity(decimal value, (decimal Min, decimal Max)? range)
    {
        #region hide [2026-09-24]
        if (value <= 0m || range == null) return null;
        var (min, max) = range.Value;
        if (max <= min) return 0d; // มีแถวเดียวหรือเท่ากันหมด ถือว่าไม่มีรายการที่หนักกว่า
        return Math.Clamp((double)((value - min) / (max - min)), 0d, 1d);
        #endregion
    }

    /// <summary>ไล่สี 3 จุด คืนค่าเป็น #RRGGBB</summary>
    public static string GetColor(double t)
    {
        #region hide [2026-09-24]
        t = Math.Clamp(t, 0d, 1d);
        var (lo, hi, local) = t <= 0.5d
            ? (ColorLow, ColorMid, t / 0.5d)
            : (ColorMid, ColorHigh, (t - 0.5d) / 0.5d);

        int r = (int)Math.Round(lo.R + (hi.R - lo.R) * local);
        int g = (int)Math.Round(lo.G + (hi.G - lo.G) * local);
        int b = (int)Math.Round(lo.B + (hi.B - lo.B) * local);
        return $"#{r:X2}{g:X2}{b:X2}";
        #endregion
    }

    /// <summary>ต่อสไตล์สีพื้น/ตัวอักษรท้าย baseStyle ตามระดับความหนัก</summary>
    public static string BuildStyle(string baseStyle, double? intensity)
    {
        #region hide [2026-09-24]
        return intensity == null
            ? $"{baseStyle} {NoDataStyle}"
            : $"{baseStyle} background-color: {GetColor(intensity.Value)}; color: #FFFFFF;";
        #endregion
    }

    /// <summary>ข้อความอธิบายระดับสำหรับ tooltip</summary>
    public static string Describe(double? intensity, string scopeLabel)
    {
        #region hide [2026-09-24]
        return intensity == null
            ? "No GHG data"
            : $"Relative level {Math.Round(intensity.Value * 100):N0}% (0% = lowest, 100% = highest among listed {scopeLabel})";
        #endregion
    }
}
