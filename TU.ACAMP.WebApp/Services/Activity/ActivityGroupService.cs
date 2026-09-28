using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

public class ActivityGroupService : BaseService
{
    #region class field
    private readonly ILogger<ActivityGroupService> _logger;
    #endregion

    #region class constructor
    public ActivityGroupService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<ActivityGroupService> logger) : base(dbFactory, authStateProvider)
    {
        _logger = logger;
    }
    #endregion

    #region class methods
    public async Task<List<TbActivityGroup>> GetAllAsync()
    {
        try
        {
            using var context = await CreateContextAsync();

            var dbGroups = await context.ActivityGroups
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
            _logger.LogError(ex, "Error fetching all active activity groups from database");
            throw;
        }
    }

    public async Task<TbActivityGroup?> GetByIdAsync(int id)
    {
        try
        {
            using var context = await CreateContextAsync();
            return await context.ActivityGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == id && !g.IsDeleted && g.IsActive);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity group from database by ID {Id}", id);
            throw;
        }
    }
    #endregion
}
