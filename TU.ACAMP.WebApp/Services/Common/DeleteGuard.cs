namespace TU.ACAMP.WebApp.Services;

/// <summary>
/// [2026-09-24] รายการที่อ้างอิงถึงข้อมูลที่กำลังจะถูกลบ (เช่น Unit ถูกอ้างโดย Emission Source)
/// Kind = ชนิดของต้นทางที่อ้างอิง (เช่น "emission source", "topic"), Title = ชื่อของต้นทาง
/// </summary>
public sealed record DeleteReference(string Kind, string Title);

/// <summary>
/// [2026-09-24] กลไกกลางป้องกันการลบข้อมูลที่ยังถูกอ้างอิงอยู่ (Delete Protection)
/// ใช้ข้อความรูปแบบเดียวกันทุกหน้า: แสดงชื่อต้นทางแรก + "and others" หากมีมากกว่า 1 รายการ
/// และแจ้งให้ผู้ใช้ไปปลดการอ้างอิงที่ต้นทางก่อน
/// </summary>
public static class DeleteGuard
{
    /// <summary>
    /// ตัดรายการซ้ำ (Kind + Title) โดยคงลำดับเดิมไว้ เพื่อให้ "ชื่อแรก" คงที่ตามลำดับที่ service ส่งมา
    /// </summary>
    public static List<DeleteReference> Normalize(IEnumerable<DeleteReference> references)
    {
        #region hide [2026-09-24]
        return references
            .Where(r => !string.IsNullOrWhiteSpace(r.Title))
            .Select(r => new DeleteReference(r.Kind, r.Title.Trim()))
            .Distinct()
            .ToList();
        #endregion
    }

    /// <summary>
    /// สร้างข้อความเตือน หากไม่มีการอ้างอิงจะคืนค่า null (ลบได้)
    /// </summary>
    /// <param name="entityLabel">ชื่อชนิดของข้อมูลที่จะลบ เช่น "emission unit"</param>
    /// <param name="references">รายการต้นทางที่อ้างอิงอยู่ (ควรผ่าน Normalize แล้ว)</param>
    public static string? BuildBlockMessage(string entityLabel, IReadOnlyList<DeleteReference> references)
    {
        #region hide [2026-09-24]
        if (references == null || references.Count == 0)
        {
            return null;
        }

        var first = references[0];
        var others = references.Count > 1 ? " and others" : string.Empty;

        return $"This {entityLabel} is currently used by {first.Kind} '{first.Title}'{others}. " +
               $"To delete it, please unassign it at the source first.";
        #endregion
    }
}
