using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Emissions;

public class EmissionSourceService : BaseService
{
    #region class field
    private readonly ILogger<EmissionSourceService> _logger;
    #endregion

    #region class constructor
    public EmissionSourceService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<EmissionSourceService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    public async Task<List<TbEmissionSource>> GetAllAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            return await context.EmissionSources
                .Where(s => !s.IsDeleted && s.IsActive)
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .AsNoTracking()
                .OrderBy(s => s.Title)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all emission sources from database.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionSource>> GetMasterListAsync()
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            return await context.EmissionSources
                .Where(s => !s.IsDeleted && s.IsActive && s.SourceType == eSourceType.Default)
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .AsNoTracking()
                .OrderBy(s => s.Title)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching master default emission sources.");
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionSource>> GetBySubscriptionIdAsync(int? subscriptionId = null, bool includeDefault = true)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var query = context.EmissionSources
                .Where(s => !s.IsDeleted && s.IsActive)
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .AsQueryable();

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                var targetSubId = subscriptionId.Value;
                if (includeDefault)
                {
                    query = query.Where(s => s.SourceType == eSourceType.Default ||
                                            (s.SubscriptionId == targetSubId && s.SourceType == eSourceType.Custom));
                }
                else
                {
                    query = query.Where(s => s.SubscriptionId == targetSubId && s.SourceType == eSourceType.Custom);
                }
            }
            else
            {
                query = query.Where(s => s.SourceType == eSourceType.Default);
            }

            return await query.AsNoTracking()
                .OrderBy(s => s.Title)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission sources for subscription context: {SubId}", subscriptionId);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionSource?> GetByIdAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            return await context.EmissionSources
                .Include(s => s.Group)
                .Include(s => s.Unit)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted && s.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching emission source by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionSource> CreateAsync(TbEmissionSource source)
    {
        #region hide [2026-08-17]
        try
        {
            if (!source.SubscriptionId.HasValue || source.SubscriptionId.Value <= 0)
            {
                throw new InvalidOperationException("SubscriptionId must be specified for custom emission source.");
            }

            if (string.IsNullOrWhiteSpace(source.Title))
            {
                throw new InvalidOperationException("Title is required.");
            }

            // [GHG-07] ห้ามบันทึก gwpCO2 = 0
            if (source.gwpCO2 <= 0)
            {
                throw new InvalidOperationException("gwpCO2 ต้องไม่เป็น 0 หรือติดลบ (GWP of CO2 must be greater than 0)");
            }

            using var context = await CreateContextAsync();

            var currentUserId = await GetCurrentUserIdAsync();

            source.SourceType = eSourceType.Custom;
            source.IsReadOnly = false;
            source.CreatedAt = TU.ACAMP.Domain.Tools.AppTimeZone.UtcNow;
            source.CreatedBy = currentUserId;
            source.UpdatedAt = TU.ACAMP.Domain.Tools.AppTimeZone.UtcNow;
            source.UpdatedBy = currentUserId;
            source.IsActive = true;
            source.IsDeleted = false;

            // Unlink virtual navigations to prevent tracking conflicts
            source.Group = null;
            source.Unit = null;

            context.EmissionSources.Add(source);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Custom emission source created successfully with ID: {Id} for Subscription: {SubId}", source.Id, source.SubscriptionId);
            return source;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating emission source for Subscription: {SubId}", source.SubscriptionId);
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionSource> UpdateAsync(TbEmissionSource source)
    {
        #region hide [2026-08-17]
        try
        {
            // [GHG-07] ห้ามบันทึก gwpCO2 = 0
            if (source.gwpCO2 <= 0)
            {
                throw new InvalidOperationException("gwpCO2 ต้องไม่เป็น 0 หรือติดลบ (GWP of CO2 must be greater than 0)");
            }

            using var context = await CreateContextAsync();

            var existing = await context.EmissionSources.FirstOrDefaultAsync(s => s.Id == source.Id && !s.IsDeleted);
            if (existing == null)
            {
                throw new InvalidOperationException("Target emission source not found or access denied.");
            }

            if (existing.SourceType == eSourceType.Default)
            {
                _logger.LogWarning("Security Warning: Attempted modification of system default emission source {Id}", source.Id);
                throw new InvalidOperationException("Security Exception: Modifying system-wide read-only static master data is strictly prohibited.");
            }

            var currentUserId = await GetCurrentUserIdAsync();

            existing.Title = source.Title.Trim();
            existing.Remark = source.Remark;
            existing.DateApproved = source.DateApproved;
            existing.DateExpire = source.DateExpire;
            existing.CO2 = source.CO2;
            existing.FossilCH4 = source.FossilCH4;
            existing.CH4 = source.CH4;
            existing.N2O = source.N2O;
            existing.SF6 = source.SF6;
            existing.NF3 = source.NF3;
            existing.HFCs = source.HFCs;
            existing.PFCs = source.PFCs;
            existing.gwpCO2 = source.gwpCO2;
            existing.gwpFossilCH4 = source.gwpFossilCH4;
            existing.gwpCH4 = source.gwpCH4;
            existing.gwpN2O = source.gwpN2O;
            existing.gwpSF6 = source.gwpSF6;
            existing.gwpNF3 = source.gwpNF3;
            existing.gwpHFCs = source.gwpHFCs;
            existing.gwpPFCs = source.gwpPFCs;
            existing.GwpVersion = source.GwpVersion;
            existing.SelfCollect = source.SelfCollect;
            existing.Supplier = source.Supplier;
            existing.THLCIDB = source.THLCIDB;
            existing.TGOEF = source.TGOEF;
            existing.ThaiRes = source.ThaiRes;
            existing.IntDB = source.IntDB;
            existing.Other = source.Other;
            existing.Substitute = source.Substitute;
            existing.Ref = source.Ref;
            existing.GroupId = source.GroupId;
            existing.UnitId = source.UnitId;
            existing.IsActive = source.IsActive;
            existing.UpdatedAt = TU.ACAMP.Domain.Tools.AppTimeZone.UtcNow;
            existing.UpdatedBy = currentUserId;

            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission source updated successfully with ID: {Id}", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating emission source ID: {Id}", source.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-20][Antigravity] ตรวจสอบรายชื่อหัวข้อ (Topics) ที่กำลังใช้งาน Emission Source นี้อยู่
    /// </summary>
    public async Task<List<string>> GetUsingTopicTitlesAsync(int sourceId)
    {
        try
        {
            using var context = await CreateContextAsync();

            return await (
                from c in context.TopicChilds
                join t in context.Topics on c.TopicId equals t.Id
                where c.SourceId == sourceId && !c.IsDeleted && !t.IsDeleted
                select t.Title
            )
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching using topic titles for emission source ID: {Id}", sourceId);
            throw;
        }
    }

    /// <summary>
    /// [2026-09-24] รวบรวมรายการที่อ้างอิง Source นี้อยู่: Topic (TopicChild.SourceId) และ Emission Mapping (Scope1/2/3/Biogenic ChildId)
    /// </summary>
    public async Task<List<DeleteReference>> GetDeleteReferencesAsync(int sourceId)
    {
        #region hide [2026-09-24]
        try
        {
            using var context = await CreateContextAsync();
            return await QueryDeleteReferencesAsync(context, sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching delete references for emission source ID: {Id}", sourceId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-24] คืนข้อความเตือนสำหรับ Notify หาก Source ยังถูกอ้างอิงอยู่ (null = ลบได้)
    /// </summary>
    public async Task<string?> GetDeleteBlockMessageAsync(int sourceId)
    {
        #region hide [2026-09-24]
        var references = await GetDeleteReferencesAsync(sourceId);
        return DeleteGuard.BuildBlockMessage("emission source", references);
        #endregion
    }

    private static async Task<List<DeleteReference>> QueryDeleteReferencesAsync(ApplicationDbContext context, int sourceId)
    {
        #region hide [2026-09-24]
        var topicTitles = await (
            from c in context.TopicChilds
            join t in context.Topics on c.TopicId equals t.Id
            where c.SourceId == sourceId && !c.IsDeleted && !t.IsDeleted
            select t.Title
        )
        .Distinct()
        .OrderBy(t => t)
        .ToListAsync();

        var mappingTitles = await context.EmissionMappings
            .Where(m => !m.IsDeleted &&
                        (m.Scope1ChildId == sourceId ||
                         m.Scope2ChildId == sourceId ||
                         m.Scope3ChildId == sourceId ||
                         m.BiogenicChildId == sourceId))
            .Select(m => m.Title)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();

        return DeleteGuard.Normalize(
            topicTitles.Select(t => new DeleteReference("topic", t))
                .Concat(mappingTitles.Select(t => new DeleteReference("emission mapping", t))));
        #endregion
    }

    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var source = await context.EmissionSources.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
            if (source == null)
            {
                return false;
            }

            if (source.SourceType == eSourceType.Default)
            {
                _logger.LogWarning("Security Warning: Attempted deletion of system default emission source ID: {Id}", id);
                return false;
            }

            // [2026-09-24] Delete Protection: ตรวจสอบทั้ง Topic (TopicChild.SourceId) และ Emission Mapping (Scope1/2/3/Biogenic ChildId)
            var references = await QueryDeleteReferencesAsync(context, id);
            var blockMessage = DeleteGuard.BuildBlockMessage("emission source", references);
            if (blockMessage != null)
            {
                _logger.LogWarning("Deletion prevented: Emission source ID {Id} is currently in use by {Count} item(s).", id, references.Count);
                throw new InvalidOperationException(blockMessage);
            }

            var currentUserId = await GetCurrentUserIdAsync();

            source.SoftDelete(currentUserId);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Emission source soft-deleted successfully with ID: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting emission source ID: {Id}", id);
            throw;
        }
        #endregion
    }
    #endregion
}
