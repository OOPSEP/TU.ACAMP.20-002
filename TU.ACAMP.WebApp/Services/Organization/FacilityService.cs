using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;

namespace TU.ACAMP.WebApp.Services.Organization;

public class FacilityService : BaseService
{
    #region class field
    private readonly ILogger<FacilityService> _logger;
    #endregion

    #region class constructor
    public FacilityService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<FacilityService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-18]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการสถานที่ประกอบการ/โรงงาน (Facilities) ตามเงื่อนไข Subscription, Corporate หรือ Company
    /// </summary>
    public async Task<List<TbFacility>> GetByContextAsync(
        int? subscriptionId = null,
        int? corporateId = null,
        int? companyId = null,
        bool includeTopics = false,
        bool includeInactive = false)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Facilities
                .Include(f => f.Company)
                    .ThenInclude(c => c.Corporate)
                        .ThenInclude(corp => corp.Subscription)
                .Where(f => !f.IsDeleted);

            if (companyId.HasValue && companyId.Value > 0)
            {
                query = query.Where(f => f.CompanyId == companyId.Value);
            }
            else if (corporateId.HasValue && corporateId.Value > 0)
            {
                query = query.Where(f => f.Company.CorporateId == corporateId.Value);
            }
            else if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(f => f.Company.Corporate.SubscriptionId == subscriptionId.Value);
            }

            if (!includeInactive)
            {
                query = query.Where(f => f.IsActive);
            }

            var facilities = await query
                .AsNoTracking()
                .OrderBy(f => f.Title)
                .ToListAsync();

            if (includeTopics && facilities.Any())
            {
                var facIds = facilities.Select(f => f.Id).ToList();
                var topics = await context.Topics
                    .Where(t => t.Level == eTopicLevel.Facility && facIds.Contains(t.TargetId) && !t.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                var topicGroup = topics.GroupBy(t => t.TargetId).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var fac in facilities)
                {
                    if (topicGroup.TryGetValue(fac.Id, out var facTopics))
                    {
                        foreach (var topic in facTopics)
                        {
                            fac.Add(topic);
                        }
                    }
                }
            }

            return facilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching facilities for Company ID: {ComId}, Corporate ID: {CorpId}, Subscription ID: {SubId}",
                companyId, corporateId, subscriptionId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูล Facility ตาม ID
    /// </summary>
    public async Task<TbFacility?> GetByIdAsync(int id, bool includeTopics = false)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var facility = await context.Facilities
                .Include(f => f.Company)
                    .ThenInclude(c => c.Corporate)
                        .ThenInclude(corp => corp.Subscription)
                .Where(f => f.Id == id && !f.IsDeleted)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (facility != null && includeTopics)
            {
                var topics = await context.Topics
                    .Where(t => t.Level == eTopicLevel.Facility && t.TargetId == facility.Id && !t.IsDeleted)
                    .AsNoTracking()
                    .ToListAsync();

                foreach (var topic in topics)
                {
                    facility.Add(topic);
                }
            }

            return facility;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching facility by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้างข้อมูล Facility ใหม่
    /// </summary>
    public async Task<TbFacility> CreateAsync(TbFacility facility)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            facility.Code = facility.Code?.Trim() ?? string.Empty;
            facility.Title = facility.Title.Trim();
            facility.Remark = facility.Remark?.Trim() ?? string.Empty;
            facility.EnsureKeyIdGenerated();

            var company = await context.Companies
                .Include(c => c.Corporate)
                .FirstOrDefaultAsync(c => c.Id == facility.CompanyId && !c.IsDeleted);

            if (company == null)
            {
                throw new KeyNotFoundException($"Company with ID {facility.CompanyId} not found.");
            }

            var duplicateTitle = await context.Facilities
                .AnyAsync(f => f.CompanyId == facility.CompanyId &&
                               f.Title == facility.Title &&
                               !f.IsDeleted);

            if (duplicateTitle)
            {
                throw new InvalidOperationException($"Facility title '{facility.Title}' already exists in this company.");
            }

            context.Facilities.Add(facility);
            await context.SaveChangesAsync();

            _logger.LogInformation("Facility '{Title}' created successfully with ID {Id} for Company {CompanyId}.",
                facility.Title, facility.Id, facility.CompanyId);
            return facility;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating facility: {Title}", facility.Title);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// คัดลอกข้อมูล Facility ซ้ำ (Duplicate)
    /// </summary>
    public async Task<TbFacility> DuplicateAsync(int facilityId, int? targetCompanyId = null)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            var source = await context.Facilities
                .Include(f => f.Company)
                .FirstOrDefaultAsync(f => f.Id == facilityId && !f.IsDeleted);

            if (source == null)
            {
                throw new KeyNotFoundException($"Facility with ID {facilityId} not found.");
            }

            var comId = targetCompanyId ?? source.CompanyId;
            var targetCompany = await context.Companies
                .FirstOrDefaultAsync(c => c.Id == comId && !c.IsDeleted);

            if (targetCompany == null)
            {
                throw new KeyNotFoundException($"Target company with ID {comId} not found.");
            }

            var dup = TbFacility.Duplicate(source, comId);
            dup.Code = await GenerateNextCodeAsync(comId);

            // Generate unique title if already exists
            var baseTitle = dup.Title;
            var title = baseTitle;
            int counter = 1;
            while (await context.Facilities.AnyAsync(f => f.CompanyId == comId && f.Title == title && !f.IsDeleted))
            {
                title = $"{baseTitle} ({++counter})";
            }
            dup.Title = title;

            context.Facilities.Add(dup);
            await context.SaveChangesAsync();

            _logger.LogInformation("Facility ID {SourceId} duplicated successfully as '{Title}' (ID: {NewId}).",
                source.Id, dup.Title, dup.Id);
            return dup;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error duplicating facility ID: {Id}", facilityId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงข้อมูล Facility
    /// </summary>
    public async Task<TbFacility> UpdateAsync(TbFacility facility)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.Facilities
                .FirstOrDefaultAsync(f => f.Id == facility.Id && !f.IsDeleted);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Facility with ID {facility.Id} not found.");
            }

            facility.Code = facility.Code?.Trim() ?? string.Empty;
            facility.Title = facility.Title.Trim();
            facility.Remark = facility.Remark?.Trim() ?? string.Empty;

            var duplicateTitle = await context.Facilities
                .AnyAsync(f => f.CompanyId == facility.CompanyId &&
                               f.Title == facility.Title &&
                               f.Id != facility.Id &&
                               !f.IsDeleted);

            if (duplicateTitle)
            {
                throw new InvalidOperationException($"Facility title '{facility.Title}' already exists in this company.");
            }

            existing.CompanyId = facility.CompanyId;
            existing.Code = facility.Code;
            existing.Title = facility.Title;
            existing.Remark = facility.Remark;
            existing.IsActive = facility.IsActive;

            await context.SaveChangesAsync();
            _logger.LogInformation("Facility ID {Id} updated successfully.", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating facility ID: {Id}", facility.Id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้างรหัส Code ถัดไปอัตโนมัติ (เช่น FAC-001)
    /// </summary>
    public async Task<string> GenerateNextCodeAsync(int companyId)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var count = await context.Facilities
                .CountAsync(f => f.CompanyId == companyId && !f.IsDeleted);

            return $"FAC-{(count + 1):D3}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating next facility code.");
            return "FAC-001";
        }
        #endregion
    }

    /// <summary>
    /// ลบข้อมูล Facility (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-09-07]
        try
        {
            using var context = await CreateContextAsync();

            var facility = await context.Facilities
                .FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);

            if (facility == null) return false;

            // ดึง topics แบบ tracked (ต้อง SaveChanges ได้)
            var topics = await context.Topics
                .Where(t => t.Level == eTopicLevel.Facility && t.TargetId == facility.Id && !t.IsDeleted)
                .ToListAsync();

            var topicIds = topics.Select(t => t.Id).ToList();

            // Safety guard: check if facility contains activity emission events
            var hasEvents = topicIds.Count > 0
                && await context.Events.AnyAsync(e => topicIds.Contains(e.TopicId) && !e.IsDeleted);

            if (hasEvents)
            {
                throw new InvalidOperationException($"Cannot delete facility '{facility.Title}' because it contains recorded carbon emission activity data. Please remove or reassign the activity logs first.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            facility.SoftDelete(currentUserId);

            // Soft-delete child topics
            foreach (var topic in topics)
            {
                topic.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Facility ID {Id} soft-deleted successfully.", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting facility ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ตรวจสอบว่าชื่อ Facility ซ้ำใน Company หรือไม่
    /// </summary>
    public async Task<bool> CheckTitleExistsAsync(string title, int companyId, int currentId = 0)
    {
        #region hide [2026-08-18]
        if (string.IsNullOrWhiteSpace(title)) return false;
        try
        {
            using var context = await CreateContextAsync();
            var trimmed = title.Trim();
            return await context.Facilities
                .AnyAsync(f => f.CompanyId == companyId &&
                               f.Title == trimmed &&
                               f.Id != currentId &&
                               !f.IsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking facility title uniqueness.");
            return false;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Facility ID ที่ผู้ใช้งานมีสิทธิ์แก้ไข
    /// </summary>
    public async Task<HashSet<int>> GetEditableFacilityIdsAsync(
        string email,
        bool isAdmin,
        int? companyId = null,
        int? corporateId = null,
        int? subscriptionId = null)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            if (isAdmin)
            {
                var query = context.Facilities.Where(f => !f.IsDeleted);
                if (companyId.HasValue && companyId.Value > 0)
                {
                    query = query.Where(f => f.CompanyId == companyId.Value);
                }
                else if (corporateId.HasValue && corporateId.Value > 0)
                {
                    query = query.Where(f => f.Company.CorporateId == corporateId.Value);
                }
                else if (subscriptionId.HasValue && subscriptionId.Value > 0)
                {
                    query = query.Where(f => f.Company.Corporate.SubscriptionId == subscriptionId.Value);
                }

                var ids = await query.Select(f => f.Id).ToListAsync();
                return ids.ToHashSet();
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return new HashSet<int>();
            }

            var lowerEmail = email.Trim().ToLowerInvariant();

            // 1. Check if user has edit role in Subscription
            var userSubIds = await context.SubscriptionUsers
                .Where(su => su.Email.ToLower() == lowerEmail && !su.IsDeleted && su.Role <= TU.ACAMP.Domain.Enums.eSubscriptionRole.Editor)
                .Select(su => su.SubscriptionId)
                .ToListAsync();

            var ownerSubIds = await context.Subscriptions
                .Where(s => s.OwnerEmail.ToLower() == lowerEmail && !s.IsDeleted)
                .Select(s => s.Id)
                .ToListAsync();

            var allowedSubIds = userSubIds.Concat(ownerSubIds).Distinct().ToList();

            // 2. Check if user has edit role in Corporate
            var corpIdsFromUser = await context.CorporateUsers
                .Where(cu => cu.Email.ToLower() == lowerEmail && !cu.IsDeleted && cu.Role <= TU.ACAMP.Domain.Enums.eCorporateRole.Editor)
                .Select(cu => cu.CorporateId)
                .ToListAsync();

            var facQuery = context.Facilities
                .Include(f => f.Company)
                    .ThenInclude(c => c.Corporate)
                .Where(f => !f.IsDeleted &&
                            (allowedSubIds.Contains(f.Company.Corporate.SubscriptionId) ||
                             corpIdsFromUser.Contains(f.Company.CorporateId)));

            if (companyId.HasValue && companyId.Value > 0)
            {
                facQuery = facQuery.Where(f => f.CompanyId == companyId.Value);
            }
            else if (corporateId.HasValue && corporateId.Value > 0)
            {
                facQuery = facQuery.Where(f => f.Company.CorporateId == corporateId.Value);
            }
            else if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                facQuery = facQuery.Where(f => f.Company.Corporate.SubscriptionId == subscriptionId.Value);
            }

            var resultIds = await facQuery.Select(f => f.Id).ToListAsync();
            return resultIds.ToHashSet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting editable facility IDs for user: {Email}", email);
            return new HashSet<int>();
        }
        #endregion
    }

    /// <summary>
    /// สรุปปริมาณการปล่อยก๊าซเรือนกระจก (tCO2e) รายสถานที่ประกอบการ/โรงงาน ตามปี fiscal year และตัวกรองต่างๆ
    ///
    /// [GHG-14] Tenant isolation: ส่ง allowedSubscriptionIds เพื่อจำกัดขอบเขตข้อมูลแบบเดียวกับ CfoReportService
    /// (null = ไม่จำกัดที่ระดับ query) และไม่ว่าจะส่งมาหรือไม่ ยอดรวมจะนับเฉพาะ Event
    /// ที่อยู่ subscription เดียวกับ Topic ของตัวเองเท่านั้น
    /// </summary>
    public async Task<Dictionary<int, decimal>> GetFacilityGhgSummaryAsync(
        List<int> facilityIds,
        int year,
        int startMonth,
        List<TU.ACAMP.Domain.Enums.eTgoScopeNo>? selectedScopes = null,
        List<int>? selectedActivityGroupIds = null,
        HashSet<int>? allowedSubscriptionIds = null)
    {
        #region hide [2026-08-21]
        try
        {
            if (facilityIds == null || !facilityIds.Any())
            {
                return new Dictionary<int, decimal>();
            }

            // [2026-09-13][Claude] ตัด Id ซ้ำจากผู้เรียกตั้งแต่ต้นทาง
            // ถ้าปล่อยให้ซ้ำ ขั้นตอนแปลง kgCO2e -> tCO2e ด้านล่างจะหาร 1000 ซ้ำรอบ ทำให้ยอดเพี้ยน 1,000 เท่า
            var targetFacilityIds = facilityIds.Where(id => id > 0).Distinct().ToList();
            if (targetFacilityIds.Count == 0)
            {
                return new Dictionary<int, decimal>();
            }

            // [2026-09-13][Claude] นิยามรอบรายงาน (ยืนยันแล้ว — ห้ามแก้เป็น year - 1)
            //   year + startMonth มาจากตัวกรองบน UI โดยตรง และ "year" คือปีที่รอบบัญชี "เริ่ม"
            //   1 รอบ = 12 เดือนเสมอ นับจากวันที่ 1 ของ startMonth
            //   ตัวอย่าง: year = 2026, startMonth = 10  =>  1 ต.ค. 2026 ถึง 30 ก.ย. 2027
            //   ตั้งใจไม่ fix ให้เริ่มเดือนตุลาคมแบบปีงบประมาณไทย เพราะแต่ละบริษัท
            //   (เช่น ลูกค้าญี่ปุ่น) ใช้รอบบัญชีไม่ตรงกัน จึงให้ผู้ใช้เลือกเดือนเริ่มต้นเองได้
            var startDate = new DateTime(year, startMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = startDate.AddMonths(12);

            using var context = await CreateContextAsync();

            // [GHG-05] ดึง entity มาก่อนแล้วคำนวณด้วย EmissionCalculator (ห้ามคำนวณเองใน projection)
            //          เพื่อให้ "สูตรคำนวณ" ตรงกับทุกเส้นทางในระบบ — ดู EmissionCalculator.CalculateKgCO2e
            //
            // [2026-09-13][Claude] หมายเหตุสำคัญ: "สูตรตรงกัน" ไม่ได้แปลว่า "ยอดตรงกับรายงาน CFO"
            //          เมธอดนี้ต่างจาก CfoReportService.BuildReportAsync โดยตั้งใจ 2 เรื่อง:
            //          1) ช่วงเวลา — ที่นี่ใช้ fiscal window (startMonth ถึง +12 เดือน)
            //             ส่วน CFO กรองด้วยปีปฏิทิน (e.TimeStamp.Year == year)
            //             => ยอดจะตรงกันเฉพาะกรณี startMonth == 1 เท่านั้น
            //          2) ขอบเขต Topic — ที่นี่นับเฉพาะ Level == Facility
            //             ส่วน CFO รวม Level Corporate/Company ขององค์กรนั้นเข้ามาด้วย
            //          ถ้าต้องการยอดที่กระทบกับรายงาน CFO ได้ ต้องเทียบบนเงื่อนไขสองข้อนี้ก่อน
            var topics = await context.Set<TbActivityTopic>()
                .Where(t => !t.IsDeleted
                         && t.Level == eTopicLevel.Facility
                         && targetFacilityIds.Contains(t.TargetId)
                         && (allowedSubscriptionIds == null || allowedSubscriptionIds.Contains(t.SubscriptionId)))
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.ActivityGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
                .Include(t => t.Events.Where(e => !e.IsDeleted
                                              && e.TimeStamp >= startDate
                                              && e.TimeStamp < endDate
                                              && (allowedSubscriptionIds == null || allowedSubscriptionIds.Contains(e.SubscriptionId))))
                .AsNoTracking()
                .ToListAsync();

            var summary = new Dictionary<int, decimal>();
            foreach (var id in targetFacilityIds) summary[id] = 0m;

            foreach (var topic in topics)
            {
                // [GHG-14][2026-09-13][Claude] Tenant isolation: Event ต้องอยู่ subscription เดียวกับ Topic เสมอ
                // ถ้าไม่ตรงถือเป็นข้อมูลผิดปกติ ไม่นำมารวมยอด และบันทึก log ไว้ตรวจสอบ
                var tenantSafeEvents = topic.Events
                    .Where(e => e.SubscriptionId == topic.SubscriptionId)
                    .ToList();

                var crossTenantCount = topic.Events.Count - tenantSafeEvents.Count;
                if (crossTenantCount > 0)
                {
                    _logger.LogWarning(
                        "Skipped {Count} cross-tenant event(s) on TopicId {TopicId} (Topic SubscriptionId {SubscriptionId}) while building facility GHG summary.",
                        crossTenantCount, topic.Id, topic.SubscriptionId);
                }

                var totalQty = tenantSafeEvents.Sum(e => e.Quantity);
                if (totalQty == 0m) continue;

                foreach (var child in topic.Childs)
                {
                    if (child.Source == null) continue;

                    // [2026-09-13][Claude] ActivityGroup (TbActivityGroup) กับ Source.Group (TbEmissionGroup)
                    //     เป็นคนละ entity กัน รวมเป็นตัวแปรเดียวไม่ได้ จึง resolve Scope แบบ nullable
                    //     โดยอ่านจาก ActivityGroup ก่อน แล้วค่อย fallback ไป Source.Group
                    //     จุดสำคัญคือ "ตัดดีฟอลต์ ?? eTgoScopeNo.Scope1 ทิ้ง" เพราะของเดิมทำให้ child
                    //     ที่จัด Scope ไม่ได้ ถูกนับเข้า Scope 1 เงียบ ๆ ทั้งที่ไม่ได้อยู่ในกลุ่มนั้นจริง
                    eTgoScopeNo? scopeNo = child.ActivityGroup?.TgoScopeNo ?? child.Source.Group?.TgoScopeNo;

                    if (scopeNo == null)
                    {
                        _logger.LogWarning(
                            "Cannot resolve TGO scope for TopicChild {ChildId} (TopicId {TopicId}): both ActivityGroup and Source.Group are missing.",
                            child.Id, topic.Id);
                    }

                    // กรอง Scope: ถ้าจัด Scope ไม่ได้ ถือว่าไม่เข้าเงื่อนไขของ Scope ใดเลย (ไม่เหมาเป็น Scope 1)
                    if (selectedScopes != null && selectedScopes.Any()
                        && (scopeNo == null || !selectedScopes.Contains(scopeNo.Value))) continue;

                    // กรอง Activity Group: ตัวเลือกบน UI เป็นรายการ Activity Group จึงเทียบกับ ActivityGroupId เสมอ
                    // (child ที่ไม่มี ActivityGroup ที่ใช้ได้ จะไม่ match กับตัวเลือกใดโดยอัตโนมัติ)
                    if (selectedActivityGroupIds != null && selectedActivityGroupIds.Any()
                        && !selectedActivityGroupIds.Contains(child.ActivityGroupId)) continue;

                    if (summary.ContainsKey(topic.TargetId))
                        summary[topic.TargetId] += EmissionCalculator.CalculateKgCO2e(totalQty, child);
                }
            }

            // แปลง kgCO2e -> tCO2e ครั้งเดียวต่อ 1 facility (วนบน key ของ dictionary ไม่ใช่ list ที่รับเข้ามา)
            foreach (var id in summary.Keys.ToList())
            {
                summary[id] = summary[id] / 1000m;
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching facility GHG summary.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// เขียนลำดับ OrderId ใหม่ทั้งชุด (1..N) ตามลำดับ Facility Id ที่ส่งเข้ามา
    /// ใช้กับการจัดเรียงแบบลาก-วาง (Drag & Drop) ภายในบริษัทเดียวกัน
    /// หมายเหตุ: ผู้เรียกต้องส่งเฉพาะ Facility ที่อยู่ในบริษัทเดียวกันเท่านั้น
    /// </summary>
    public async Task<bool> ReorderFacilitiesInCompanyAsync(IList<int> orderedFacilityIds, string? currentUserId = null)
    {
        #region hide [2026-09-11]
        try
        {
            if (orderedFacilityIds == null || orderedFacilityIds.Count == 0) return false;

            var ids = orderedFacilityIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return false;

            using var context = await CreateContextAsync();
            var facilities = await context.Facilities
                .Where(f => ids.Contains(f.Id) && !f.IsDeleted)
                .ToListAsync();

            if (facilities.Count == 0) return false;

            // ป้องกันการจัดลำดับข้ามบริษัท (ต้องอยู่ใน CompanyId เดียวกันทั้งหมด)
            var companyIds = facilities.Select(f => f.CompanyId).Distinct().ToList();
            if (companyIds.Count > 1)
            {
                _logger.LogWarning("Reorder rejected: facilities span {Count} companies.", companyIds.Count);
                return false;
            }

            var now = AppTimeZone.UtcNow;
            var changed = 0;
            var order = 1;

            foreach (var id in ids)
            {
                var facility = facilities.FirstOrDefault(f => f.Id == id);
                if (facility == null) continue;

                if (facility.OrderId != order)
                {
                    facility.OrderId = order;
                    facility.UpdatedAt = now;
                    if (!string.IsNullOrWhiteSpace(currentUserId))
                    {
                        facility.UpdatedBy = currentUserId;
                    }
                    changed++;
                }
                order++;
            }

            if (changed == 0) return true;

            await context.SaveChangesAsync();
            _logger.LogInformation("Reordered {Changed} of {Total} facilities (OrderId 1..N) in company {CompanyId}",
                changed, ids.Count, companyIds.FirstOrDefault());
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering facilities");
            throw;
        }
        #endregion
    }
    #endregion
}
