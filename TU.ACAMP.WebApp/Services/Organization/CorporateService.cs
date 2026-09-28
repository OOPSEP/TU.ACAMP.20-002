using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Organization;

public class CorporateService : BaseService
{
    #region class field
    private readonly ILogger<CorporateService> _logger;
    #endregion

    #region class constructor
    public CorporateService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<CorporateService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการกลุ่มบริษัท (Corporates) ภายใต้สิทธิ์ SubscriptionId ของลูกค้า (หรือดึงทั้งหมดกรณีผู้ดูแลระบบ)
    /// </summary>
    public async Task<List<TbCorporate>> GetBySubscriptionIdAsync(
        int? subscriptionId = null,
        bool includeCompanies = false,
        bool includeUsers = false,
        bool includeTopics = false,
        bool includeInactive = false)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Corporates
                .Include(c => c.Subscription)
                .Where(c => !c.IsDeleted);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(c => c.SubscriptionId == subscriptionId.Value);
            }

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            if (includeCompanies)
            {
                query = query.Include(c => c.Companies.Where(com => !com.IsDeleted));
            }

            if (includeUsers)
            {
                query = query.Include(c => c.Users.Where(u => !u.IsDeleted));
            }

            if (includeCompanies || includeUsers)
            {
                query = query.AsSplitQuery();
            }

            var corporates = await query
                .AsNoTracking()
                .OrderBy(c => c.Code)
                .ThenBy(c => c.Title)
                .ToListAsync();

            if (includeTopics && corporates.Any())
            {
                var corpIds = corporates.Select(c => c.Id).ToList();
                var topics = await context.Topics
                    .Where(t => t.Level == eTopicLevel.Corporate && corpIds.Contains(t.TargetId) && !t.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var topicGroup = topics.GroupBy(t => t.TargetId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var corp in corporates)
                {
                    if (topicGroup.TryGetValue(corp.Id, out var corpTopics))
                    {
                        foreach (var topic in corpTopics)
                        {
                            corp.Add(topic);
                        }
                    }
                }
            }

            return corporates;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching corporates for subscription context: {SubId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูล Corporate ตาม ID พร้อม Option รวม Companies และ Users
    /// </summary>
    public async Task<TbCorporate?> GetByIdAsync(
        int id,
        bool includeCompanies = false,
        bool includeUsers = false)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Corporates
                .Include(c => c.Subscription)
                .Where(c => c.Id == id && !c.IsDeleted);

            if (includeCompanies)
            {
                query = query.Include(c => c.Companies.Where(com => !com.IsDeleted));
            }

            if (includeUsers)
            {
                query = query.Include(c => c.Users.Where(u => !u.IsDeleted));
            }

            if (includeCompanies || includeUsers)
            {
                query = query.AsSplitQuery();
            }

            return await query.AsNoTracking().FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching corporate by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้างข้อมูล Corporate ใหม่
    /// </summary>
    public async Task<TbCorporate> CreateAsync(TbCorporate corporate)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            corporate.Code = corporate.Code.Trim();
            corporate.Title = corporate.Title.Trim();
            corporate.Remark = corporate.Remark?.Trim() ?? string.Empty;
            corporate.EnsureKeyIdGenerated();

            if (string.IsNullOrWhiteSpace(corporate.Code))
            {
                corporate.Code = await GenerateNextCodeAsync(corporate.SubscriptionId);
            }

            // Check subscription corporate quota
            var sub = await context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == corporate.SubscriptionId && !s.IsDeleted);

            if (sub != null)
            {
                var currentCount = await context.Corporates
                    .CountAsync(c => c.SubscriptionId == corporate.SubscriptionId && !c.IsDeleted);

                if (currentCount >= sub.AllowCorporateCount)
                {
                    throw new InvalidOperationException($"Corporate quota reached ({sub.AllowCorporateCount} max allowed for {sub.SubscriptionPlan} plan). Please upgrade your subscription plan.");
                }
            }

            var exists = await context.Corporates
                .AnyAsync(c => c.SubscriptionId == corporate.SubscriptionId &&
                               c.Code == corporate.Code &&
                               !c.IsDeleted);

            if (exists)
            {
                throw new InvalidOperationException($"Corporate code '{corporate.Code}' already exists in this subscription.");
            }

            context.Corporates.Add(corporate);
            await context.SaveChangesAsync();

            return corporate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating corporate: {Title}", corporate.Title);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ทำสำเนา Corporate พร้อมคัดลอกข้อมูลทั้งหมดรวมถึง Users
    /// </summary>
    public async Task<TbCorporate> DuplicateAsync(int corporateId, int? targetSubscriptionId = null)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var source = await context.Corporates
                .Include(c => c.Users.Where(u => !u.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == corporateId && !c.IsDeleted);

            if (source == null)
            {
                throw new KeyNotFoundException($"Corporate with ID {corporateId} not found.");
            }

            var subId = targetSubscriptionId ?? source.SubscriptionId;

            // Check subscription corporate quota
            var sub = await context.Subscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == subId && !s.IsDeleted);

            if (sub != null)
            {
                var currentCount = await context.Corporates
                    .CountAsync(c => c.SubscriptionId == subId && !c.IsDeleted);

                if (currentCount >= sub.AllowCorporateCount)
                {
                    throw new InvalidOperationException($"Corporate quota reached ({sub.AllowCorporateCount} max allowed for {sub.SubscriptionPlan} plan). Please upgrade your subscription plan.");
                }
            }

            var dup = TbCorporate.Duplicate(source, subId);
            dup.Code = await GenerateNextCodeAsync(subId);

            context.Corporates.Add(dup);
            await context.SaveChangesAsync();

            _logger.LogInformation("Corporate ID {SourceId} duplicated successfully as '{Code}' with {UserCount} user(s).", source.Id, dup.Code, dup.Users.Count);
            return dup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error duplicating corporate ID: {Id}", corporateId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงข้อมูล Corporate
    /// </summary>
    public async Task<TbCorporate> UpdateAsync(TbCorporate corporate)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.Corporates
                .FirstOrDefaultAsync(c => c.Id == corporate.Id && !c.IsDeleted);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Corporate with ID {corporate.Id} not found.");
            }

            corporate.Code = corporate.Code.Trim();
            corporate.Title = corporate.Title.Trim();
            corporate.Remark = corporate.Remark?.Trim() ?? string.Empty;

            var duplicateCode = await context.Corporates
                .AnyAsync(c => c.SubscriptionId == corporate.SubscriptionId &&
                               c.Code == corporate.Code &&
                               c.Id != corporate.Id &&
                               !c.IsDeleted);

            if (duplicateCode)
            {
                throw new InvalidOperationException($"Corporate code '{corporate.Code}' already exists in this subscription.");
            }

            existing.Code = corporate.Code;
            existing.Title = corporate.Title;
            existing.Remark = corporate.Remark;
            existing.IsActive = corporate.IsActive;

            await context.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating corporate ID: {Id}", corporate.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบข้อมูล Corporate (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var corporate = await context.Corporates
                .Include(c => c.Companies.Where(com => !com.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (corporate == null) return false;

            if (corporate.Companies.Any())
            {
                throw new InvalidOperationException($"Cannot delete corporate '{corporate.Title}' because it contains {corporate.Companies.Count} company/companies. Please remove or reassign the companies first.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            corporate.SoftDelete(currentUserId);

            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting corporate ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// เคลียร์บริษัทลูกทั้งหมด (Soft Delete) เฉพาะบริษัทที่ไม่มี Facility/Site ใช้งานอยู่
    /// </summary>
    public async Task<int> ClearChildCompaniesAsync(int corporateId)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var corporate = await context.Corporates
                .Include(c => c.Companies.Where(com => !com.IsDeleted))
                    .ThenInclude(com => com.Facilities.Where(f => !f.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == corporateId && !c.IsDeleted);

            if (corporate == null)
            {
                throw new KeyNotFoundException($"Corporate with ID {corporateId} not found.");
            }

            if (!corporate.Companies.Any())
            {
                return 0;
            }

            // Safety guard: check if any company has active facilities
            var companiesWithFacilities = corporate.Companies
                .Where(com => com.Facilities.Any(f => !f.IsDeleted))
                .ToList();

            if (companiesWithFacilities.Any())
            {
                var names = string.Join(", ", companiesWithFacilities.Select(c => $"'{c.Title}'"));
                throw new InvalidOperationException($"Cannot clear companies: {companiesWithFacilities.Count} company/companies ({names}) contain active facilities/sites. Please remove or reassign the facilities first.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            var count = corporate.Companies.Count;

            foreach (var company in corporate.Companies)
            {
                company.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Successfully cleared {Count} child company/companies for Corporate ID: {CorporateId}", count, corporateId);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing child companies for Corporate ID: {CorporateId}", corporateId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ตรวจสอบว่ารหัส Code ซ้ำใน Subscription หรือไม่
    /// </summary>
    public async Task<bool> CheckCodeExistsAsync(string code, int subscriptionId, int currentId = 0)
    {
        #region hide [2026-08-17]
        if (string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            using var context = await CreateContextAsync();
            var trimmed = code.Trim();
            return await context.Corporates
                .AnyAsync(c => c.SubscriptionId == subscriptionId &&
                               c.Code == trimmed &&
                               c.Id != currentId &&
                               !c.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking corporate code uniqueness.");
            return false;
        }
        #endregion
    }

    /// <summary>
    /// สร้างรหัส Code ถัดไปอัตโนมัติ (เช่น CRP-001)
    /// </summary>
    public async Task<string> GenerateNextCodeAsync(int subscriptionId)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var count = await context.Corporates
                .CountAsync(c => c.SubscriptionId == subscriptionId && !c.IsDeleted);

            return $"CRP-{(count + 1):D3}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating next corporate code.");
            return "CRP-001";
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Corporate ID ที่ผู้ใช้งานมีสิทธิ์แก้ไข
    /// </summary>
    public async Task<HashSet<int>> GetEditableCorporateIdsAsync(string email, bool isAdmin, int? subscriptionId = null)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            if (isAdmin)
            {
                var query = context.Corporates.Where(c => !c.IsDeleted);
                if (subscriptionId.HasValue && subscriptionId.Value > 0)
                {
                    query = query.Where(c => c.SubscriptionId == subscriptionId.Value);
                }
                var ids = await query.Select(c => c.Id).ToListAsync();
                return ids.ToHashSet();
            }

            if (string.IsNullOrWhiteSpace(email)) return new HashSet<int>();

            var lowerEmail = email.Trim().ToLowerInvariant();

            // Check if user is Subscription Owner/Admin/Editor
            var userSubs = await context.SubscriptionUsers
                .Where(su => su.Email.ToLower() == lowerEmail && !su.IsDeleted && su.Role <= eSubscriptionRole.Editor)
                .Select(su => su.SubscriptionId)
                .ToListAsync();

            // Check if user is owner of subscription directly
            var ownerSubs = await context.Subscriptions
                .Where(s => s.OwnerEmail.ToLower() == lowerEmail && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync();

            var allowedSubIds = userSubs.Concat(ownerSubs).Distinct().ToList();

            var queryCorp = context.Corporates
                .Where(c => !c.IsDeleted && allowedSubIds.Contains(c.SubscriptionId));

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                queryCorp = queryCorp.Where(c => c.SubscriptionId == subscriptionId.Value);
            }

            var corpIdsFromSub = await queryCorp.Select(c => c.Id).ToListAsync();

            // Also check Corporate Users
            var directCorpIds = await context.CorporateUsers
                .Where(cu => cu.Email.ToLower() == lowerEmail && !cu.IsDeleted && cu.Role <= eCorporateRole.Editor)
                .Select(cu => cu.CorporateId)
                .ToListAsync();

            return corpIdsFromSub.Concat(directCorpIds).ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching editable corporate IDs for user: {Email}", email);
            return new HashSet<int>();
        }
        #endregion
    }

    /// <summary>
    /// คำนวณสรุปยอดปริมาณการปล่อยก๊าซเรือนกระจก (tCO2e) ของกลุ่มบริษัท ตามตัวกรอง (ปีงบประมาณ, Scope, Activity Group)
    /// </summary>
    public async Task<Dictionary<int, decimal>> GetCorporateGhgSummaryAsync(
        List<int> corporateIds,
        int year,
        int startMonth = 1,
        IEnumerable<eTgoScopeNo>? selectedScopes = null,
        IEnumerable<int>? selectedActivityGroupIds = null)
    {
        #region hide [2026-08-21]
        var summary = new Dictionary<int, decimal>();
        if (corporateIds == null || !corporateIds.Any()) return summary;

        foreach (var id in corporateIds)
        {
            summary[id] = 0m;
        }

        try
        {
            using var context = await CreateContextAsync();

            // 1. Map Companies under Corporates
            var companyMap = await context.Companies
                .Where(c => !c.IsDeleted && corporateIds.Contains(c.CorporateId))
                .Select(c => new { c.Id, c.CorporateId })
                .ToListAsync();

            var companyToCorpMap = companyMap.ToDictionary(c => c.Id, c => c.CorporateId);
            var allCompanyIds = companyToCorpMap.Keys.ToList();

            // 2. Map Facilities under Companies
            var facilityMap = await context.Facilities
                .Where(f => !f.IsDeleted && allCompanyIds.Contains(f.CompanyId))
                .Select(f => new { f.Id, f.CompanyId })
                .ToListAsync();

            var facilityToCorpMap = facilityMap.ToDictionary(
                f => f.Id,
                f => companyToCorpMap[f.CompanyId]
            );
            var allFacilityIds = facilityToCorpMap.Keys.ToList();

            // 3. Fetch Topics across Corporate, Company, and Facility levels
            var topics = await context.Topics
                .Where(t => !t.IsDeleted &&
                    ((t.Level == eTopicLevel.Corporate && corporateIds.Contains(t.TargetId)) ||
                     (t.Level == eTopicLevel.Company && allCompanyIds.Contains(t.TargetId)) ||
                     (t.Level == eTopicLevel.Facility && allFacilityIds.Contains(t.TargetId))))
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.ActivityGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
                .Include(t => t.Events.Where(e => !e.IsDeleted))
                .AsNoTracking()
                .ToListAsync();

            // 4. Determine Fiscal Year Date Range
            DateTime startDate;
            DateTime endDate;
            if (startMonth <= 1)
            {
                startDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                endDate = new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            }
            else
            {
                startDate = new DateTime(year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
                endDate = new DateTime(year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc).AddYears(1).AddTicks(-1);
            }

            var scopeSet = selectedScopes != null && selectedScopes.Any() ? selectedScopes.ToHashSet() : null;
            var agIdSet = selectedActivityGroupIds != null && selectedActivityGroupIds.Any() ? selectedActivityGroupIds.ToHashSet() : null;

            var kgSummary = new Dictionary<int, decimal>();
            foreach (var id in corporateIds)
            {
                kgSummary[id] = 0m;
            }

            // 5. Calculate Emissions
            foreach (var topic in topics)
            {
                int corpId = 0;
                if (topic.Level == eTopicLevel.Corporate)
                {
                    corpId = topic.TargetId;
                }
                else if (topic.Level == eTopicLevel.Company && companyToCorpMap.TryGetValue(topic.TargetId, out var mappedCorpId1))
                {
                    corpId = mappedCorpId1;
                }
                else if (topic.Level == eTopicLevel.Facility && facilityToCorpMap.TryGetValue(topic.TargetId, out var mappedCorpId2))
                {
                    corpId = mappedCorpId2;
                }

                if (corpId <= 0 || !kgSummary.ContainsKey(corpId)) continue;

                var validEvents = topic.Events
                    .Where(e => e.TimeStamp >= startDate && e.TimeStamp <= endDate)
                    .ToList();

                if (!validEvents.Any()) continue;

                foreach (var child in topic.Childs)
                {
                    if (!child.IsActive || child.Source == null) continue;

                    var scopeNo = child.ActivityGroup?.TgoScopeNo ?? child.Source.Group?.TgoScopeNo;
                    if (scopeSet != null && scopeNo.HasValue && !scopeSet.Contains(scopeNo.Value)) continue;

                    if (agIdSet != null && !agIdSet.Contains(child.ActivityGroupId)) continue;

                    var ratio = (decimal)(child.UnitConversionRatio ?? 1);
                    var ef = (decimal)child.Source.EF;
                    var share = (decimal)child.EmissionSharePercent / 100m;

                    foreach (var ev in validEvents)
                    {
                        kgSummary[corpId] += ev.Quantity * ratio * ef * share;
                    }
                }
            }

            foreach (var id in corporateIds)
            {
                summary[id] = kgSummary[id] / 1000m;
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching corporate GHG summary with filters");
            return summary;
        }
        #endregion
    }

    /// <summary>
    /// เขียนลำดับ OrderId ใหม่ทั้งชุด (1..N) ตามลำดับ Corporate Id ที่ส่งเข้ามา
    /// ใช้กับการจัดเรียงแบบลาก-วาง (Drag & Drop) ภายใน Subscription เดียวกัน
    /// หมายเหตุ: ผู้เรียกต้องส่งเฉพาะรายการที่อยู่ใน Subscription เดียวกันเท่านั้น
    /// </summary>
    public async Task<bool> ReorderCorporatesInSubscriptionAsync(IList<int> orderedCorporateIds, string? currentUserId = null)
    {
        #region hide [2026-09-11]
        try
        {
            if (orderedCorporateIds == null || orderedCorporateIds.Count == 0) return false;

            var ids = orderedCorporateIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return false;

            using var context = await CreateContextAsync();
            var items = await context.Corporates
                .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
                .ToListAsync();

            if (items.Count == 0) return false;

            // ป้องกันการจัดลำดับข้าม Subscription  (ต้องอยู่ใน SubscriptionId เดียวกันทั้งหมด)
            var scopeIds = items.Select(x => x.SubscriptionId).Distinct().ToList();
            if (scopeIds.Count > 1)
            {
                _logger.LogWarning("Reorder rejected: items span {Count} subscriptions.", scopeIds.Count);
                return false;
            }

            var now = AppTimeZone.UtcNow;
            var changed = 0;
            var order = 1;

            foreach (var id in ids)
            {
                var item = items.FirstOrDefault(x => x.Id == id);
                if (item == null) continue;

                if (item.OrderId != order)
                {
                    item.OrderId = order;
                    item.UpdatedAt = now;
                    if (!string.IsNullOrWhiteSpace(currentUserId))
                    {
                        item.UpdatedBy = currentUserId;
                    }
                    changed++;
                }
                order++;
            }

            if (changed == 0) return true;

            await context.SaveChangesAsync();
            _logger.LogInformation("Reordered {Changed} of {Total} corporates (OrderId 1..N) in SubscriptionId {ScopeId}",
                changed, ids.Count, scopeIds.FirstOrDefault());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering corporates");
            throw;
        }
        #endregion
    }

    #endregion
}
