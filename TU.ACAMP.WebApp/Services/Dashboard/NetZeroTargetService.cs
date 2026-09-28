using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class NetZeroTargetService : BaseService
{
    #region class constructor
    public NetZeroTargetService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider) : base(dbFactory, authStateProvider)
    {
    }
    #endregion

    #region class methods
    public async Task<TbNetZeroTarget> GetAsync(int subscriptionId, int? corporateId = null)
    {
        #region hide [2026-08-22]
        using var db = await CreateContextAsync();
        var target = await db.NetZeroTargets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.SubscriptionId == subscriptionId && t.CorporateId == corporateId);

        if (target != null) return target;

        // Fallback to subscription-level setting if corporate setting not found
        if (corporateId.HasValue)
        {
            var subTarget = await db.NetZeroTargets
                .AsNoTracking()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.SubscriptionId == subscriptionId && t.CorporateId == null);
            if (subTarget != null) return subTarget;
        }

        // Default fallback values
        return new TbNetZeroTarget
        {
            SubscriptionId = subscriptionId,
            CorporateId = corporateId,
            BaseYear = 2023,
            TargetYear = 2030,
            TargetReductionPercent = 50.0m,
            // [2026-09-25][Antigravity] Default alert settings for Net-Zero threshold
            EnableAlert = true,
            AlertThresholdPercent = 80.0m,
            AlertEmails = null
        };
        #endregion
    }

    public async Task SaveAsync(
        int subscriptionId,
        int? corporateId,
        int baseYear,
        int targetYear,
        decimal targetReductionPercent,
        bool enableAlert = true,
        decimal alertThresholdPercent = 80.0m,
        string? alertEmails = null)
    {
        #region hide [2026-08-22]
        using var db = await CreateContextAsync();
        var entity = await db.NetZeroTargets
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.SubscriptionId == subscriptionId && t.CorporateId == corporateId);

        var currentUser = await GetCurrentUserIdAsync();

        if (entity == null)
        {
            entity = new TbNetZeroTarget
            {
                SubscriptionId = subscriptionId,
                CorporateId = corporateId,
                BaseYear = baseYear,
                TargetYear = targetYear,
                TargetReductionPercent = targetReductionPercent,
                // [2026-09-25][Antigravity] ตั้งค่าการแจ้งเตือนอีเมลเมื่อค่า GHG สูงขึ้นเข้าใกล้เป้าหมาย
                EnableAlert = enableAlert,
                AlertThresholdPercent = alertThresholdPercent,
                AlertEmails = alertEmails,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = currentUser
            };
            db.NetZeroTargets.Add(entity);
        }
        else
        {
            entity.BaseYear = baseYear;
            entity.TargetYear = targetYear;
            entity.TargetReductionPercent = targetReductionPercent;
            // [2026-09-25][Antigravity] อัปเดตการตั้งค่าการแจ้งเตือนอีเมล
            entity.EnableAlert = enableAlert;
            entity.AlertThresholdPercent = alertThresholdPercent;
            entity.AlertEmails = alertEmails;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedBy = currentUser;
            db.NetZeroTargets.Update(entity);
        }

        await db.SaveChangesAsync();
        #endregion
    }
    #endregion
}
