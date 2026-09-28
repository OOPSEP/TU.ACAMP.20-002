using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Organization;

public class CorporateUserService : BaseService
{
    #region class field
    private readonly ILogger<CorporateUserService> _logger;
    #endregion

    #region class constructor
    public CorporateUserService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<CorporateUserService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายชื่อผู้ใช้ที่ผูกกับ Corporate ID
    /// </summary>
    public async Task<List<TbCorporateUser>> GetByCorporateIdAsync(int corporateId)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            return await context.CorporateUsers
                .Where(u => u.CorporateId == corporateId && !u.IsDeleted)
                .OrderBy(u => u.Email)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching users for Corporate ID: {CorporateId}", corporateId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายชื่อผู้ใช้งานระดับ Subscription ทั้งหมดที่มีสิทธิ์สืบทอด (Inherited Users) ลงมายัง Corporate
    /// </summary>
    public async Task<List<TbSubscriptionUser>> GetInheritedSubscriptionUsersBySubIdAsync(int subscriptionId)
    {
        #region hide [2026-08-18]
        if (subscriptionId <= 0) return new List<TbSubscriptionUser>();
        try
        {
            using var context = await CreateContextAsync();
            var sub = await context.Subscriptions
                .AsNoTracking()
                .Include(s => s.Users.Where(u => !u.IsDeleted && u.IsActive))
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted);

            if (sub == null) return new List<TbSubscriptionUser>();

            var subUsers = sub.Users
                .OrderBy(u => u.Role)
                .ThenBy(u => u.Email)
                .ToList();

            // Include owner email if not present
            if (!string.IsNullOrWhiteSpace(sub.OwnerEmail) && !subUsers.Any(a => a.Email.Equals(sub.OwnerEmail, StringComparison.OrdinalIgnoreCase)))
            {
                subUsers.Insert(0, new TbSubscriptionUser
                {
                    SubscriptionId = sub.Id,
                    Email = sub.OwnerEmail,
                    Role = eSubscriptionRole.Owner,
                    Remarks = "Subscription Owner",
                    IsActive = true
                });
            }

            return subUsers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching inherited subscription users for Subscription ID: {SubId}", subscriptionId);
            return new List<TbSubscriptionUser>();
        }
        #endregion
    }

    /// <summary>
    /// เพิ่มสิทธิ์ผู้ใช้งานในระดับ Corporate พร้อมตรวจสอบโควตาจำนวนผู้ใช้ต่อกลุ่ม (AllowCorporateUserCount)
    /// </summary>
    public async Task<TbCorporateUser> AddUserAsync(TbCorporateUser user)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            user.Email = user.Email.Trim().ToLowerInvariant();

            var corporate = await context.Corporates
                .Include(c => c.Subscription)
                .FirstOrDefaultAsync(c => c.Id == user.CorporateId && !c.IsDeleted);

            if (corporate == null)
            {
                throw new KeyNotFoundException($"Corporate with ID {user.CorporateId} not found.");
            }

            var exists = await context.CorporateUsers
                .AnyAsync(u => u.CorporateId == user.CorporateId &&
                               u.Email.ToLower() == user.Email &&
                               !u.IsDeleted);

            if (exists)
            {
                throw new InvalidOperationException($"User with email '{user.Email}' is already assigned to this corporate.");
            }

            // Check per-corporate user limit
            var maxAllowed = corporate.Subscription?.AllowCorporateUserCount ?? 9;
            var inheritedUsers = await GetInheritedSubscriptionUsersBySubIdAsync(corporate.SubscriptionId);
            var currentDirectUsers = await context.CorporateUsers
                .Where(u => u.CorporateId == user.CorporateId && !u.IsDeleted)
                .Select(u => u.Email.ToLower())
                .ToListAsync();

            var distinctEmailsInCorp = inheritedUsers.Select(a => a.Email.ToLower())
                .Concat(currentDirectUsers)
                .ToHashSet();

            if (!distinctEmailsInCorp.Contains(user.Email) && distinctEmailsInCorp.Count >= maxAllowed)
            {
                throw new InvalidOperationException($"Corporate user quota reached ({maxAllowed} users max per corporate).");
            }

            context.CorporateUsers.Add(user);
            await context.SaveChangesAsync();
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user to Corporate ID: {CorporateId}", user.CorporateId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงข้อมูลสิทธิ์ผู้ใช้งานใน Corporate
    /// </summary>
    public async Task<TbCorporateUser> UpdateUserAsync(TbCorporateUser user)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var existing = await context.CorporateUsers
                .FirstOrDefaultAsync(u => u.Id == user.Id && !u.IsDeleted);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Corporate user with ID {user.Id} not found.");
            }

            existing.Role = user.Role;
            existing.Remarks = user.Remarks?.Trim() ?? string.Empty;

            await context.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user ID: {Id}", user.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบสิทธิ์ผู้ใช้งานออกจาก Corporate (Soft Delete)
    /// </summary>
    public async Task<bool> RemoveUserAsync(int userId)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var existing = await context.CorporateUsers
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted);

            if (existing == null) return false;

            var currentUserId = await GetCurrentUserIdAsync();
            existing.SoftDelete(currentUserId);

            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing user ID: {Id}", userId);
            throw;
        }
        #endregion
    }
    #endregion
}
