using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Emissions;

public class EmissionGroupService : BaseService
{
    #region class field
    private readonly ILogger<EmissionGroupService> _logger;
    #endregion

    #region class constructor
    public EmissionGroupService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<EmissionGroupService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    public async Task<List<TbEmissionGroup>> GetAllAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var dbGroups = await context.EmissionGroups
                .Where(g => !g.IsDeleted && g.IsActive)
                .AsNoTracking()
                .ToListAsync();

            return dbGroups
                .OrderBy(g => g.TgoScopeNo)
                .ThenBy(g => g.Code)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all active emission groups from database.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionGroup>> GetMasterListAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var dbGroups = await context.EmissionGroups
                .Where(g => !g.IsDeleted && g.IsActive && g.SourceType == eSourceType.Default)
                .AsNoTracking()
                .ToListAsync();

            return dbGroups
                .OrderBy(g => g.TgoScopeNo)
                .ThenBy(g => g.Code)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching master default emission groups.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionGroup>> GetBySubscriptionIdAsync(int? subscriptionId = null, bool includeDefault = true)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var query = context.EmissionGroups.Where(g => !g.IsDeleted && g.IsActive);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                var targetSubId = subscriptionId.Value;
                if (includeDefault)
                {
                    query = query.Where(g => g.SourceType == eSourceType.Default ||
                                            (g.SubscriptionId == targetSubId && g.SourceType == eSourceType.Custom));
                }
                else
                {
                    query = query.Where(g => g.SubscriptionId == targetSubId && g.SourceType == eSourceType.Custom);
                }
            }
            else
            {
                query = query.Where(g => g.SourceType == eSourceType.Default);
            }

            var dbGroups = await query.AsNoTracking().ToListAsync();

            return dbGroups
                .OrderBy(g => g.TgoScopeNo)
                .ThenBy(g => g.Code)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission groups for subscription context: {SubId}, includeDefault: {IncludeDefault}", subscriptionId, includeDefault);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionGroup?> GetByIdAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            return await context.EmissionGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && g.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission group by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionGroup> CreateAsync(TbEmissionGroup group)
    {
        #region hide [2026-08-17]
        try
        {
            bool isSystemDefault = group.SourceType == eSourceType.Default || !group.SubscriptionId.HasValue || group.SubscriptionId.Value <= 0;
            if (!isSystemDefault && (!group.SubscriptionId.HasValue || group.SubscriptionId.Value <= 0))
            {
                throw new InvalidOperationException("SubscriptionId must be specified for custom emission group.");
            }

            var normalizedCode = (group.Code ?? string.Empty).Trim().ToUpper();
            using var context = await CreateContextAsync();

            // Check code duplication in subscription context + system default
            bool isDbConflict = await context.EmissionGroups.AnyAsync(g =>
                !g.IsDeleted &&
                g.Code.ToLower() == normalizedCode.ToLower() &&
                (g.SourceType == eSourceType.Default || g.SubscriptionId == group.SubscriptionId));

            if (isDbConflict)
            {
                throw new InvalidOperationException($"The emission group code '{group.Code}' already exists in your workspace library.");
            }

            var currentUserId = await GetCurrentUserIdAsync();

            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้ายของชื่อ Title (รวมถึงหลายช่องว่างและ Unicode whitespace)
            group.Title = EmissionGroupExcelService.TrimTitle(group.Title);
            group.Code = normalizedCode;
            group.IsReadOnly = false;
            group.SourceType = isSystemDefault ? eSourceType.Default : eSourceType.Custom;
            group.SubscriptionId = isSystemDefault ? null : group.SubscriptionId;
            group.CreatedAt = DateTime.UtcNow;
            group.CreatedBy = currentUserId;
            group.UpdatedAt = DateTime.UtcNow;
            group.UpdatedBy = currentUserId;
            group.IsActive = true;
            group.IsDeleted = false;

            context.EmissionGroups.Add(group);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission group created successfully with ID: {Id} (SourceType: {SourceType})", group.Id, group.SourceType);
            return group;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating emission group for Subscription: {SubId}", group.SubscriptionId);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionGroup> UpdateAsync(TbEmissionGroup group)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.EmissionGroups.FirstOrDefaultAsync(g => g.Id == group.Id && !g.IsDeleted);
            if (existing == null)
            {
                throw new InvalidOperationException("Target emission group not found or access denied.");
            }

            if (existing.SourceType == eSourceType.Default || existing.IsReadOnly)
            {
                _logger.LogWarning("Security Warning: Attempted modification of system-wide read-only static master emission group {Id}", group.Id);
                throw new InvalidOperationException("Security Exception: Modifying system-wide read-only static master data is strictly prohibited.");
            }

            var normalizedCode = (group.Code ?? string.Empty).Trim().ToUpper();

            // Check code duplication excluding self
            bool isDuplicate = await context.EmissionGroups.AnyAsync(g =>
                g.Id != group.Id &&
                !g.IsDeleted &&
                g.Code.ToLower() == normalizedCode.ToLower() &&
                (g.SourceType == eSourceType.Default || g.SubscriptionId == existing.SubscriptionId));

            if (isDuplicate)
            {
                throw new InvalidOperationException($"The emission group code '{group.Code}' is already in use by another entity.");
            }

            var currentUserId = await GetCurrentUserIdAsync();

            existing.Code = normalizedCode;
            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้ายของชื่อ Title (รวมถึงหลายช่องว่างและ Unicode whitespace)
            existing.Title = EmissionGroupExcelService.TrimTitle(group.Title);
            existing.TitleTh = group.TitleTh;
            existing.TgoScopeNo = group.TgoScopeNo;
            existing.IsoCategoryNo = group.IsoCategoryNo;
            existing.Remark = group.Remark;
            existing.IsActive = group.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = currentUserId;

            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission group updated successfully with ID: {Id}", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating emission group ID: {Id}", group.Id);
            throw;
        }
        #endregion
    }

    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var group = await context.EmissionGroups.FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted);
            if (group == null)
            {
                return false;
            }

            if (group.SourceType == eSourceType.Default || group.IsReadOnly)
            {
                _logger.LogWarning("Security Warning: Attempted deletion of system default emission group ID: {Id}", id);
                return false;
            }

            // [2026-09-24] Delete Protection: ตรวจสอบทั้ง Emission Source (GroupId) และ Topic (EmissionGroupId)
            var references = await QueryDeleteReferencesAsync(context, id);
            var blockMessage = DeleteGuard.BuildBlockMessage("emission group", references);
            if (blockMessage != null)
            {
                _logger.LogWarning("Deletion prevented: Emission group ID {Id} is currently in use by {Count} item(s).", id, references.Count);
                throw new InvalidOperationException(blockMessage);
            }

            var currentUserId = await GetCurrentUserIdAsync();

            group.SoftDelete(currentUserId);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission group soft-deleted successfully with ID: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting emission group ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-24] รวบรวมรายการที่อ้างอิง Group นี้อยู่: Emission Source (GroupId) และ Topic (EmissionGroupId)
    /// </summary>
    public async Task<List<DeleteReference>> GetDeleteReferencesAsync(int groupId)
    {
        #region hide [2026-09-24]
        try
        {
            using var context = await CreateContextAsync();
            return await QueryDeleteReferencesAsync(context, groupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching delete references for emission group ID: {Id}", groupId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-24] คืนข้อความเตือนสำหรับ Notify หาก Group ยังถูกอ้างอิงอยู่ (null = ลบได้)
    /// </summary>
    public async Task<string?> GetDeleteBlockMessageAsync(int groupId)
    {
        #region hide [2026-09-24]
        var references = await GetDeleteReferencesAsync(groupId);
        return DeleteGuard.BuildBlockMessage("emission group", references);
        #endregion
    }

    private static async Task<List<DeleteReference>> QueryDeleteReferencesAsync(ApplicationDbContext context, int groupId)
    {
        #region hide [2026-09-24]
        var sourceTitles = await context.EmissionSources
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .Select(s => s.Title)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        var topicTitles = await context.Topics
            .Where(t => t.EmissionGroupId == groupId && !t.IsDeleted)
            .Select(t => t.Title)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        return DeleteGuard.Normalize(
            sourceTitles.Select(t => new DeleteReference("emission source", t))
                .Concat(topicTitles.Select(t => new DeleteReference("topic", t))));
        #endregion
    }
    #endregion
}
