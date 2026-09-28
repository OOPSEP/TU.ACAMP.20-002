using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Identity;

public class IdentityAdminService : BaseService
{
    #region class field
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<IdentityAdminService> _logger;
    #endregion

    #region class constructor
    public IdentityAdminService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<IdentityAdminService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-16]
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการบัญชีผู้ใช้งานระบบทั้งหมดจากตาราง aspnetusers
    /// </summary>
    public async Task<List<ApplicationUser>> GetSystemUsersAsync()
    {
        #region hide [2026-08-16]
        try
        {
            using var context = await CreateContextAsync();
            return await context.Users
                .OrderBy(u => u.Email)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching system user accounts.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูลบัญชีผู้ใช้งานตาม Id
    /// </summary>
    public async Task<ApplicationUser?> GetUserByIdAsync(string userId)
    {
        #region hide [2026-08-16]
        if (string.IsNullOrWhiteSpace(userId)) return null;

        try
        {
            using var context = await CreateContextAsync();
            return await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching user account by Id: {UserId}", userId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายชื่อ System Roles ทั้งหมดที่มีอยู่ในตาราง aspnetroles
    /// </summary>
    public async Task<List<string>> GetAvailableSystemRolesAsync()
    {
        #region hide [2026-08-16]
        try
        {
            return await _roleManager.Roles
                .Where(r => r.Name != null)
                .Select(r => r.Name!)
                .OrderBy(name => name)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching available system roles.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายชื่อ System Roles ของผู้ใช้รายบุคคลตาม Id
    /// </summary>
    public async Task<List<string>> GetUserSystemRolesAsync(string userId)
    {
        #region hide [2026-08-16]
        if (string.IsNullOrWhiteSpace(userId)) return new List<string>();

        try
        {
            using var context = await CreateContextAsync();
            return await (from userRole in context.UserRoles
                          join role in context.Roles on userRole.RoleId equals role.Id
                          where userRole.UserId == userId && role.Name != null
                          orderby role.Name
                          select role.Name!)
                         .AsNoTracking()
                         .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching system roles for user: {UserId}", userId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงพจนานุกรมแสดง Roles ทั้งหมดของผู้ใช้แต่ละคน (UserId -> List of RoleName)
    /// </summary>
    public async Task<Dictionary<string, List<string>>> GetAllUserRolesMapAsync()
    {
        #region hide [2026-08-16]
        try
        {
            using var context = await CreateContextAsync();

            var pairs = await (from userRole in context.UserRoles
                               join role in context.Roles on userRole.RoleId equals role.Id
                               where role.Name != null
                               select new { userRole.UserId, RoleName = role.Name! })
                              .AsNoTracking()
                              .ToListAsync();

            return pairs
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.RoleName).Distinct().OrderBy(name => name).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while building the user roles map.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายชื่ออีเมลของ System Administrator ทั้งหมดในระบบ
    /// </summary>
    public async Task<List<string>> GetAdminEmailsAsync()
    {
        #region hide [2026-08-16]
        try
        {
            using var context = await CreateContextAsync();
            var adminRoleId = await context.Roles
                .Where(r => r.Name == SystemRoles.Administrator)
                .Select(r => r.Id)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(adminRoleId)) return new List<string>();

            return await (from userRole in context.UserRoles
                          join user in context.Users on userRole.UserId equals user.Id
                          where userRole.RoleId == adminRoleId && !string.IsNullOrEmpty(user.Email)
                          select user.Email!)
                         .Distinct()
                         .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching administrator emails.");
            return new List<string>();
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงรายการ System Roles ของผู้ใช้งานให้ตรงกับชุดที่เลือกไว้
    /// </summary>
    public async Task UpdateUserSystemRolesAsync(string userId, List<string> selectedRoles)
    {
        #region hide [2026-08-16]
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException("User account was not found.");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var targetRoles = (selectedRoles ?? new List<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rolesToRemove = currentRoles.Except(targetRoles, StringComparer.OrdinalIgnoreCase).ToList();
            var rolesToAdd = targetRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();

            if (rolesToRemove.Any())
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removeResult.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", removeResult.Errors.Select(e => e.Description)));
                }
            }

            if (rolesToAdd.Any())
            {
                var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
                if (!addResult.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", addResult.Errors.Select(e => e.Description)));
                }
            }

            _logger.LogInformation("System roles updated for user {UserId}. Added: {Added}, Removed: {Removed}",
                userId, string.Join(",", rolesToAdd), string.Join(",", rolesToRemove));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating system roles for user: {UserId}", userId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ยืนยันอีเมลบัญชีผู้ใช้งานโดยตรงโดย Administrator (Admin Confirm Email)
    /// </summary>
    public async Task ConfirmEmailByAdminAsync(string email)
    {
        #region hide [2026-08-16]
        if (string.IsNullOrWhiteSpace(email)) return;

        try
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                throw new InvalidOperationException($"User with email '{email}' was not found.");
            }

            if (!user.EmailConfirmed)
            {
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                var result = await _userManager.ConfirmEmailAsync(user, token);
                if (!result.Succeeded)
                {
                    user.EmailConfirmed = true;
                    await _userManager.UpdateAsync(user);
                }
                _logger.LogInformation("Admin confirmed email address for {Email}.", email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm email by admin for {Email}.", email);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ยืนยันอีเมลของผู้ใช้ทุกคนที่รอยืนยัน (Confirm All Pending Emails)
    /// </summary>
    public async Task<int> ConfirmAllPendingEmailsAsync()
    {
        #region hide [2026-08-16]
        try
        {
            var pendingUsers = await _userManager.Users
                .Where(u => !u.EmailConfirmed && u.Email != null)
                .ToListAsync();

            int count = 0;
            foreach (var user in pendingUsers)
            {
                try
                {
                    await ConfirmEmailByAdminAsync(user.Email!);
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to confirm pending email for {Email}", user.Email);
                }
            }

            _logger.LogInformation("Admin confirmed all pending emails ({Count} users).", count);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm all pending emails.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูลวันที่ลงทะเบียนของผู้ใช้งานระบบ (Email -> DateTime)
    /// </summary>
    public async Task<Dictionary<string, DateTime?>> GetUserRegisterDatesMapAsync()
    {
        #region hide [2026-08-16]
        try
        {
            using var context = await CreateContextAsync();
            var subUserDates = await context.SubscriptionUsers
                .AsNoTracking()
                .Where(u => !u.IsDeleted && u.Email != null)
                .GroupBy(u => u.Email!.ToLower())
                .Select(g => new { Email = g.Key, MinDate = g.Min(x => (DateTime?)x.CreatedAt) })
                .ToListAsync();

            var subDates = await context.Subscriptions
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.OwnerEmail != null)
                .GroupBy(s => s.OwnerEmail!.ToLower())
                .Select(g => new { Email = g.Key, MinDate = g.Min(x => (DateTime?)x.CreatedAt) })
                .ToListAsync();

            var result = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in subUserDates)
            {
                result[item.Email] = item.MinDate;
            }

            foreach (var item in subDates)
            {
                if (!result.ContainsKey(item.Email) || (item.MinDate.HasValue && (!result[item.Email].HasValue || item.MinDate.Value < result[item.Email]!.Value)))
                {
                    result[item.Email] = item.MinDate;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch user register dates map.");
            return new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        }
        #endregion
    }
    #endregion
}
