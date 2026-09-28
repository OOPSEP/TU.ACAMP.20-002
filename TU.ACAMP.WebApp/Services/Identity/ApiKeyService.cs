using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Identity;

public class ApiKeyService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IMemoryCache _cache;

    public ApiKeyService(IDbContextFactory<ApplicationDbContext> dbFactory, IMemoryCache cache)
    {
        #region hide [2026-08-24]
        _dbFactory = dbFactory;
        _cache = cache;
        #endregion
    }

    /// <summary>
    /// Generates a new API Key for a subscription. Returns the raw key string ONCE for display.
    /// </summary>
    public async Task<(TbApiKey Entity, string RawKey)> CreateApiKeyAsync(int subscriptionId, string keyName, TU.ACAMP.Domain.Enums.eTopicLevel? scopeLevel = null, int? scopeTargetId = null, int? customRateLimit = null, DateTime? expiresAt = null)
    {
        #region hide [2026-08-24]
        var randomBytes = new byte[20];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        var hex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        var prefix = "acamp_" + hex.Substring(0, 8); // 15 chars
        var secretPart = hex.Substring(8); // 32 hex chars
        var rawKey = $"{prefix}_{secretPart}";

        var keyHash = ComputeSha256(rawKey);

        var entity = new TbApiKey
        {
            SubscriptionId = subscriptionId,
            ScopeLevel = scopeLevel,
            ScopeTargetId = scopeTargetId,
            KeyName = keyName.Trim(),
            KeyPrefix = prefix,
            KeyHash = keyHash,
            ExpiresAt = expiresAt,
            CustomRateLimitPerMin = customRateLimit,
            IsActive = true
        };

        using var db = await _dbFactory.CreateDbContextAsync();
        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync();

        return (entity, rawKey);
        #endregion
    }

    /// <summary>
    /// Gets the first active API Key entity for a subscription.
    /// </summary>
    public async Task<TbApiKey?> GetActiveKeyForSubscriptionAsync(int subscriptionId)
    {
        #region hide [2026-08-24]
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.SubscriptionId == subscriptionId && k.IsActive && !k.IsDeleted);
        #endregion
    }

    /// <summary>
    /// Gets or auto-generates a default active API Key for a subscription workspace.
    /// Returns the active key entity and raw API Key string ready for immediate use.
    /// </summary>
    public async Task<(TbApiKey Entity, string RawKey)> GetOrCreateDefaultApiKeyForSubscriptionAsync(int subscriptionId)
    {
        #region hide [2026-08-24]
        using var db = await _dbFactory.CreateDbContextAsync();
        var existingKey = await db.ApiKeys
            .FirstOrDefaultAsync(k => k.SubscriptionId == subscriptionId && k.IsActive && !k.IsDeleted);

        if (existingKey != null)
        {
            if (existingKey.KeyPrefix == "acamp_default1")
            {
                return (existingKey, "acamp_default1_1234567890abcdef1234567890abcdef");
            }
            return (existingKey, $"{existingKey.KeyPrefix}_********************************");
        }

        // Auto-create default key for subscription
        return await CreateApiKeyAsync(subscriptionId, "Auto-Generated Workspace Integration Key");
        #endregion
    }

    /// <summary>
    /// Validates a raw X-API-Key header value. Returns the associated TbApiKey if valid, or null if invalid/expired/revoked.
    /// Results are cached in IMemoryCache for 60 seconds.
    /// </summary>
    public async Task<TbApiKey?> ValidateApiKeyAsync(string? rawKey)
    {
        #region hide [2026-08-24]
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        rawKey = rawKey.Trim();
        if (!rawKey.StartsWith("acamp_") || rawKey.Length < 40)
        {
            return null;
        }

        var parts = rawKey.Split('_');
        if (parts.Length != 3) return null;

        var prefix = $"{parts[0]}_{parts[1]}";
        var cacheKey = $"apikey_val_{prefix}";

        if (_cache.TryGetValue<TbApiKey>(cacheKey, out var cachedKey) && cachedKey != null)
        {
            if (ComputeSha256(rawKey) == cachedKey.KeyHash)
            {
                if (cachedKey.ExpiresAt.HasValue && cachedKey.ExpiresAt.Value < AppTimeZone.UtcNow) return null;
                _ = ThrottleLastUsedAtAsync(cachedKey.Id);
                return cachedKey;
            }
            return null;
        }

        var inputHash = ComputeSha256(rawKey);
        using var db = await _dbFactory.CreateDbContextAsync();

        var dbKey = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyPrefix == prefix && k.IsActive && !k.IsDeleted);

        if (dbKey == null || dbKey.KeyHash != inputHash)
        {
            return null;
        }

        if (dbKey.ExpiresAt.HasValue && dbKey.ExpiresAt.Value < AppTimeZone.UtcNow)
        {
            return null;
        }

        _cache.Set(cacheKey, dbKey, TimeSpan.FromSeconds(60));
        _ = ThrottleLastUsedAtAsync(dbKey.Id);

        return dbKey;
        #endregion
    }

    /// <summary>
    /// Revokes an API Key and invalidates the local validation cache.
    ///
    /// [2026-09-13][Claude] แก้คำอธิบายให้ตรงความจริง: การเพิกถอนมีผลทันทีเฉพาะ instance นี้
    /// instance อื่นที่ cache ผล validate ไว้ (TTL 60 วินาที) จะยังยอมรับคีย์นี้จนกว่า cache จะหมดอายุ
    /// ถ้าต้องการผลทันทีทุก instance ต้องใช้ cache ส่วนกลางหรือกลไก invalidate ข้าม instance
    /// </summary>
    public async Task RevokeApiKeyAsync(int apiKeyId, int subscriptionId)
    {
        #region hide [2026-08-24]
        using var db = await _dbFactory.CreateDbContextAsync();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == apiKeyId && k.SubscriptionId == subscriptionId);
        if (key != null)
        {
            key.IsActive = false;
            key.Status = "Revoked";
            await db.SaveChangesAsync();

            _cache.Remove($"apikey_val_{key.KeyPrefix}");
        }
        #endregion
    }

    /// <summary>
    /// เพิ่มจำนวน Event ที่ ingest สำเร็จจริงให้กับ API Key
    /// ผู้เรียกคือ endpoint ฝั่ง ingestion เท่านั้น และส่งมาเฉพาะจำนวนรายการที่ถูกรับไว้จริง
    /// </summary>
    public async Task AddIngestedEventsCountAsync(int apiKeyId, int acceptedCount)
    {
        // [2026-09-13][Claude] แยกตัวนับนี้ออกจาก ThrottleLastUsedAtAsync
        //     ของเดิมบวก TotalIngestedEventsCount ทีละ 1 ทุกครั้งที่ "ตรวจคีย์" ไม่ว่าจะเป็น
        //     endpoint อ่านอย่างเดียวอย่าง GET /events/{key} และยังถูก throttle 5 นาทีอีกชั้น
        //     ตัวเลขที่ได้จึงไม่ใช่จำนวน Event ที่ ingest จริงเลย
        if (acceptedCount <= 0) return;

        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            await db.ApiKeys
                .Where(k => k.Id == apiKeyId)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.TotalIngestedEventsCount, k => k.TotalIngestedEventsCount + acceptedCount));
        }
        catch
        {
            // ตัวนับเชิงสถิติ ล้มเหลวแล้วไม่ควรทำให้คำขอหลักพัง
        }
    }

    private async Task ThrottleLastUsedAtAsync(int apiKeyId)
    {
        #region hide [2026-08-24]
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var now = AppTimeZone.UtcNow;
            // [2026-09-13][Claude] อัปเดตเฉพาะ LastUsedAt เท่านั้น ไม่แตะตัวนับจำนวน Event อีกต่อไป
            await db.ApiKeys
                .Where(k => k.Id == apiKeyId && (k.LastUsedAt == null || k.LastUsedAt < now.AddMinutes(-5)))
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now));
        }
        catch
        {
            // Silently ignore background throttle update errors
        }
        #endregion
    }

    private static string ComputeSha256(string raw)
    {
        #region hide [2026-08-24]
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
        #endregion
    }
}
