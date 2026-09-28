// [2026-09-23][Claude] เก็บสถานะการ Migrate ต่อ 1 V16 Subscription (V16 Id → V20 Id/Key) เพื่อให้รันซ้ำ/รันต่อได้
using System.Text.Json;

namespace MigrationTool.Migration;

public sealed class MigrationState
{
    public Guid V16SubscriptionId { get; set; }
    public string V16Title { get; set; } = string.Empty;
    public string ExternalRef { get; set; } = string.Empty;

    public int? V20SubscriptionId { get; set; }
    public string? V20ApiKeyPrefix { get; set; }
    /// <summary>API Key ของ V20 Subscription (ได้ครั้งเดียวตอนออก Key) — ไฟล์นี้ต้องเก็บเป็นความลับ</summary>
    public string? V20ApiKey { get; set; }
    public DateTime? V20ApiKeyExpiresAt { get; set; }

    /// <summary>[2026-09-23][Claude] V16 OrganizationId → V20 CorporateKey (crp_xxx)</summary>
    public Dictionary<int, string> CorporateKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 SubsidiaryId → V20 CompanyKey (cmp_xxx)</summary>
    public Dictionary<int, string> CompanyKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 Emission UnitId (Custom ของ Subscription) → V20 UnitKey (unt_xxx) — อาจชี้ไปหน่วย Default ของ V20 ถ้าชื่อตรงกัน</summary>
    public Dictionary<int, string> UnitKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 Emission GroupId (Custom ของ Subscription) → V20 GroupKey (egp_xxx) — อาจชี้ไปกลุ่ม Default ถ้าชื่อ+Scope ตรงกัน</summary>
    public Dictionary<int, string> GroupKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 Emission SourceId (Custom ของ Subscription) → V20 SourceKey (esr_xxx) — อาจชี้ไป Source Default/Custom ที่ชื่อตรงกัน</summary>
    public Dictionary<int, string> SourceKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 TopicId → V20 TopicKey (tpc_xxx) — ขั้น Event ใช้ต่อ</summary>
    public Dictionary<int, string> TopicKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 FacilityId → V20 FacilityKey (fac_xxx)</summary>
    public Dictionary<int, string> FacilityKeys { get; set; } = new();

    /// <summary>[2026-09-23][Claude] V16 SubsidiaryId ที่เลือก migrate (null = ทั้งหมดใต้ Organization ที่เลือก)</summary>
    public List<int>? SelectedSubsidiaryIds { get; set; }

    /// <summary>[2026-09-23][Claude] V16 FacilityId ที่เลือก migrate (null = ทั้งหมดใต้ Subsidiary ที่เลือก) — ขั้น Topic / Event ใช้ชุดนี้</summary>
    public List<int>? SelectedFacilityIds { get; set; }

    /// <summary>
    /// [2026-09-23][Claude] V16 OrganizationId ที่เลือก migrate (null = ทั้งหมด)
    /// ขั้นถัดไป (Company / Facility / Topic / Event) ทำเฉพาะใต้ Organization ชุดนี้
    /// </summary>
    public List<int>? SelectedOrganizationIds { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class MigrationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>{OutputBasePath}/migration-state/{v16SubscriptionId}.json</summary>
    public static string GetStatePath(string outputBasePath, Guid v16SubscriptionId)
        => Path.Combine(outputBasePath, "migration-state", $"{v16SubscriptionId.ToString().ToLowerInvariant()}.json");

    public static MigrationState? Load(string outputBasePath, Guid v16SubscriptionId)
    {
        #region hide [2026-09-23]
        var path = GetStatePath(outputBasePath, v16SubscriptionId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<MigrationState>(File.ReadAllText(path), JsonOptions);
        #endregion
    }

    public static string Save(string outputBasePath, MigrationState state)
    {
        #region hide [2026-09-23]
        var path = GetStatePath(outputBasePath, state.V16SubscriptionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        state.UpdatedAtUtc = DateTime.UtcNow;

        // เขียนไฟล์ชั่วคราวก่อนแล้วค่อยแทนที่ กันไฟล์เสียถ้าโปรแกรมหยุดกลางคัน
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
        return path;
        #endregion
    }
}
