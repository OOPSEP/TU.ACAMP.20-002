namespace TU.ACAMP.WebApp.Services.Activity;

/// <summary>
/// [2026-09-24][Claude] อันดับของรายการที่เป็น Hotspot
/// </summary>
/// <param name="Rank">ลำดับจากมากไปน้อย (1 = ปล่อยมากที่สุด)</param>
/// <param name="SharePercent">สัดส่วนของรายการนี้ต่อยอดรวม (%)</param>
/// <param name="CumulativePercent">สัดส่วนสะสมตั้งแต่อันดับ 1 ถึงรายการนี้ (%)</param>
public sealed record HotspotRank(int Rank, decimal SharePercent, decimal CumulativePercent);

/// <summary>
/// [2026-09-24][Claude] วิเคราะห์ GHG Hotspot แบบ Pareto
/// นิยาม: Hotspot = กลุ่มรายการที่ปล่อยมากที่สุด เมื่อเรียงจากมากไปน้อยแล้วรวมกันได้ถึงสัดส่วนสะสมที่กำหนด (ค่าเริ่มต้น 70%)
/// รายการที่ทำให้ยอดสะสมข้ามเส้นเกณฑ์นับเป็น Hotspot ด้วย จึงรับประกันว่ากลุ่ม Hotspot รวมกันแล้ว ≥ เกณฑ์เสมอ
/// (ไม่ใช่นิยามเดิม "แต่ละรายการ ≥ 5% ของยอดรวม")
/// </summary>
public static class HotspotAnalyzer
{
    public const decimal DefaultCumulativePercent = 70m;

    public static Dictionary<TKey, HotspotRank> Analyze<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        Func<TItem, decimal> valueSelector,
        decimal cumulativePercent = DefaultCumulativePercent)
        where TKey : notnull
    {
        #region hide [2026-09-24]
        var result = new Dictionary<TKey, HotspotRank>();
        if (items == null) return result;

        var ranked = items
            .Select(i => (Key: keySelector(i), Value: valueSelector(i)))
            .Where(x => x.Value > 0m)
            .OrderByDescending(x => x.Value) // stable: ค่าเท่ากันคงลำดับเดิม
            .ToList();

        var total = ranked.Sum(x => x.Value);
        if (total <= 0m) return result;

        var cutoff = Math.Clamp(cumulativePercent, 0m, 100m);
        decimal cumulative = 0m;
        int rank = 0;

        foreach (var (key, value) in ranked)
        {
            // หยุดเมื่อยอดสะสมของรายการก่อนหน้าถึงเกณฑ์แล้ว
            if (cumulative * 100m / total >= cutoff) break;

            cumulative += value;
            rank++;
            if (!result.ContainsKey(key))
            {
                result[key] = new HotspotRank(rank, value * 100m / total, cumulative * 100m / total);
            }
        }

        return result;
        #endregion
    }
}
