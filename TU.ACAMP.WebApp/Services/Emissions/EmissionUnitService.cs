using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Emissions;

public class EmissionUnitService : BaseService
{
    #region class field
    private readonly ILogger<EmissionUnitService> _logger;
    #endregion

    #region class constructor
    public EmissionUnitService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<EmissionUnitService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    public async Task<List<TbEmissionUnit>> GetAllAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var dbUnits = await context.Units
                .Where(u => !u.IsDeleted && u.IsActive)
                .AsNoTracking()
                .ToListAsync();

            return dbUnits
                .OrderBy(u => u.Title)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching active emission units from database.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionUnit>> GetMasterListAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var dbUnits = await context.Units
                .Where(u => !u.IsDeleted && u.IsActive && u.SourceType == eSourceType.Default)
                .AsNoTracking()
                .ToListAsync();

            return dbUnits
                .OrderBy(u => u.Title)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching master default emission units.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionUnit>> GetBySubscriptionIdAsync(int? subscriptionId = null, bool includeDefault = true)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var query = context.Units.Where(u => !u.IsDeleted && u.IsActive);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                var targetSubId = subscriptionId.Value;
                if (includeDefault)
                {
                    query = query.Where(u => u.SourceType == eSourceType.Default ||
                                            (u.SubscriptionId == targetSubId && u.SourceType == eSourceType.Custom));
                }
                else
                {
                    query = query.Where(u => u.SubscriptionId == targetSubId && u.SourceType == eSourceType.Custom);
                }
            }
            else
            {
                query = query.Where(u => u.SourceType == eSourceType.Default);
            }

            var dbUnits = await query.AsNoTracking().ToListAsync();

            return dbUnits
                .OrderBy(u => u.Title)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission units for subscription context: {SubId}, includeDefault: {IncludeDefault}", subscriptionId, includeDefault);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionUnit?> GetByIdAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            return await context.Units
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted && u.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission unit by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionUnit> CreateAsync(TbEmissionUnit unit)
    {
        #region hide [2026-08-17]
        try
        {
            if (!unit.SubscriptionId.HasValue || unit.SubscriptionId.Value <= 0)
            {
                throw new InvalidOperationException("SubscriptionId must be specified for custom emission unit.");
            }

            // [2026-09-19][Antigravity] ตัดช่องว่างหัวและท้ายชื่อทั้งเดี่ยว หลายช่องว่าง และ Unicode whitespace
            var normalizedTitle = EmissionUnitExcelService.TrimTitle(unit.Title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new InvalidOperationException("Title is required.");
            }

            using var context = await CreateContextAsync();

            // Check title duplication in subscription context + system default
            bool isDbConflict = await context.Units.AnyAsync(u =>
                !u.IsDeleted &&
                u.Title.ToLower() == normalizedTitle.ToLower() &&
                (u.SourceType == eSourceType.Default || u.SubscriptionId == unit.SubscriptionId));

            if (isDbConflict)
            {
                throw new InvalidOperationException($"The emission unit '{normalizedTitle}' already exists in your workspace library.");
            }

            var currentUserId = await GetCurrentUserIdAsync();

            unit.Title = normalizedTitle;
            unit.SourceType = eSourceType.Custom;
            unit.CreatedAt = DateTime.UtcNow;
            unit.CreatedBy = currentUserId;
            unit.UpdatedAt = DateTime.UtcNow;
            unit.UpdatedBy = currentUserId;
            unit.IsActive = true;
            unit.IsDeleted = false;

            context.Units.Add(unit);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Custom emission unit created successfully with ID: {Id} for Subscription: {SubId}", unit.Id, unit.SubscriptionId);
            return unit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating emission unit for Subscription: {SubId}", unit.SubscriptionId);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionUnit> UpdateAsync(TbEmissionUnit unit)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.Units.FirstOrDefaultAsync(u => u.Id == unit.Id && !u.IsDeleted);
            if (existing == null)
            {
                throw new InvalidOperationException("Target emission unit not found or access denied.");
            }

            if (existing.SourceType == eSourceType.Default)
            {
                _logger.LogWarning("Security Warning: Attempted modification of system-wide read-only static master emission unit {Id}", unit.Id);
                throw new InvalidOperationException("Security Exception: Modifying system-wide read-only static master data is strictly prohibited.");
            }

            // [2026-09-19][Antigravity] ตัดช่องว่างหัวและท้ายชื่อทั้งเดี่ยว หลายช่องว่าง และ Unicode whitespace
            var normalizedTitle = EmissionUnitExcelService.TrimTitle(unit.Title);
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new InvalidOperationException("Title is required.");
            }

            // Check duplicate title excluding self
            bool isDuplicate = await context.Units.AnyAsync(u =>
                u.Id != unit.Id &&
                !u.IsDeleted &&
                u.Title.ToLower() == normalizedTitle.ToLower() &&
                (u.SourceType == eSourceType.Default || u.SubscriptionId == existing.SubscriptionId));

            if (isDuplicate)
            {
                throw new InvalidOperationException($"The emission unit '{normalizedTitle}' is already in use by another entity.");
            }

            var currentUserId = await GetCurrentUserIdAsync();

            existing.Title = normalizedTitle;
            existing.Remark = unit.Remark;
            existing.IsActive = unit.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = currentUserId;

            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission unit updated successfully with ID: {Id}", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating emission unit ID: {Id}", unit.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-20][Antigravity] ดึงรายชื่อ Emission Source (ที่ยังไม่ถูกลบ) ที่อ้างอิงใช้งาน Emission Unit นี้
    /// </summary>
    public async Task<List<string>> GetUsingSourceTitlesAsync(int unitId)
    {
        try
        {
            using var context = await CreateContextAsync();

            return await context.EmissionSources
                .Where(s => s.UnitId == unitId && !s.IsDeleted)
                .Select(s => s.Title)
                .Distinct()
                .OrderBy(t => t)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching using emission source titles for emission unit ID: {Id}", unitId);
            throw;
        }
    }

    /// <summary>
    /// [2026-09-24] รวบรวมรายการที่อ้างอิง Unit นี้อยู่: Emission Source (UnitId) และ Topic (TopicChild.ActivityUnitId)
    /// </summary>
    public async Task<List<DeleteReference>> GetDeleteReferencesAsync(int unitId)
    {
        #region hide [2026-09-24]
        try
        {
            using var context = await CreateContextAsync();
            return await QueryDeleteReferencesAsync(context, unitId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching delete references for emission unit ID: {Id}", unitId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-24] คืนข้อความเตือนสำหรับ Notify หาก Unit ยังถูกอ้างอิงอยู่ (null = ลบได้)
    /// </summary>
    public async Task<string?> GetDeleteBlockMessageAsync(int unitId)
    {
        #region hide [2026-09-24]
        var references = await GetDeleteReferencesAsync(unitId);
        return DeleteGuard.BuildBlockMessage("emission unit", references);
        #endregion
    }

    private static async Task<List<DeleteReference>> QueryDeleteReferencesAsync(ApplicationDbContext context, int unitId)
    {
        #region hide [2026-09-24]
        var sourceTitles = await context.EmissionSources
            .Where(s => s.UnitId == unitId && !s.IsDeleted)
            .Select(s => s.Title)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        var topicTitles = await (
            from c in context.TopicChilds
            join t in context.Topics on c.TopicId equals t.Id
            where c.ActivityUnitId == unitId && !c.IsDeleted && !t.IsDeleted
            select t.Title
        )
        .Distinct()
        .OrderBy(t => t)
        .ToListAsync();

        return DeleteGuard.Normalize(
            sourceTitles.Select(t => new DeleteReference("emission source", t))
                .Concat(topicTitles.Select(t => new DeleteReference("topic", t))));
        #endregion
    }

    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var unit = await context.Units.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
            if (unit == null)
            {
                return false;
            }

            if (unit.SourceType == eSourceType.Default)
            {
                _logger.LogWarning("Security Warning: Attempted deletion of system default emission unit ID: {Id}", id);
                return false;
            }

            // [2026-09-24] Delete Protection: ตรวจสอบทั้ง Emission Source (UnitId) และ Topic (TopicChild.ActivityUnitId)
            var references = await QueryDeleteReferencesAsync(context, id);
            var blockMessage = DeleteGuard.BuildBlockMessage("emission unit", references);
            if (blockMessage != null)
            {
                _logger.LogWarning("Deletion prevented: Emission unit ID {Id} is currently in use by {Count} item(s).", id, references.Count);
                throw new InvalidOperationException(blockMessage);
            }

            var currentUserId = await GetCurrentUserIdAsync();

            unit.SoftDelete(currentUserId);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission unit soft-deleted successfully with ID: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting emission unit ID: {Id}", id);
            throw;
        }
        #endregion
    }
    #endregion
}
