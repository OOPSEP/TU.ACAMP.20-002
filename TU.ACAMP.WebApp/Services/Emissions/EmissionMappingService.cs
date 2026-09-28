using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Emissions;

public class EmissionMappingService : BaseService
{
    #region class field
    private readonly ILogger<EmissionMappingService> _logger;
    #endregion

    #region class constructor
    public EmissionMappingService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<EmissionMappingService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-20]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการ Emission Mapping ทั้งหมด
    /// </summary>
    public async Task<List<TbEmissionMapping>> GetAllAsync(int? subscriptionId = null)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            return await context.EmissionMappings
                .Where(m => !m.IsDeleted && (m.SubscriptionId == null || m.SubscriptionId == subscriptionId))
                .OrderBy(m => m.Title)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all emission mappings.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ค้นหา Mapping ตาม Source ID (Scope1, Scope2, Scope3, หรือ Biogenic)
    /// </summary>
    public async Task<TbEmissionMapping?> FindMappingBySourceIdAsync(int sourceId, int? subscriptionId = null)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            return await context.EmissionMappings
                .Where(m => !m.IsDeleted && (m.SubscriptionId == null || m.SubscriptionId == subscriptionId))
                .FirstOrDefaultAsync(m => m.Scope1ChildId == sourceId ||
                                          m.Scope2ChildId == sourceId ||
                                          m.Scope3ChildId == sourceId ||
                                          m.BiogenicChildId == sourceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding emission mapping by source ID: {SourceId}", sourceId);
            throw;
        }
        #endregion
    }

    public async Task<List<TbEmissionMapping>> GetBySubscriptionIdsAsync(IEnumerable<int> subscriptionIds)
    {
        #region hide [2026-08-21]
        try
        {
            var ids = subscriptionIds?.ToHashSet() ?? new HashSet<int>();

            using var context = await CreateContextAsync();
            var list = await context.EmissionMappings
                .Where(x => !x.IsDeleted && (x.SubscriptionId == null || (x.SubscriptionId.HasValue && ids.Contains(x.SubscriptionId.Value))))
                .OrderBy(x => x.Id)
                .AsNoTracking()
                .ToListAsync();

            if (list.Count == 0)
            {
                return TbEmissionMapping.GetMasterList().ToList();
            }

            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving emission mappings for subscription set.");
            return TbEmissionMapping.GetMasterList().ToList();
        }
        #endregion
    }

    public async Task<TbEmissionMapping> CreateAsync(TbEmissionMapping mapping)
    {
        #region hide [2026-08-21]
        if (mapping == null) throw new ArgumentNullException(nameof(mapping));
        try
        {
            using var context = await CreateContextAsync();
            context.EmissionMappings.Add(mapping);
            await context.SaveChangesAsync();
            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting new emission mapping.");
            throw;
        }
        #endregion
    }

    public async Task<TbEmissionMapping> UpdateAsync(TbEmissionMapping mapping)
    {
        #region hide [2026-08-21]
        if (mapping == null) throw new ArgumentNullException(nameof(mapping));
        try
        {
            using var context = await CreateContextAsync();
            context.Entry(mapping).State = EntityState.Modified;
            await context.SaveChangesAsync();
            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating emission mapping.");
            throw;
        }
        #endregion
    }

    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-21]
        try
        {
            using var context = await CreateContextAsync();
            var item = await context.EmissionMappings.FindAsync(id);
            if (item != null)
            {
                item.IsDeleted = true;
                item.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting emission mapping ID {Id}.", id);
            return false;
        }
        #endregion
    }
    #endregion
}
