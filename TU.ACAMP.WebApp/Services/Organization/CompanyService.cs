using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.WebApp.Services.Organization;

public class CompanyService : BaseService
{
    #region class field
    private readonly ILogger<CompanyService> _logger;
    #endregion

    #region class constructor
    public CompanyService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<CompanyService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-17]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการบริษัท (Companies) ภายใต้สิทธิ์ CorporateId หรือ SubscriptionId (หรือดึงทั้งหมดกรณี Admin)
    /// </summary>
    public Task<List<TbCompany>> GetByCorporateIdAsync(
        int? corporateId = null,
        int? subscriptionId = null,
        bool includeFacilities = false,
        bool includeTopics = false,
        bool includeInactive = false)
    {
        var corpIds = corporateId.HasValue && corporateId.Value > 0 ? new[] { corporateId.Value } : null;
        return GetByCorporateIdsAsync(corpIds, subscriptionId, includeFacilities, includeTopics, includeInactive);
    }

    /// <summary>
    /// [2026-09-14][Gemini] ดึงรายการบริษัท (Companies) รองรับการกรองหลาย CorporateIds พร้อมกัน (Multi-select)
    /// </summary>
    public async Task<List<TbCompany>> GetByCorporateIdsAsync(
        IEnumerable<int>? corporateIds = null,
        int? subscriptionId = null,
        bool includeFacilities = false,
        bool includeTopics = false,
        bool includeInactive = false)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Companies
                .Include(c => c.Corporate)
                    .ThenInclude(corp => corp.Subscription)
                .Where(c => !c.IsDeleted);

            var corpIdList = corporateIds?.Where(id => id > 0).Distinct().ToList();
            if (corpIdList != null && corpIdList.Count != 0)
            {
                query = query.Where(c => corpIdList.Contains(c.CorporateId));
            }
            else if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(c => c.Corporate.SubscriptionId == subscriptionId.Value);
            }

            if (!includeInactive)
            {
                query = query.Where(c => c.IsActive);
            }

            if (includeFacilities)
            {
                query = query.Include(c => c.Facilities.Where(f => !f.IsDeleted));
            }

            if (includeFacilities)
            {
                query = query.AsSplitQuery();
            }

            var companies = await query
                .AsNoTracking()
                .OrderBy(c => c.Code)
                .ThenBy(c => c.Title)
                .ToListAsync();

            if (includeTopics && companies.Any())
            {
                var compIds = companies.Select(c => c.Id).ToList();
                var topics = await context.Topics
                    .Where(t => t.Level == eTopicLevel.Company && compIds.Contains(t.TargetId) && !t.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var topicGroup = topics.GroupBy(t => t.TargetId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var comp in companies)
                {
                    if (topicGroup.TryGetValue(comp.Id, out var compTopics))
                    {
                        foreach (var topic in compTopics)
                        {
                            comp.Add(topic);
                        }
                    }
                }
            }

            return companies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching companies for Corporate IDs, Subscription ID: {SubId}", subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูล Company ตาม ID พร้อม Option รวม Facilities และ Topics
    /// </summary>
    public async Task<TbCompany?> GetByIdAsync(int id, bool includeFacilities = false, bool includeTopics = false)
    {
        #region hide [2026-09-07]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Companies
                .Include(c => c.Corporate)
                    .ThenInclude(corp => corp.Subscription)
                .Where(c => c.Id == id && !c.IsDeleted);

            if (includeFacilities)
            {
                query = query.Include(c => c.Facilities.Where(f => !f.IsDeleted));
            }

            var company = await query.AsNoTracking().FirstOrDefaultAsync();

            if (company != null && includeTopics)
            {
                var topics = await context.Topics
                    .Where(t => t.Level == eTopicLevel.Company && t.TargetId == company.Id && !t.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var topic in topics)
                {
                    company.Add(topic);
                }
            }

            return company;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching company by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้างข้อมูล Company ใหม่
    /// </summary>
    public async Task<TbCompany> CreateAsync(TbCompany company)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            company.Code = company.Code.Trim();
            company.Title = company.Title.Trim();
            company.Remark = company.Remark?.Trim() ?? string.Empty;
            company.EnsureKeyIdGenerated();

            var corporate = await context.Corporates
                .Include(c => c.Subscription)
                .FirstOrDefaultAsync(c => c.Id == company.CorporateId && !c.IsDeleted);

            if (corporate == null)
            {
                throw new KeyNotFoundException($"Corporate group with ID {company.CorporateId} not found.");
            }

            if (corporate.Subscription != null)
            {
                var subId = corporate.SubscriptionId;
                var currentCompanyCount = await context.Companies
                    .CountAsync(c => c.Corporate.SubscriptionId == subId && !c.IsDeleted);

                if (currentCompanyCount >= corporate.Subscription.AllowCompanyCount)
                {
                    throw new InvalidOperationException($"Company quota reached ({corporate.Subscription.AllowCompanyCount} max allowed for {corporate.Subscription.SubscriptionPlan} plan). Please upgrade your subscription plan.");
                }
            }

            if (string.IsNullOrWhiteSpace(company.Code))
            {
                company.Code = await GenerateNextCodeAsync(company.CorporateId);
            }

            var exists = await context.Companies
                .AnyAsync(c => c.CorporateId == company.CorporateId &&
                               c.Code == company.Code &&
                               !c.IsDeleted);

            if (exists)
            {
                throw new InvalidOperationException($"Company code '{company.Code}' already exists in this corporate group.");
            }

            context.Companies.Add(company);
            await context.SaveChangesAsync();

            return company;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating company: {Title}", company.Title);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ทำสำเนา Company พร้อมตรวจสอบโควตา
    /// </summary>
    public async Task<TbCompany> DuplicateAsync(int companyId, int? targetCorporateId = null)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var source = await context.Companies
                .Include(c => c.Corporate)
                    .ThenInclude(corp => corp.Subscription)
                .Include(c => c.Facilities.Where(f => !f.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == companyId && !c.IsDeleted);

            if (source == null)
            {
                throw new KeyNotFoundException($"Company with ID {companyId} not found.");
            }

            var corpId = targetCorporateId ?? source.CorporateId;
            var targetCorp = await context.Corporates
                .Include(c => c.Subscription)
                .FirstOrDefaultAsync(c => c.Id == corpId && !c.IsDeleted);

            if (targetCorp == null)
            {
                throw new KeyNotFoundException($"Target corporate group with ID {corpId} not found.");
            }

            if (targetCorp.Subscription != null)
            {
                var subId = targetCorp.SubscriptionId;
                var currentCompanyCount = await context.Companies
                    .CountAsync(c => c.Corporate.SubscriptionId == subId && !c.IsDeleted);

                if (currentCompanyCount >= targetCorp.Subscription.AllowCompanyCount)
                {
                    throw new InvalidOperationException($"Company quota reached ({targetCorp.Subscription.AllowCompanyCount} max allowed for {targetCorp.Subscription.SubscriptionPlan} plan). Please upgrade your subscription plan.");
                }
            }

            var dup = TbCompany.Duplicate(source, corpId);
            dup.Code = await GenerateNextCodeAsync(corpId);

            context.Companies.Add(dup);
            await context.SaveChangesAsync();

            _logger.LogInformation("Company ID {SourceId} duplicated successfully as '{Code}'.", source.Id, dup.Code);
            return dup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error duplicating company ID: {Id}", companyId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงข้อมูล Company
    /// </summary>
    public async Task<TbCompany> UpdateAsync(TbCompany company)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.Companies
                .FirstOrDefaultAsync(c => c.Id == company.Id && !c.IsDeleted);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Company with ID {company.Id} not found.");
            }

            company.Code = company.Code.Trim();
            company.Title = company.Title.Trim();
            company.Remark = company.Remark?.Trim() ?? string.Empty;

            var duplicateCode = await context.Companies
                .AnyAsync(c => c.CorporateId == company.CorporateId &&
                               c.Code == company.Code &&
                               c.Id != company.Id &&
                               !c.IsDeleted);

            if (duplicateCode)
            {
                throw new InvalidOperationException($"Company code '{company.Code}' already exists in this corporate group.");
            }

            existing.Code = company.Code;
            existing.Title = company.Title;
            existing.EquityShare = company.EquityShare;
            existing.Remark = company.Remark;
            existing.IsActive = company.IsActive;

            await context.SaveChangesAsync();
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating company ID: {Id}", company.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบข้อมูล Company (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();

            var company = await context.Companies
                .Include(c => c.Facilities.Where(f => !f.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (company == null) return false;

            if (company.Facilities.Any())
            {
                throw new InvalidOperationException($"Cannot delete company '{company.Title}' because it contains {company.Facilities.Count} facility/site(s). Please remove or reassign the facilities first.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            company.SoftDelete(currentUserId);

            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting company ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// เคลียร์ไซต์งาน/โรงงาน (Facilities) ทั้งหมดภายใต้ Company (Soft Delete)
    /// </summary>
    public async Task<int> ClearChildFacilitiesAsync(int companyId)
    {
        #region hide [2026-09-07]
        try
        {
            using var context = await CreateContextAsync();
            var company = await context.Companies
                .Include(c => c.Facilities.Where(f => !f.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == companyId && !c.IsDeleted);

            if (company == null)
            {
                throw new KeyNotFoundException($"Company with ID {companyId} not found.");
            }

            if (!company.Facilities.Any())
            {
                return 0;
            }

            // --- Safety guard: ตรวจว่ามี facility ใดมี activity event บ้าง ---
            var facilityIds = company.Facilities.Select(f => f.Id).ToList();

            var topicMap = await context.Topics
                .Where(t => t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId) && !t.IsDeleted)
                .Select(t => new { t.Id, t.TargetId })
                .AsNoTracking()
                .ToListAsync();

            var topicIds = topicMap.Select(t => t.Id).ToList();

            var topicIdsWithEvents = topicIds.Count == 0
                ? new List<int>()
                : await context.Events
                    .Where(e => topicIds.Contains(e.TopicId) && !e.IsDeleted)
                    .Select(e => e.TopicId)
                    .Distinct()
                    .ToListAsync();

            var facilityIdsWithEvents = topicMap
                .Where(t => topicIdsWithEvents.Contains(t.Id))
                .Select(t => t.TargetId)
                .Distinct()
                .ToHashSet();

            var facilitiesWithEvents = company.Facilities
                .Where(f => facilityIdsWithEvents.Contains(f.Id))
                .ToList();

            if (facilitiesWithEvents.Any())
            {
                var names = string.Join(", ", facilitiesWithEvents.Select(f => $"'{f.Title}'"));
                throw new InvalidOperationException($"Cannot clear facilities: {facilitiesWithEvents.Count} facility/site(s) ({names}) contain recorded carbon emission activity data. Please remove or reassign the activity logs first.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            var count = company.Facilities.Count;

            foreach (var facility in company.Facilities)
            {
                facility.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Successfully cleared {Count} facility/site(s) for Company ID: {CompanyId}", count, companyId);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing child facilities for Company ID: {CompanyId}", companyId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ตรวจสอบว่ารหัส Code ซ้ำใน Corporate หรือไม่
    /// </summary>
    public async Task<bool> CheckCodeExistsAsync(string code, int corporateId, int currentId = 0)
    {
        #region hide [2026-08-17]
        if (string.IsNullOrWhiteSpace(code)) return false;
        try
        {
            using var context = await CreateContextAsync();
            var trimmed = code.Trim();
            return await context.Companies
                .AnyAsync(c => c.CorporateId == corporateId &&
                               c.Code == trimmed &&
                               c.Id != currentId &&
                               !c.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking company code uniqueness.");
            return false;
        }
        #endregion
    }

    /// <summary>
    /// สร้างรหัส Code ถัดไปอัตโนมัติ (เช่น COM-001)
    /// </summary>
    public async Task<string> GenerateNextCodeAsync(int corporateId)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var count = await context.Companies
                .CountAsync(c => c.CorporateId == corporateId && !c.IsDeleted);

            return $"COM-{(count + 1):D3}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating next company code.");
            return "COM-001";
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Company ID ที่ผู้ใช้งานมีสิทธิ์แก้ไข
    /// </summary>
    public Task<HashSet<int>> GetEditableCompanyIdsAsync(
        string email,
        bool isAdmin,
        int? corporateId = null,
        int? subscriptionId = null)
    {
        var corpIds = corporateId.HasValue && corporateId.Value > 0 ? new[] { corporateId.Value } : null;
        return GetEditableCompanyIdsAsync(email, isAdmin, corpIds, subscriptionId);
    }

    /// <summary>
    /// [2026-09-14][Gemini] ประเมินสิทธิ์การแก้ไขระดับบริษัท รองรับการกรองหลาย CorporateIds พร้อมกัน
    /// </summary>
    public async Task<HashSet<int>> GetEditableCompanyIdsAsync(
        string email,
        bool isAdmin,
        IEnumerable<int>? corporateIds,
        int? subscriptionId = null)
    {
        #region hide [2026-08-17]
        try
        {
            using var context = await CreateContextAsync();
            var corpIdList = corporateIds?.Where(id => id > 0).Distinct().ToList();

            if (isAdmin)
            {
                var query = context.Companies.Where(c => !c.IsDeleted);
                if (corpIdList != null && corpIdList.Count != 0)
                {
                    query = query.Where(c => corpIdList.Contains(c.CorporateId));
                }
                else if (subscriptionId.HasValue && subscriptionId.Value > 0)
                {
                    query = query.Where(c => c.Corporate.SubscriptionId == subscriptionId.Value);
                }
                var ids = await query.Select(c => c.Id).ToListAsync();
                return ids.ToHashSet();
            }

            if (string.IsNullOrWhiteSpace(email)) return new HashSet<int>();

            var lowerEmail = email.Trim().ToLowerInvariant();

            // 1. Check if user has edit role in Subscription
            var userSubIds = await context.SubscriptionUsers
                .Where(su => su.Email.ToLower() == lowerEmail && !su.IsDeleted && su.Role <= eSubscriptionRole.Editor)
                .Select(su => su.SubscriptionId)
                .ToListAsync();

            var ownerSubIds = await context.Subscriptions
                .Where(s => s.OwnerEmail.ToLower() == lowerEmail && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync();

            var allowedSubIds = userSubIds.Concat(ownerSubIds).Distinct().ToList();

            // 2. Check if user has edit role in Corporate
            var corpIdsFromUser = await context.CorporateUsers
                .Where(cu => cu.Email.ToLower() == lowerEmail && !cu.IsDeleted && cu.Role <= eCorporateRole.Editor)
                .Select(cu => cu.CorporateId)
                .ToListAsync();

            var queryCom = context.Companies
                .Include(c => c.Corporate)
                .Where(c => !c.IsDeleted &&
                            (allowedSubIds.Contains(c.Corporate.SubscriptionId) || corpIdsFromUser.Contains(c.CorporateId)));

            if (corpIdList != null && corpIdList.Count != 0)
            {
                queryCom = queryCom.Where(c => corpIdList.Contains(c.CorporateId));
            }
            else if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                queryCom = queryCom.Where(c => c.Corporate.SubscriptionId == subscriptionId.Value);
            }

            var comIds = await queryCom.Select(c => c.Id).ToListAsync();
            return comIds.ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking editable company IDs for user: {Email}", email);
            return new HashSet<int>();
        }
        #endregion
    }

    /// <summary>
    /// คำนวณสรุปยอดปริมาณการปล่อยก๊าซเรือนกระจก (tCO2e) ของแต่ละบริษัท (Company) ตามเงื่อนไขตัวกรอง (ปี, เดือนเริ่มต้น, Scopes, Activity Groups)
    /// </summary>
    public async Task<Dictionary<int, decimal>> GetCompanyGhgSummaryAsync(
        List<int> companyIds,
        int year,
        int startMonth = 1,
        IEnumerable<eTgoScopeNo>? selectedScopes = null,
        IEnumerable<int>? selectedActivityGroupIds = null)
    {
        #region hide [2026-08-29]
        var summary = new Dictionary<int, decimal>();
        if (companyIds == null || !companyIds.Any()) return summary;

        foreach (var id in companyIds)
        {
            summary[id] = 0m;
        }

        try
        {
            using var context = await CreateContextAsync();

            // 1. หา Facility IDs ทั้งหมดภายใต้แต่ละ Company
            var facilityMap = await context.Facilities
                .Where(f => !f.IsDeleted && companyIds.Contains(f.CompanyId))
                .Select(f => new { f.CompanyId, FacilityId = f.Id })
                .ToListAsync();

            var facilityToCompanyId = facilityMap.ToDictionary(f => f.FacilityId, f => f.CompanyId);
            var allFacilityIds = facilityToCompanyId.Keys.ToList();

            // 2. ดึง Topics ทั้งหมดระดับ Company และ Facility
            var topics = await context.Topics
                .Where(t => !t.IsDeleted &&
                    ((t.Level == eTopicLevel.Company && companyIds.Contains(t.TargetId)) ||
                     (t.Level == eTopicLevel.Facility && allFacilityIds.Contains(t.TargetId))))
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.ActivityGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
                .Include(t => t.Events.Where(e => !e.IsDeleted))
                .AsNoTracking()
                .ToListAsync();

            // 3. กำหนดช่วงเวลา (Date Range) สำหรับ 12 เดือนตาม startMonth และ year
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
            foreach (var id in companyIds)
            {
                kgSummary[id] = 0m;
            }

            // 4. คำนวณยอดการปล่อยก๊าซเรือนกระจก
            foreach (var topic in topics)
            {
                int comId = 0;
                if (topic.Level == eTopicLevel.Company)
                {
                    comId = topic.TargetId;
                }
                else if (topic.Level == eTopicLevel.Facility && facilityToCompanyId.TryGetValue(topic.TargetId, out var mappedComId))
                {
                    comId = mappedComId;
                }

                if (comId <= 0 || !kgSummary.ContainsKey(comId)) continue;

                var validEvents = topic.Events
                    .Where(e => e.TimeStamp >= startDate && e.TimeStamp <= endDate)
                    .ToList();

                if (!validEvents.Any()) continue;

                var totalQty = validEvents.Sum(e => e.Quantity);
                if (totalQty == 0m) continue;

                foreach (var child in topic.Childs)
                {
                    if (!child.IsActive || child.Source == null) continue;

                    var scopeNo = child.ActivityGroup?.TgoScopeNo ?? child.Source.Group?.TgoScopeNo;
                    if (scopeSet != null && scopeNo.HasValue && !scopeSet.Contains(scopeNo.Value)) continue;

                    if (agIdSet != null && !agIdSet.Contains(child.ActivityGroupId)) continue;

                    kgSummary[comId] += EmissionCalculator.CalculateKgCO2e(totalQty, child);
                }
            }

            foreach (var id in companyIds)
            {
                summary[id] = kgSummary[id] / 1000m;
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating company GHG summary.");
            return summary;
        }
        #endregion
    }

    /// <summary>
    /// Legacy compatibility wrapper for GetCompanyGhgSummaryAsync
    /// </summary>
    public async Task<Dictionary<int, decimal>> GetCompanyEmissionsAsync(
        List<int> companyIds,
        int year,
        int startMonth = 1,
        IEnumerable<eTgoScopeNo>? selectedScopes = null,
        IEnumerable<int>? selectedActivityGroupIds = null)
    {
        return await GetCompanyGhgSummaryAsync(companyIds, year, startMonth, selectedScopes, selectedActivityGroupIds);
    }

    /// <summary>
    /// เขียนลำดับ OrderId ใหม่ทั้งชุด (1..N) ตามลำดับ Company Id ที่ส่งเข้ามา
    /// ใช้กับการจัดเรียงแบบลาก-วาง (Drag & Drop) ภายในกลุ่มบริษัท (Corporate)เดียวกัน
    /// หมายเหตุ: ผู้เรียกต้องส่งเฉพาะรายการที่อยู่ในกลุ่มบริษัท (Corporate)เดียวกันเท่านั้น
    /// </summary>
    public async Task<bool> ReorderCompaniesInCorporateAsync(IList<int> orderedCompanyIds, string? currentUserId = null)
    {
        #region hide [2026-09-11]
        try
        {
            if (orderedCompanyIds == null || orderedCompanyIds.Count == 0) return false;

            var ids = orderedCompanyIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return false;

            using var context = await CreateContextAsync();
            var items = await context.Companies
                .Where(x => ids.Contains(x.Id) && !x.IsDeleted)
                .ToListAsync();

            if (items.Count == 0) return false;

            // ป้องกันการจัดลำดับข้ามกลุ่มบริษัท (Corporate) (ต้องอยู่ใน CorporateId เดียวกันทั้งหมด)
            var scopeIds = items.Select(x => x.CorporateId).Distinct().ToList();
            if (scopeIds.Count > 1)
            {
                _logger.LogWarning("Reorder rejected: items span {Count} corporates.", scopeIds.Count);
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
            _logger.LogInformation("Reordered {Changed} of {Total} companies (OrderId 1..N) in CorporateId {ScopeId}",
                changed, ids.Count, scopeIds.FirstOrDefault());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering companies");
            throw;
        }
        #endregion
    }

    #endregion
}
