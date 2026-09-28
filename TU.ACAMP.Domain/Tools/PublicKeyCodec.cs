using System;

namespace TU.ACAMP.Domain.Tools;

/// <summary>
/// Codec helper for public identifier keys (e.g. tpc_9f3a... for TopicKey and evt_1a2b... for EventKey).
/// Ensures key format validation, prefix checking, and safe AES Guid decryption.
/// </summary>
public static class PublicKeyCodec
{
    public const string TopicPrefix = "tpc";
    public const string EventPrefix = "evt";

    // [2026-09-18][Claude] Public key prefixes สำหรับ Organization REST API (api/v2) — ไม่เปิด integer Id ออกสู่ภายนอก
    public const string CorporatePrefix = "crp";
    public const string CompanyPrefix = "cmp";
    public const string FacilityPrefix = "fac";

    // [2026-09-23][Claude] Public key prefixes สำหรับ Emission Library REST API (api/v2)
    public const string UnitPrefix = "unt";
    public const string EmissionGroupPrefix = "egp";
    public const string EmissionSourcePrefix = "esr";

    /// <summary>
    /// Encodes an integer primary key into a prefixed public key string (e.g. tpc_32hexchars).
    /// </summary>
    public static string Encode(string prefix, int id)
    {
        #region hide [2026-08-24]
        if (id <= 0) return string.Empty;
        var cleanPrefix = prefix.TrimEnd('_').ToLowerInvariant();
        var guid = Cryption.EncryptIntToGuid(id);
        return $"{cleanPrefix}_{guid:N}";
        #endregion
    }

    /// <summary>
    /// Attempts to decode a prefixed public key string into an integer primary key.
    /// Returns false if format, prefix, or hex is invalid, or if decoded ID <= 0.
    /// </summary>
    public static bool TryDecode(string prefix, string? key, out int id)
    {
        #region hide [2026-08-24]
        id = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var cleanPrefix = prefix.TrimEnd('_').ToLowerInvariant() + "_";
        if (!key.StartsWith(cleanPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var hexPart = key.Substring(cleanPrefix.Length);
        if (hexPart.Length != 32) return false;

        if (!Guid.TryParseExact(hexPart, "N", out var guid)) return false;

        id = Cryption.DecryptGuidToInt(guid);
        return id > 0;
        #endregion
    }
}
