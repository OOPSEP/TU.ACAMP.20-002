using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Identity;

public class SubscriptionService : BaseService
{
    #region class field  
    private readonly ILogger<SubscriptionService> _logger;
    private readonly EmailSender _emailSender;
    #endregion

    #region class constructor
    public SubscriptionService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        EmailSender emailSender,
        ILogger<SubscriptionService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-15]
        _emailSender = emailSender;
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods 
    /// <summary>
    /// ดึงรายการ Subscription ทั้งหมดในระบบ
    /// </summary>
    public async Task<List<TbSubscription>> GetAllAsync(
        bool includeUsers = false,
        bool includeCorporates = false,
        bool includeInactive = false)
    {
        #region hide [2026-08-15]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Subscriptions.Where(s => !s.IsDeleted);

            if (includeUsers)
            {
                query = query.Include(s => s.Users.Where(u => !u.IsDeleted));
            }

            if (includeCorporates)
            {
                query = query.Include(s => s.Corporates.Where(c => !c.IsDeleted));
            }

            if (includeUsers && includeCorporates)
            {
                query = query.AsSplitQuery();
            }

            if (!includeInactive)
            {
                query = query.Where(s => s.IsActive);
            }

            return await query.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching all subscriptions.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการสัญญา (Subscription) ที่ผู้ใช้งานมีสิทธิ์มองเห็น (Admin เห็นทั้งหมด / General User เห็นเฉพาะที่เป็นสมาชิกใน TbSubscriptionUser หรือเป็น OwnerEmail)
    /// </summary>
    public async Task<List<TbSubscription>> GetSubscriptionsForUserAsync(
        string email,
        bool isAdmin = false,
        bool isSubscriptionManager = false,
        bool includeUsers = false,
        bool includeCorporates = false,
        bool includeInactive = false)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrWhiteSpace(email) && !isAdmin && !isSubscriptionManager) return new List<TbSubscription>();

        try
        {
            using var context = await CreateContextAsync();

            // 1. ถ้าเป็น System Administrator หรือ Subscription Manager ให้ดึงทั้งหมด
            if (isAdmin || isSubscriptionManager)
            {
                return await GetAllAsync(includeUsers, includeCorporates, includeInactive);
            }

            var lowerEmail = email.Trim().ToLowerInvariant();

            // 2. ถ้าเป็น User ทั่วไป ค้นหาเฉพาะ SubscriptionId ที่อีเมลนี้ถูกกำหนดสิทธิ์ใน TbSubscriptionUser หรือเป็น OwnerEmail
            var subIds = await context.SubscriptionUsers
                .AsNoTracking()
                .Where(u => u.Email != null && u.Email.ToLower() == lowerEmail && !u.IsDeleted && (includeInactive || u.IsActive))
                .Select(u => u.SubscriptionId)
                .Distinct()
                .ToListAsync();

            var query = context.Subscriptions
                .Where(s => !s.IsDeleted && (subIds.Contains(s.Id) || (s.OwnerEmail != null && s.OwnerEmail.ToLower() == lowerEmail)));

            if (includeUsers)
            {
                query = query.Include(s => s.Users.Where(u => !u.IsDeleted));
            }

            if (includeCorporates)
            {
                query = query.Include(s => s.Corporates.Where(c => !c.IsDeleted));
            }

            if (includeUsers && includeCorporates)
            {
                query = query.AsSplitQuery();
            }

            if (!includeInactive)
            {
                query = query.Where(s => s.IsActive && s.ExpiryDate > DateTime.UtcNow);
            }

            return await query.AsNoTracking().ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching subscriptions for user: {Email}", email);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Subscription ID ที่ผู้ใช้มีสิทธิ์แก้ไขได้ (Admin แก้ไขได้ทั้งหมด / Normal user ต้องมี Role Owner, Admin, Editor หรือเป็น OwnerEmail)
    /// </summary>
    public async Task<HashSet<int>> GetEditableSubscriptionIdsAsync(string email, bool isAdmin = false, bool isSubscriptionManager = false)
    {
        #region hide [2026-08-15]
        if (string.IsNullOrWhiteSpace(email) && !isAdmin && !isSubscriptionManager)
        {
            return new HashSet<int>();
        }

        try
        {
            using var context = await CreateContextAsync();
            if (isAdmin || isSubscriptionManager)
            {
                var allIds = await context.Subscriptions
                    .Where(s => !s.IsDeleted)
                    .Select(s => s.Id)
                    .ToListAsync();
                return new HashSet<int>(allIds);
            }

            var lowerEmail = email.Trim().ToLowerInvariant();

            var subUsers = await context.SubscriptionUsers
                .AsNoTracking()
                .Where(u => u.Email != null && u.Email.ToLower() == lowerEmail && !u.IsDeleted && u.IsActive)
                .Select(u => new { u.SubscriptionId, u.Role })
                .ToListAsync();

            var editableSubIds = subUsers
                .Where(u => u.Role.CanEdit())
                .Select(u => u.SubscriptionId)
                .ToHashSet();

            var ownedSubIds = await context.Subscriptions
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.OwnerEmail != null && s.OwnerEmail.ToLower() == lowerEmail)
                .Select(s => s.Id)
                .ToListAsync();

            foreach (var id in ownedSubIds)
            {
                editableSubIds.Add(id);
            }

            return editableSubIds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching editable subscription IDs for user: {Email}", email);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูล Subscription ตาม ID
    /// </summary>
    public async Task<TbSubscription?> GetByIdAsync(
        int id,
        bool includeUsers = false,
        bool includeCorporates = false)
    {
        #region hide [2026-08-15]
        if (id <= 0) return null;

        try
        {
            using var context = await CreateContextAsync();
            var query = context.Subscriptions.Where(s => s.Id == id && !s.IsDeleted);

            if (includeUsers)
            {
                query = query.Include(s => s.Users.Where(u => !u.IsDeleted));
            }

            if (includeCorporates)
            {
                query = query.Include(s => s.Corporates.Where(c => !c.IsDeleted));
            }

            if (includeUsers && includeCorporates)
            {
                query = query.AsSplitQuery();
            }

            return await query.AsNoTracking().FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching subscription by Id. Id: {SubscriptionId}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// [2026-09-23][Claude] ค้นหา Subscription (ที่ยังไม่ถูกลบ) จาก tag ใน Remarks เช่น "[ExternalRef:V16:{guid}]"
    /// ใช้โดย System API เพื่อกันการสร้าง Subscription ซ้ำจากระบบต้นทางเดียวกัน
    /// </summary>
    public async Task<TbSubscription?> FindByRemarksTagAsync(string tag)
    {
        #region hide [2026-09-23]
        if (string.IsNullOrWhiteSpace(tag)) return null;

        using var context = await CreateContextAsync();
        return await context.Subscriptions
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.Remarks.Contains(tag))
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync();
        #endregion
    }

    /// <summary>
    /// สร้าง Subscription ใหม่ตามแพ็กเกจที่เลือก
    /// </summary>
    public async Task<TbSubscription> CreateAsync(TbSubscription subscription, bool sendEmail = true)
    {
        #region hide [2026-08-16]
        if (subscription == null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            subscription.ApplyPlanQuotas(subscription.SubscriptionPlan);

            await context.Subscriptions.AddAsync(subscription);
            await context.SaveChangesAsync(currentUserId);

            _logger.LogInformation("Subscription created successfully with ID: {Id}, Plan: {Plan}", subscription.Id, subscription.SubscriptionPlan);

            // Send email notification for subscription creation if enabled
            if (sendEmail)
            {
                try
                {
                    await _emailSender.SendEmailCreateSubscribeAsync(subscription);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send subscription creation email for Subscription ID {Id}", subscription.Id);
                }
            }

            return subscription;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while creating subscription.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// อัปเดตข้อมูล Subscription
    /// </summary>
    public async Task<TbSubscription> UpdateAsync(TbSubscription subscription)
    {
        #region hide [2026-08-15]
        if (subscription == null || subscription.Id <= 0)
        {
            throw new ArgumentException("Invalid subscription data provided.");
        }

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var existing = await context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscription.Id && !s.IsDeleted);

            if (existing == null)
            {
                throw new InvalidOperationException("Subscription not found or has been deleted.");
            }

            existing.Title = subscription.Title;
            existing.SubscriptionPlan = subscription.SubscriptionPlan;
            existing.OwnerType = subscription.OwnerType;
            existing.OwnerName = subscription.OwnerName;
            existing.OwnerEmail = subscription.OwnerEmail;
            existing.TaxId = subscription.TaxId;
            existing.OwnerPhone = subscription.OwnerPhone;
            existing.BillingAddress = subscription.BillingAddress;
            existing.ContactPerson = subscription.ContactPerson;
            existing.Remarks = subscription.Remarks;
            existing.IsActive = subscription.IsActive;
            existing.StartDate = subscription.StartDate;
            existing.ExpiryDate = subscription.ExpiryDate;
            existing.AllowCorporateCount = subscription.AllowCorporateCount;
            existing.AllowCompanyCount = subscription.AllowCompanyCount;
            existing.AllowFacilityCount = subscription.AllowFacilityCount;
            existing.AllowUserCount = subscription.AllowUserCount;
            existing.AllowDataUploadCount = subscription.AllowDataUploadCount;
            existing.AllowDataDownloadCount = subscription.AllowDataDownloadCount;
            existing.AllowDataStoreCount = subscription.AllowDataStoreCount;
            existing.AllowEmissionCount = subscription.AllowEmissionCount;
            existing.AllowTopicCount = subscription.AllowTopicCount;
            existing.AllowEventCount = subscription.AllowEventCount;

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Subscription updated successfully with ID: {Id}", subscription.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while updating subscription. Id: {SubscriptionId}", subscription.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายชื่อผู้ใช้ที่ผูกกับ Subscription ตาม ID
    /// </summary>
    public async Task<List<TbSubscriptionUser>> GetSubscriptionUsersAsync(int subscriptionId)
    {
        #region hide [2026-08-15]
        if (subscriptionId <= 0) return new List<TbSubscriptionUser>();

        try
        {
            using var context = await CreateContextAsync();
            return await context.SubscriptionUsers
                .Where(u => u.SubscriptionId == subscriptionId && !u.IsDeleted)
                .OrderBy(u => u.Role)
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching users for subscription. Id: {SubscriptionId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// บันทึกหรือสร้าง Subscription พร้อมรายการ Users
    /// </summary>
    public async Task<TbSubscription> SaveSubscriptionWithUsersAsync(TbSubscription subscription, List<TbSubscriptionUser> users, bool sendEmail = true)
    {
        #region hide [2026-08-16]
        if (subscription == null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var isNew = subscription.Id <= 0;
            TbSubscription targetSub;
            if (isNew)
            {
                // Ensure quotas match plan
                subscription.ApplyPlanQuotas(subscription.SubscriptionPlan);

                // Create new
                await context.Subscriptions.AddAsync(subscription);
                await context.SaveChangesAsync(currentUserId);
                targetSub = subscription;
            }
            else
            {
                // Update existing
                var existing = await context.Subscriptions
                    .FirstOrDefaultAsync(s => s.Id == subscription.Id && !s.IsDeleted);

                if (existing == null)
                {
                    throw new InvalidOperationException("Subscription not found or has been deleted.");
                }

                existing.Title = subscription.Title;
                existing.SubscriptionPlan = subscription.SubscriptionPlan;
                existing.OwnerType = subscription.OwnerType;
                existing.OwnerName = subscription.OwnerName;
                existing.OwnerEmail = subscription.OwnerEmail;
                existing.TaxId = subscription.TaxId;
                existing.OwnerPhone = subscription.OwnerPhone;
                existing.BillingAddress = subscription.BillingAddress;
                existing.ContactPerson = subscription.ContactPerson;
                existing.Remarks = subscription.Remarks;
                existing.IsActive = subscription.IsActive;
                existing.StartDate = subscription.StartDate;
                existing.ExpiryDate = subscription.ExpiryDate;
                existing.AllowCorporateCount = subscription.AllowCorporateCount;
                existing.AllowCompanyCount = subscription.AllowCompanyCount;
                existing.AllowFacilityCount = subscription.AllowFacilityCount;
                existing.AllowUserCount = subscription.AllowUserCount;
                existing.AllowDataUploadCount = subscription.AllowDataUploadCount;
                existing.AllowDataDownloadCount = subscription.AllowDataDownloadCount;
                existing.AllowDataStoreCount = subscription.AllowDataStoreCount;
                existing.AllowEmissionCount = subscription.AllowEmissionCount;
                existing.AllowTopicCount = subscription.AllowTopicCount;
                existing.AllowEventCount = subscription.AllowEventCount;

                await context.SaveChangesAsync(currentUserId);
                targetSub = existing;
            }

            // Sync users
            var existingUsers = await context.SubscriptionUsers
                .Where(u => u.SubscriptionId == targetSub.Id && !u.IsDeleted)
                .ToListAsync();

            var incomingEmails = users.Select(u => u.Email.Trim().ToLowerInvariant()).ToHashSet();

            // Soft-delete removed users
            foreach (var eu in existingUsers)
            {
                if (!incomingEmails.Contains(eu.Email.Trim().ToLowerInvariant()))
                {
                    eu.SoftDelete(currentUserId);
                }
            }

            // Add or update users
            foreach (var u in users)
            {
                var normEmail = u.Email.Trim();
                var eu = existingUsers.FirstOrDefault(x => x.Email.Trim().Equals(normEmail, StringComparison.OrdinalIgnoreCase));
                if (eu != null)
                {
                    eu.Role = u.Role;
                    eu.Remarks = u.Remarks ?? string.Empty;
                    eu.IsActive = u.IsActive;
                }
                else
                {
                    var newUser = new TbSubscriptionUser
                    {
                        SubscriptionId = targetSub.Id,
                        Email = normEmail,
                        Role = u.Role,
                        Remarks = u.Remarks ?? string.Empty,
                        IsActive = true
                    };
                    await context.SubscriptionUsers.AddAsync(newUser);
                }
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Subscription and users saved successfully with ID: {Id}", targetSub.Id);

            if (isNew && sendEmail)
            {
                try
                {
                    // [2026-09-28][Gemini] 1. ส่งอีเมลแจ้งเตือนการสร้าง Subscription ถึง Owner พร้อม CC ไปยัง patipath.eia@nectec.or.th
                    await _emailSender.SendEmailCreateSubscribeAsync(targetSub, "patipath.eia@nectec.or.th");

                    // [2026-09-28][Gemini] 2. ส่งอีเมลแจ้งเตือนถึง Authorized Workspace Members ทุกท่าน (เช่น Editor, Admin)
                    var otherMembers = users
                        .Where(u => !string.IsNullOrWhiteSpace(u.Email) &&
                                    !u.Email.Trim().Equals(targetSub.OwnerEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Select(u => u.Email.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase);

                    foreach (var memberEmail in otherMembers)
                    {
                        try
                        {
                            var memberUser = users.FirstOrDefault(u => u.Email.Trim().Equals(memberEmail, StringComparison.OrdinalIgnoreCase));
                            var memberRole = memberUser?.Role.ToString() ?? "Member";
                            var subject = $"[ACAMP] Workspace Invitation - You have been added to '{targetSub.Title}' as {memberRole}";
                            var body = $@"
                                <h3>Welcome to ACAMP Workspace</h3>
                                <p>Dear {memberEmail},</p>
                                <p>You have been assigned as <strong>{memberRole}</strong> for the subscription workspace <strong>'{targetSub.Title}'</strong> in the ACAMP system.</p>
                                <table style='border-collapse: collapse; width: 100%; margin: 15px 0;'>
                                    <tr><td style='padding: 6px; font-weight: bold;'>Subscription:</td><td style='padding: 6px;'>{targetSub.Title}</td></tr>
                                    <tr><td style='padding: 6px; font-weight: bold;'>Your Role:</td><td style='padding: 6px;'>{memberRole}</td></tr>
                                    <tr><td style='padding: 6px; font-weight: bold;'>Plan Tier:</td><td style='padding: 6px;'>{targetSub.SubscriptionPlan}</td></tr>
                                    <tr><td style='padding: 6px; font-weight: bold;'>Owner Email:</td><td style='padding: 6px;'>{targetSub.OwnerEmail}</td></tr>
                                </table>
                                <p>You can sign in to the platform to collaborate and manage emission data.</p>
                                <hr>
                                <p><i>Automatic Carbon Account Management Platform (ACAMP)</i></p>";

                            await _emailSender.SendEmailAsync_003(memberEmail, subject, body);
                            _logger.LogInformation("Sent workspace invitation email to member {Email} for Subscription {Id}", memberEmail, targetSub.Id);
                        }
                        catch (Exception memberEx)
                        {
                            _logger.LogWarning(memberEx, "Failed to send member invitation email to {Email}", memberEmail);
                        }
                    }
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send subscription creation email for Subscription ID {Id}", targetSub.Id);
                }
            }

            return targetSub;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while saving subscription with users.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้าง Subscription อัตโนมัติสำหรับผู้ใช้งานที่ยังไม่มี Subscription (Auto-create Pro Subscription for 3 months)
    /// </summary>
    public async Task<TbSubscription> ActivateUserSubscriptionAsync(string email, string? userName = null, bool sendEmail = true)
    {
        #region hide [2026-08-16]
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        var cleanEmail = email.Trim();
        var cleanUserName = !string.IsNullOrWhiteSpace(userName) ? userName.Trim() : cleanEmail;
        var todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var subscription = new TbSubscription
        {
            OwnerEmail = cleanEmail,
            OwnerName = cleanUserName,
            ContactPerson = cleanEmail,
            Title = $"AUTO_CREATE_{cleanEmail}_{todayStr}",
            StartDate = DateTime.UtcNow,
            ExpiryDate = DateTime.UtcNow.AddMonths(3),
            IsActive = true,
            SubscriptionPlan = eSubscriptionPlan.Pro,
            TaxId = "0000000000000",
            BillingAddress = "Auto-generated registration",
            Remarks = "Auto-created subscription via Activate Subscriptions",
            OwnerType = eSubscriptionOwnerType.Juristic
        };

        subscription.ApplyPlanQuotas(eSubscriptionPlan.Pro);

        var ownerUser = new TbSubscriptionUser
        {
            Email = cleanEmail,
            Role = eSubscriptionRole.Owner,
            IsActive = true,
            Remarks = "Auto-assigned Owner"
        };

        return await SaveSubscriptionWithUsersAsync(subscription, new List<TbSubscriptionUser> { ownerUser }, sendEmail);
        #endregion
    }

    /// <summary>
    /// ลบ Subscription (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-15]
        if (id <= 0) return false;

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var subscription = await context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

            if (subscription == null) return false;

            subscription.SoftDelete(currentUserId);
            subscription.IsActive = false;

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Subscription soft-deleted successfully with ID: {Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while soft-deleting subscription. Id: {SubscriptionId}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สรุปการใช้งานโควตาทรัพยากรของ Subscription (Current Usage vs Allow Limits)
    /// </summary>
    public async Task<SubscriptionQuotaSummary?> GetQuotaUsageSummaryAsync(int subscriptionId)
    {
        #region hide [2026-08-15]
        if (subscriptionId <= 0) return null;

        try
        {
            using var context = await CreateContextAsync();
            var sub = await context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted);

            if (sub == null) return null;

            var corporateCount = await context.Corporates.CountAsync(c => c.SubscriptionId == subscriptionId && !c.IsDeleted);
            var companyCount = await context.Companies.CountAsync(c => c.Corporate != null && c.Corporate.SubscriptionId == subscriptionId && !c.IsDeleted);
            var facilityCount = await context.Facilities.CountAsync(f => f.Company != null && f.Company.Corporate != null && f.Company.Corporate.SubscriptionId == subscriptionId && !f.IsDeleted);
            var userCount = await context.SubscriptionUsers.CountAsync(u => u.SubscriptionId == subscriptionId && !u.IsDeleted);
            var topicCount = await context.Topics.CountAsync(t => t.SubscriptionId == subscriptionId && !t.IsDeleted);
            var eventCount = await context.Events.CountAsync(e => e.SubscriptionId == subscriptionId && !e.IsDeleted);

            return new SubscriptionQuotaSummary
            {
                SubscriptionId = subscriptionId,
                Plan = sub.SubscriptionPlan,
                CurrentCorporates = corporateCount,
                AllowCorporates = sub.AllowCorporateCount,
                CurrentCompanies = companyCount,
                AllowCompanies = sub.AllowCompanyCount,
                CurrentFacilities = facilityCount,
                AllowFacilities = sub.AllowFacilityCount,
                CurrentUserCount = userCount,
                AllowUserCount = sub.AllowUserCount,
                CurrentTopics = topicCount,
                AllowTopics = sub.AllowTopicCount,
                CurrentEvents = eventCount,
                AllowEvents = sub.AllowEventCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while fetching quota usage summary for SubscriptionId: {SubscriptionId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ขยายระยะเวลาหรือต่ออายุ Subscription
    /// </summary>
    public async Task<bool> ExtendSubscriptionAsync(int subscriptionId, int addMonths = 3, DateTime? specificExpiryDate = null)
    {
        #region hide [2026-08-20]
        if (subscriptionId <= 0) return false;

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var subscription = await context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted);

            if (subscription == null) return false;

            if (specificExpiryDate.HasValue)
            {
                subscription.ExpiryDate = specificExpiryDate.Value;
            }
            else
            {
                var baseDate = subscription.ExpiryDate > DateTime.UtcNow ? subscription.ExpiryDate : DateTime.UtcNow;
                subscription.ExpiryDate = baseDate.AddMonths(addMonths);
            }

            subscription.IsActive = true;
            subscription.Status = "Active";

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Subscription {Id} extended to {ExpiryDate}", subscription.Id, subscription.ExpiryDate);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while extending subscription. Id: {SubscriptionId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ส่งอีเมลแจ้งเตือนวันหมดอายุไปยัง Owner/Contact Person ของ Subscription
    /// </summary>
    public async Task<bool> SendExpirationReminderAsync(int subscriptionId)
    {
        #region hide [2026-08-20]
        if (subscriptionId <= 0) return false;

        try
        {
            using var context = await CreateContextAsync();
            var subscription = await context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted);

            if (subscription == null) return false;

            var recipientEmail = !string.IsNullOrWhiteSpace(subscription.OwnerEmail) ? subscription.OwnerEmail : subscription.ContactPerson;
            if (string.IsNullOrWhiteSpace(recipientEmail)) return false;

            var recipientName = !string.IsNullOrWhiteSpace(subscription.OwnerName) ? subscription.OwnerName : subscription.ContactPerson;
            var remainingDays = (int)Math.Ceiling(subscription.GetRemainingDays());

            await _emailSender.SendSubscriptionExpirationReminderAsync(
                recipientEmail: recipientEmail,
                recipientName: recipientName,
                subscriptionName: subscription.Title,
                planName: subscription.SubscriptionPlan.ToString(),
                expiryDate: subscription.ExpiryDate,
                daysRemaining: remainingDays);

            _logger.LogInformation("Sent expiration reminder for Subscription {Id} to {Email}", subscription.Id, recipientEmail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while sending expiration reminder for SubscriptionId: {SubscriptionId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สลับสถานะเปิด/ปิดการใช้งาน (Toggle Active/Inactive)
    /// </summary>
    public async Task<bool> ToggleActiveStatusAsync(int subscriptionId)
    {
        #region hide [2026-08-20]
        if (subscriptionId <= 0) return false;

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var subscription = await context.Subscriptions
                .FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted);

            if (subscription == null) return false;

            subscription.IsActive = !subscription.IsActive;
            subscription.Status = subscription.IsActive ? "Active" : "Inactive";

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Toggled active status for Subscription {Id} to {Status}", subscription.Id, subscription.IsActive);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while toggling status for SubscriptionId: {SubscriptionId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// บันทึกประวัติการกระทำเกี่ยวกับ Subscription ลงตาราง tb_subscription_audit_log
    /// </summary>
    public async Task<TbSubscriptionAuditLog?> LogAuditAsync(
        int subscriptionId,
        string subscriptionTitle,
        string actionType,
        string actorEmail,
        string actorRole,
        string details,
        string? ipAddress = null)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();
            var log = new TbSubscriptionAuditLog
            {
                SubscriptionId = subscriptionId,
                SubscriptionTitle = subscriptionTitle,
                ActionType = actionType,
                ActorEmail = actorEmail,
                ActorRole = actorRole,
                ActionDate = DateTime.UtcNow,
                Details = details,
                IpAddress = ipAddress,
                CreatedBy = !string.IsNullOrWhiteSpace(actorEmail) ? actorEmail : currentUserId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            await context.SubscriptionAuditLogs.AddAsync(log);
            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Logged audit {ActionType} for Subscription {Id} by {Email} ({Role})",
                actionType, subscriptionId, actorEmail, actorRole);
            return log;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for Subscription {Id}", subscriptionId);
            return null;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการประวัติการแก้ไข/สร้าง Subscription ล่าสุด
    /// </summary>
    public async Task<List<TbSubscriptionAuditLog>> GetSubscriptionAuditLogsAsync(int? subscriptionId = null, int limit = 100)
    {
        #region hide [2026-08-20]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.SubscriptionAuditLogs
                .AsNoTracking()
                .Where(l => !l.IsDeleted);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(l => l.SubscriptionId == subscriptionId.Value);
            }

            return await query
                .OrderByDescending(l => l.ActionDate)
                .Take(limit)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve subscription audit logs.");
            return new List<TbSubscriptionAuditLog>();
        }
        #endregion
    }

    /// <summary>
    /// ส่ง Email แจ้งเตือนไปยัง System Administrators เมื่อมี Subscription ถูกสร้างหรือปรับปรุงโดยเจ้าหน้าที่
    /// </summary>
    public async Task NotifyAdminsOnSubscriptionChangeAsync(
        TbSubscription subscription,
        string actionType,
        string actorEmail,
        string actorRole,
        List<string> adminEmails,
        string details = "")
    {
        #region hide [2026-08-20]
        if (adminEmails == null || !adminEmails.Any()) return;

        try
        {
            var subject = $"[ACAMP Security Alert] Subscription '{subscription.Title}' {actionType} by {actorEmail}";
            var actionColor = actionType.Equals("Created", StringComparison.OrdinalIgnoreCase) ? "#28a745" : "#17a2b8";

            var body = $@"
<div style='font-family: Arial, sans-serif; max-width: 650px; margin: 0 auto; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;'>
    <div style='background-color: #1e293b; padding: 20px; color: #ffffff;'>
        <h2 style='margin: 0; font-size: 20px;'>ACAMP Security & Audit Alert</h2>
        <p style='margin: 5px 0 0 0; color: #94a3b8; font-size: 13px;'>Subscription Management Activity Notification</p>
    </div>
    <div style='padding: 24px; background-color: #ffffff;'>
        <div style='margin-bottom: 20px;'>
            <span style='background-color: {actionColor}; color: #ffffff; padding: 4px 10px; border-radius: 12px; font-size: 12px; font-weight: bold;'>
                {actionType.ToUpperInvariant()}
            </span>
            <span style='margin-left: 10px; color: #64748b; font-size: 13px;'>Time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</span>
        </div>
        <p style='color: #334155; font-size: 14px; line-height: 1.5;'>
            A subscription has been <strong>{actionType.ToLowerInvariant()}</strong> in the ACAMP platform by an authorized operator.
        </p>
        <table style='width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 13px;'>
            <tr style='background-color: #f8fafc;'>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold; width: 35%;'>Subscription Title</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{subscription.Title}</td>
            </tr>
            <tr>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Plan</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{subscription.SubscriptionPlan}</td>
            </tr>
            <tr style='background-color: #f8fafc;'>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Tax ID / Identification</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{subscription.TaxId}</td>
            </tr>
            <tr>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Owner Email</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{subscription.OwnerEmail}</td>
            </tr>
            <tr style='background-color: #f8fafc;'>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Validity Period</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{subscription.StartDate:dd/MM/yyyy} - {subscription.ExpiryDate:dd/MM/yyyy}</td>
            </tr>
            <tr>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Performed By</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{actorEmail} (<strong>{actorRole}</strong>)</td>
            </tr>
            {(string.IsNullOrWhiteSpace(details) ? "" : $@"
            <tr style='background-color: #f8fafc;'>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0; font-weight: bold;'>Additional Details</td>
                <td style='padding: 8px 12px; border: 1px solid #e2e8f0;'>{details}</td>
            </tr>")}
        </table>
        <p style='color: #64748b; font-size: 12px; margin-top: 20px;'>
            This is an automated governance notification for System Administrators. You can inspect this activity in the Subscription Audit Trail dashboard.
        </p>
    </div>
</div>";

            foreach (var email in adminEmails.Distinct())
            {
                try
                {
                    await _emailSender.SendEmailAsync_003(email, subject, body);
                }
                catch (Exception sendEx)
                {
                    _logger.LogWarning(sendEx, "Could not send admin notification to {Email}", email);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch admin notification emails for Subscription {Id}", subscription.Id);
        }
        #endregion
    }
    /// <summary>
    /// [2026-09-28][Gemini] ค้นหารายการ Subscription เดิมในระบบที่มี TaxId หรือ Title ตรงกัน เพื่อใช้ตรวจสอบความซ้ำซ้อนและแจ้งเตือนผู้ใช้
    /// </summary>
    public async Task<List<TbSubscription>> FindDuplicateSubscriptionsAsync(string? taxId, string? title, int excludeSubscriptionId = 0)
    {
        var cleanTaxId = taxId?.Trim().ToLower();
        var cleanTitle = title?.Trim().ToLower();

        var hasTaxId = !string.IsNullOrWhiteSpace(cleanTaxId) && cleanTaxId.Length >= 10;
        var hasTitle = !string.IsNullOrWhiteSpace(cleanTitle) && cleanTitle.Length >= 3;

        if (!hasTaxId && !hasTitle)
        {
            return new List<TbSubscription>();
        }

        try
        {
            using var context = await CreateContextAsync();
            var query = context.Subscriptions
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.Id != excludeSubscriptionId);

            if (hasTaxId && hasTitle)
            {
                query = query.Where(s => s.TaxId.ToLower() == cleanTaxId || s.Title.ToLower() == cleanTitle);
            }
            else if (hasTaxId)
            {
                query = query.Where(s => s.TaxId.ToLower() == cleanTaxId);
            }
            else
            {
                query = query.Where(s => s.Title.ToLower() == cleanTitle);
            }

            return await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking duplicate subscriptions for TaxId={TaxId}, Title={Title}", taxId, title);
            return new List<TbSubscription>();
        }
    }
    #endregion
}

public class SubscriptionQuotaSummary
{
    public int SubscriptionId { get; set; }
    public eSubscriptionPlan Plan { get; set; }

    public int CurrentCorporates { get; set; }
    public int AllowCorporates { get; set; }
    public bool IsCorporatesLimitReached => CurrentCorporates >= AllowCorporates;

    public int CurrentCompanies { get; set; }
    public int AllowCompanies { get; set; }
    public bool IsCompaniesLimitReached => CurrentCompanies >= AllowCompanies;

    public int CurrentFacilities { get; set; }
    public int AllowFacilities { get; set; }
    public bool IsFacilitiesLimitReached => CurrentFacilities >= AllowFacilities;

    public int CurrentUserCount { get; set; }
    public int AllowUserCount { get; set; }
    public bool IsUserCountLimitReached => CurrentUserCount >= AllowUserCount;

    public int CurrentTopics { get; set; }
    public int AllowTopics { get; set; }
    public bool IsTopicsLimitReached => CurrentTopics >= AllowTopics;

    public int CurrentEvents { get; set; }
    public int AllowEvents { get; set; }
    public bool IsEventsLimitReached => CurrentEvents >= AllowEvents;
}
