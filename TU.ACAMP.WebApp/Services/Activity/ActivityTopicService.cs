using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Models.Library;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Activity;

public class ActivityTopicService : BaseService
{
    #region class field
    private readonly ILogger<ActivityTopicService> _logger;
    #endregion

    #region class constructor
    public ActivityTopicService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        AuthenticationStateProvider authStateProvider,
        ILogger<ActivityTopicService> logger) : base(dbFactory, authStateProvider)
    {
        #region hide [2026-08-18]
        _logger = logger;
        #endregion
    }
    #endregion

    #region class methods
    /// <summary>
    /// ดึงรายการหัวข้อกิจกรรม (Activity Topics) ตามเงื่อนไข Context ขอบเขต (Subscription, Level, TargetId, Group)
    /// </summary>
    public async Task<List<TbActivityTopic>> GetByContextAsync(
        int? subscriptionId = null,
        eTopicLevel? level = null,
        int? targetId = null,
        int? activityGroupId = null,
        int? emissionGroupId = null,
        bool includeRelations = true,
        bool includeInactive = false)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Topics
                .Where(t => !t.IsDeleted);

            if (subscriptionId.HasValue && subscriptionId.Value > 0)
            {
                query = query.Where(t => t.SubscriptionId == subscriptionId.Value);
            }

            if (level.HasValue)
            {
                query = query.Where(t => t.Level == level.Value);
            }

            if (targetId.HasValue && targetId.Value > 0)
            {
                query = query.Where(t => t.TargetId == targetId.Value);
            }

            if (emissionGroupId.HasValue && emissionGroupId.Value > 0)
            {
                query = query.Where(t => t.EmissionGroupId == emissionGroupId.Value);
            }

            if (!includeInactive)
            {
                query = query.Where(t => t.IsActive);
            }

            if (includeRelations)
            {
                query = query
                    .Include(t => t.EmissionGroup)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.ActivityGroup)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.ActivityUnit)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.Source)
                            .ThenInclude(s => s!.Unit)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.Source)
                            .ThenInclude(s => s!.Group)
                    .Include(t => t.Events.Where(e => !e.IsDeleted));
            }

            var results = await query
                .AsNoTracking()
                .OrderBy(t => t.OrderId)
                .ThenBy(t => t.Id)
                .ToListAsync();

            if (activityGroupId.HasValue && activityGroupId.Value > 0)
            {
                results = results.Where(t => t.Childs.Any(c => c.ActivityGroupId == activityGroupId.Value)).ToList();
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity topics by context criteria.");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงข้อมูล Activity Topic ตาม ID พร้อม Child Relations
    /// </summary>
    public async Task<TbActivityTopic?> GetByIdAsync(int id, bool includeRelations = true)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var query = context.Topics
                .Where(t => t.Id == id && !t.IsDeleted);

            if (includeRelations)
            {
                query = query
                    .Include(t => t.EmissionGroup)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.ActivityGroup)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.ActivityUnit)
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                        .ThenInclude(c => c.Source)
                            .ThenInclude(s => s!.Unit)
                    .Include(t => t.Events.Where(e => !e.IsDeleted));
            }

            return await query.AsNoTracking().FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching activity topic by ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สร้างหัวข้อกิจกรรมคาร์บอน (Activity Topic) ใหม่
    /// </summary>
    public async Task<TbActivityTopic> CreateAsync(TbActivityTopic topic)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้ายของชื่อ Title (รวมถึงหลายช่องว่างและ Unicode whitespace)
            topic.Title = ActivityTopicExcelService.TrimTitle(topic.Title);
            topic.Remark = topic.Remark?.Trim() ?? string.Empty;

            context.Topics.Add(topic);
            SyncShadowForeignKeys(context, topic);
            await context.SaveChangesAsync();

            _logger.LogInformation("Activity topic created successfully: '{Title}' (ID: {Id})", topic.Title, topic.Id);
            return topic;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating activity topic: {Title}", topic.Title);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ปรับปรุงข้อมูล Activity Topic
    /// </summary>
    public async Task<TbActivityTopic> UpdateAsync(TbActivityTopic topic)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            var existing = await context.Topics
                .Include(t => t.Childs)
                .FirstOrDefaultAsync(t => t.Id == topic.Id && !t.IsDeleted);

            if (existing == null)
            {
                throw new KeyNotFoundException($"Activity topic with ID {topic.Id} not found.");
            }

            // [2026-09-19][Antigravity] ตัดช่องว่างหัวท้ายของชื่อ Title (รวมถึงหลายช่องว่างและ Unicode whitespace)
            topic.Title = ActivityTopicExcelService.TrimTitle(topic.Title);
            topic.Remark = topic.Remark?.Trim() ?? string.Empty;

            existing.Level = topic.Level;
            existing.TargetId = topic.TargetId;
            existing.EmissionGroupId = topic.EmissionGroupId;
            existing.Title = topic.Title;
            existing.Remark = topic.Remark;
            existing.JsonDataFiles = topic.JsonDataFiles;
            existing.IsActive = topic.IsActive;

            SyncShadowForeignKeys(context, existing);

            await context.SaveChangesAsync();
            _logger.LogInformation("Activity topic updated successfully: ID {Id}", existing.Id);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating activity topic: {Id}", topic.Id);
            throw;
        }
        #endregion
    }

    private static void SyncShadowForeignKeys(ApplicationDbContext context, TbActivityTopic topic)
    {
        var entry = context.Entry(topic);
        if (entry.Metadata.FindProperty("TbCorporateId") != null)
        {
            entry.Property("TbCorporateId").CurrentValue = topic.Level == eTopicLevel.Corporate && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        }

        if (entry.Metadata.FindProperty("TbCompanyId") != null)
        {
            entry.Property("TbCompanyId").CurrentValue = topic.Level == eTopicLevel.Company && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        }

        if (entry.Metadata.FindProperty("TbFacilityId") != null)
        {
            entry.Property("TbFacilityId").CurrentValue = topic.Level == eTopicLevel.Facility && topic.TargetId > 0 ? (int?)topic.TargetId : null;
        }
    }

    /// <summary>
    /// คัดลอกข้อมูล Activity Topic (Duplicate)
    /// </summary>
    public async Task<TbActivityTopic> DuplicateAsync(int topicId)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            var source = await context.Topics
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                .FirstOrDefaultAsync(t => t.Id == topicId && !t.IsDeleted);

            if (source == null)
            {
                throw new KeyNotFoundException($"Activity topic with ID {topicId} not found.");
            }

            var copy = new TbActivityTopic
            {
                SubscriptionId = source.SubscriptionId,
                Level = source.Level,
                TargetId = source.TargetId,
                EmissionGroupId = source.EmissionGroupId,
                Title = $"{source.Title} (Copy)",
                Remark = source.Remark,
                JsonDataFiles = source.JsonDataFiles?.DeepClone() ?? JsonNode.Parse("[]")!,
                IsActive = true
            };

            foreach (var child in source.Childs)
            {
                copy.Add(new TbActivityTopicChild
                {
                    ActivityGroupId = child.ActivityGroupId,
                    SourceId = child.SourceId,
                    UnitConversionRatio = child.UnitConversionRatio,
                    EmissionSharePercent = child.EmissionSharePercent
                });
            }

            context.Topics.Add(copy);
            SyncShadowForeignKeys(context, copy);
            await context.SaveChangesAsync();

            _logger.LogInformation("Activity topic ID {SourceId} duplicated as '{Title}' (ID: {NewId})",
                source.Id, copy.Title, copy.Id);
            return copy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error duplicating activity topic ID: {Id}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบหัวข้อกิจกรรม (Soft Delete)
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();

            var topic = await context.Topics
                .Include(t => t.Events.Where(e => !e.IsDeleted))
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (topic == null) return false;

            var currentUserId = await GetCurrentUserIdAsync();
            topic.SoftDelete(currentUserId);

            foreach (var ev in topic.Events)
            {
                ev.SoftDelete(currentUserId);
            }

            foreach (var child in topic.Childs)
            {
                child.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Activity topic ID {Id} deleted successfully.", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting activity topic ID: {Id}", id);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ลบ/เคลียร์รายการ Activity Events ทั้งหมดของ Topic (Soft Delete)
    /// </summary>
    public async Task<int> ClearEventsAsync(int topicId)
    {
        #region hide [2026-09-11]
        try
        {
            using var context = await CreateContextAsync();
            var events = await context.Events
                .Where(e => e.TopicId == topicId && !e.IsDeleted)
                .ToListAsync();

            if (!events.Any()) return 0;

            var currentUserId = await GetCurrentUserIdAsync();
            foreach (var ev in events)
            {
                ev.SoftDelete(currentUserId);
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Cleared {Count} activity events for Topic ID {Id}", events.Count, topicId);
            return events.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing activity events for Topic ID: {Id}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ล้างหัวข้อกิจกรรมทั้งหมดในระดับขอบเขตองค์กรที่ระบุ (Corporate, Company, Facility)
    /// </summary>
    public async Task<int> ClearTopicsByTargetAsync(eTopicLevel level, int targetId)
    {
        #region hide [2026-08-29]
        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();

            var topics = await context.Topics
                .Include(t => t.Events.Where(e => !e.IsDeleted))
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                .Where(t => t.Level == level && t.TargetId == targetId && !t.IsDeleted)
                .ToListAsync();

            if (!topics.Any()) return 0;

            foreach (var topic in topics)
            {
                topic.SoftDelete(currentUserId);
                foreach (var ev in topic.Events)
                {
                    ev.SoftDelete(currentUserId);
                }
                foreach (var child in topic.Childs)
                {
                    child.SoftDelete(currentUserId);
                }
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Cleared {Count} activity topics for Level: {Level}, Target ID: {TargetId}", topics.Count, level, targetId);
            return topics.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing activity topics for Level: {Level}, Target ID: {TargetId}", level, targetId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Activity Events 12 เดือน ของหัวข้อตามปี
    /// </summary>
    public async Task<List<TbActivityEvent>> GetEventsByTopicIdAsync(int topicId, int? year = null)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var targetYear = year ?? DateTime.UtcNow.Year;

            var events = await context.Events
                .Where(e => e.TopicId == topicId && e.TimeStamp.Year == targetYear && !e.IsDeleted)
                .OrderBy(e => e.TimeStamp)
                .AsNoTracking()
                .ToListAsync();

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching events for topic ID: {TopicId}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// บันทึกชุดข้อมูลกิจกรรมรายเดือน (12 Monthly Events)
    /// </summary>
    public async Task SaveMonthlyEventsAsync(int topicId, int year, decimal[] monthlyQuantities, string[] monthlyRemarks)
    {
        #region hide [2026-08-18]
        try
        {
            using var context = await CreateContextAsync();
            var topic = await context.Topics.FirstOrDefaultAsync(t => t.Id == topicId && !t.IsDeleted);
            if (topic == null)
            {
                throw new KeyNotFoundException($"Topic ID {topicId} not found.");
            }

            var currentUserId = await GetCurrentUserIdAsync();
            var existingEvents = await context.Events
                .Where(e => e.TopicId == topicId && e.TimeStamp.Year == year && !e.IsDeleted)
                .ToListAsync();

            for (int month = 1; month <= 12; month++)
            {
                var idx = month - 1;
                var qty = idx < monthlyQuantities.Length ? monthlyQuantities[idx] : 0m;
                var remark = idx < monthlyRemarks.Length ? monthlyRemarks[idx] ?? string.Empty : string.Empty;

                var existing = existingEvents.FirstOrDefault(e => e.TimeStamp.Month == month);
                if (existing != null)
                {
                    existing.Quantity = qty;
                    existing.Remark = remark;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = currentUserId;
                }
                else if (qty > 0 || !string.IsNullOrWhiteSpace(remark))
                {
                    var newEvent = new TbActivityEvent
                    {
                        TopicId = topicId,
                        SubscriptionId = topic.SubscriptionId,
                        TimeStamp = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
                        Quantity = qty,
                        Remark = remark,
                        IsActive = true
                    };
                    context.Events.Add(newEvent);
                }
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Saved monthly events for topic ID {TopicId}, Year: {Year}", topicId, year);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving monthly events for topic ID: {TopicId}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// บันทึกหรืออัปเดต Activity Topic พร้อมรายการ Child Emission Sources
    /// </summary>
    public async Task<TbActivityTopic> SaveTopicWithChildsAsync(TbActivityTopic topic, List<TbActivityTopicChild> childs)
    {
        #region hide [2026-08-20]
        if (topic == null) throw new ArgumentNullException(nameof(topic));

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();
            var isNew = topic.Id <= 0;

            topic.Title = topic.Title?.Trim() ?? string.Empty;
            topic.Remark = topic.Remark?.Trim() ?? string.Empty;

            // Ensure EmissionGroupId refers to a valid active EmissionGroup
            if (topic.EmissionGroupId <= 0 || !await context.EmissionGroups.AnyAsync(eg => eg.Id == topic.EmissionGroupId && !eg.IsDeleted))
            {
                var defaultGroup = await context.EmissionGroups
                    .Where(eg => !eg.IsDeleted && eg.IsActive)
                    .OrderBy(eg => eg.Id)
                    .FirstOrDefaultAsync();
                if (defaultGroup != null)
                {
                    topic.EmissionGroupId = defaultGroup.Id;
                }
            }

            TbActivityTopic targetTopic;
            if (isNew)
            {
                context.Topics.Add(topic);
                SyncShadowForeignKeys(context, topic);
                await context.SaveChangesAsync(currentUserId);
                targetTopic = topic;
            }
            else
            {
                var existing = await context.Topics
                    .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .FirstOrDefaultAsync(t => t.Id == topic.Id && !t.IsDeleted);

                if (existing == null)
                {
                    throw new KeyNotFoundException($"Activity topic with ID {topic.Id} not found.");
                }

                existing.Level = topic.Level;
                existing.TargetId = topic.TargetId;
                existing.EmissionGroupId = topic.EmissionGroupId;
                existing.Title = topic.Title;
                existing.Remark = topic.Remark;
                existing.JsonDataFiles = topic.JsonDataFiles;
                existing.IsActive = topic.IsActive;

                SyncShadowForeignKeys(context, existing);
                await context.SaveChangesAsync(currentUserId);
                targetTopic = existing;
            }

            // Sync Childs
            var existingChilds = await context.TopicChilds
                .Where(c => c.TopicId == targetTopic.Id && !c.IsDeleted)
                .ToListAsync();

            var incomingChildIds = childs.Where(c => c.Id > 0).Select(c => c.Id).ToHashSet();

            // Soft-delete removed childs
            foreach (var ec in existingChilds)
            {
                if (!incomingChildIds.Contains(ec.Id))
                {
                    ec.SoftDelete(currentUserId);
                }
            }

            // Add or update childs
            foreach (var c in childs)
            {
                if (c.Id > 0)
                {
                    var ec = existingChilds.FirstOrDefault(x => x.Id == c.Id);
                    if (ec != null)
                    {
                        ec.ActivityGroupId = c.ActivityGroupId;
                        ec.SourceId = c.SourceId;
                        ec.ActivityUnitId = c.ActivityUnitId;
                        ec.EmissionSharePercent = c.EmissionSharePercent;
                        ec.UnitConversionRatio = c.UnitConversionRatio ?? 1;
                        ec.IsActive = c.IsActive;
                    }
                }
                else
                {
                    var newChild = new TbActivityTopicChild
                    {
                        TopicId = targetTopic.Id,
                        ActivityGroupId = c.ActivityGroupId,
                        SourceId = c.SourceId,
                        ActivityUnitId = c.ActivityUnitId,
                        EmissionSharePercent = c.EmissionSharePercent,
                        UnitConversionRatio = c.UnitConversionRatio ?? 1,
                        IsActive = c.IsActive
                    };
                    context.TopicChilds.Add(newChild);
                }
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Activity topic and childs saved successfully: ID {Id}", targetTopic.Id);
            return targetTopic;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving activity topic with childs: {Title}", topic?.Title);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// เพิ่มรายการ Child Emission Sources ให้กับ Master Topic ที่มีอยู่แล้วในระบบ
    /// </summary>
    public async Task<List<TbActivityTopicChild>> AppendChildsToTopicAsync(int topicId, List<TbActivityTopicChild> newChilds)
    {
        #region hide [2026-08-29]
        if (topicId <= 0) throw new ArgumentException("Invalid topicId", nameof(topicId));
        if (newChilds == null || !newChilds.Any()) return new List<TbActivityTopicChild>();

        try
        {
            using var context = await CreateContextAsync();
            var currentUserId = await GetCurrentUserIdAsync();
            var topic = await context.Topics.FirstOrDefaultAsync(t => t.Id == topicId && !t.IsDeleted);
            if (topic == null)
            {
                throw new KeyNotFoundException($"Topic ID {topicId} not found.");
            }

            // [GHG] Child ต้องไม่ซ้ำคู่ (ActivityGroupId, SourceId) ภายในหัวข้อเดียวกัน
            // เพราะ EF ถูกบังคับผ่าน Emission Source Mapping อยู่แล้ว ถ้าปล่อยให้ต่อท้ายซ้ำ ค่า kgCO2e จะถูกนับซ้ำทันที
            var existingKeys = await context.TopicChilds
                .Where(c => c.TopicId == topicId && !c.IsDeleted)
                .Select(c => new { c.ActivityGroupId, c.SourceId })
                .ToListAsync();

            var childKeys = new HashSet<(int ActivityGroupId, int SourceId)>(
                existingKeys.Select(k => (k.ActivityGroupId, k.SourceId)));

            var addedChilds = new List<TbActivityTopicChild>();
            int skippedDuplicates = 0;

            foreach (var c in newChilds)
            {
                // กันซ้ำทั้งกับของเดิมในฐานข้อมูล และกับรายการที่ซ้ำกันเองภายในชุดที่ส่งเข้ามา
                if (!childKeys.Add((c.ActivityGroupId, c.SourceId)))
                {
                    skippedDuplicates++;
                    continue;
                }

                var newChild = new TbActivityTopicChild
                {
                    TopicId = topicId,
                    ActivityGroupId = c.ActivityGroupId,
                    SourceId = c.SourceId,
                    ActivityUnitId = c.ActivityUnitId,
                    EmissionSharePercent = c.EmissionSharePercent,
                    UnitConversionRatio = c.UnitConversionRatio ?? 1,
                    IsActive = c.IsActive
                };
                context.TopicChilds.Add(newChild);
                addedChilds.Add(newChild);
            }

            if (addedChilds.Count == 0)
            {
                _logger.LogInformation("No child appended to Topic ID {TopicId} — all {Skipped} entries already exist.", topicId, skippedDuplicates);
                return addedChilds;
            }

            await context.SaveChangesAsync(currentUserId);
            _logger.LogInformation("Appended {Count} childs to Topic ID {TopicId} (skipped {Skipped} duplicates)", addedChilds.Count, topicId, skippedDuplicates);
            return addedChilds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error appending childs to Topic ID: {TopicId}", topicId);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// สลับลำดับ OrderId ระหว่าง 2 หัวข้อกิจกรรม
    /// </summary>
    public async Task<bool> SwapTopicOrderAsync(int topicId1, int topicId2, string? currentUserId = null)
    {
        #region hide [2026-08-25]
        try
        {
            using var context = await CreateContextAsync();
            var item1 = await context.Topics.FindAsync(topicId1);
            var item2 = await context.Topics.FindAsync(topicId2);

            if (item1 == null || item2 == null) return false;

            // หากทั้ง 2 รายการมี OrderId เท่ากัน ให้กำหนดลำดับตาม Id ก่อนสลับ
            if (item1.OrderId == item2.OrderId)
            {
                if (item1.Id < item2.Id)
                {
                    item1.OrderId = item1.Id;
                    item2.OrderId = item2.Id;
                }
                else
                {
                    item1.OrderId = item1.Id;
                    item2.OrderId = item2.Id;
                }
            }

            var tempOrder = item1.OrderId;
            item1.OrderId = item2.OrderId;
            item2.OrderId = tempOrder;

            item1.UpdatedAt = DateTime.UtcNow;
            item2.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                item1.UpdatedBy = currentUserId;
                item2.UpdatedBy = currentUserId;
            }

            await context.SaveChangesAsync();
            _logger.LogInformation("Swapped OrderId between Topic {Id1} and {Id2}", topicId1, topicId2);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error swapping topic orders: {Id1} and {Id2}", topicId1, topicId2);
            throw;
        }
        #endregion
    }

    /// <summary>
    /// เขียนลำดับ OrderId ใหม่ทั้งชุด (1..N) ตามลำดับ Topic Id ที่ส่งเข้ามา
    /// ใช้กับการจัดเรียงแบบลาก-วาง (Drag & Drop) ภายใน Group Code เดียวกัน
    /// หมายเหตุ: ผู้เรียกต้องส่งเฉพาะ Topic ที่อยู่ใน Group Code เดียวกันเท่านั้น
    /// </summary>
    public async Task<bool> ReorderTopicsInGroupAsync(IList<int> orderedTopicIds, string? currentUserId = null)
    {
        #region hide [2026-09-11]
        try
        {
            if (orderedTopicIds == null || orderedTopicIds.Count == 0) return false;

            var ids = orderedTopicIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return false;

            using var context = await CreateContextAsync();
            var topics = await context.Topics
                .Where(t => ids.Contains(t.Id) && !t.IsDeleted)
                .ToListAsync();

            if (topics.Count == 0) return false;

            var now = AppTimeZone.UtcNow;
            var changed = 0;
            var order = 1;

            foreach (var id in ids)
            {
                var topic = topics.FirstOrDefault(t => t.Id == id);
                if (topic == null) continue;

                if (topic.OrderId != order)
                {
                    topic.OrderId = order;
                    topic.UpdatedAt = now;
                    if (!string.IsNullOrWhiteSpace(currentUserId))
                    {
                        topic.UpdatedBy = currentUserId;
                    }
                    changed++;
                }
                order++;
            }

            if (changed == 0) return true;

            await context.SaveChangesAsync();
            _logger.LogInformation("Reordered {Changed} of {Total} topics (OrderId 1..N)", changed, ids.Count);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering topics");
            throw;
        }
        #endregion
    }

    /// <summary>
    /// ดึงรายการ Topic แบบแบ่งหน้า โดยบังคับ Subscription และ Scope ของ API Key (ใช้กับ Public API v2)
    /// </summary>
    public async Task<(List<TbActivityTopic> Items, int TotalCount)> GetPagedForApiKeyScopeAsync(
        int subscriptionId,
        eTopicLevel? scopeLevel,
        int? scopeTargetId,
        eTopicLevel? level = null,
        int? targetId = null,
        string? search = null,
        bool includeInactive = false,
        int page = 1,
        int pageSize = 50)
    {
        // [2026-09-13][Claude] เพิ่มเมธอดนี้ให้ Topics API อ่านข้อมูลได้โดยบังคับขอบเขตของ API Key ตั้งแต่ชั้น query
        //     GetByContextAsync เดิมไม่บังคับ Scope และดึง Events มาด้วยทุกครั้ง ซึ่งหนักเกินไปสำหรับ endpoint แบบ list
        try
        {
            using var context = await CreateContextAsync();

            var query = context.Topics
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.SubscriptionId == subscriptionId);

            if (scopeLevel.HasValue && scopeTargetId.HasValue)
            {
                query = query.Where(t => t.Level == scopeLevel.Value && t.TargetId == scopeTargetId.Value);
            }

            if (level.HasValue)
            {
                query = query.Where(t => t.Level == level.Value);
            }

            if (targetId.HasValue && targetId.Value > 0)
            {
                query = query.Where(t => t.TargetId == targetId.Value);
            }

            if (!includeInactive)
            {
                query = query.Where(t => t.IsActive);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var keyword = search.Trim();
                query = query.Where(t => t.Title.Contains(keyword));
            }

            var totalCount = await query.CountAsync();

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            var items = await query
                .Include(t => t.EmissionGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.ActivityGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.ActivityUnit)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Unit)
                .OrderBy(t => t.OrderId)
                .ThenBy(t => t.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching scoped activity topics for subscription {SubscriptionId}.", subscriptionId);
            throw;
        }
    }

    /// <summary>
    /// ดึง Topic รายการเดียวโดยบังคับ Subscription และ Scope ของ API Key (ใช้กับ Public API v2)
    /// </summary>
    public async Task<TbActivityTopic?> GetForApiKeyScopeAsync(
        int id,
        int subscriptionId,
        eTopicLevel? scopeLevel,
        int? scopeTargetId)
    {
        // [2026-09-13][Claude] GetByIdAsync เดิมไม่ตรวจ SubscriptionId เลย ถ้า Topics API เรียกใช้ตรง ๆ
        //     ผู้ถือ Key จะอ่าน/แก้/ลบ Topic ข้าม Subscription ได้ทันทีที่เดา TopicKey ถูก
        try
        {
            using var context = await CreateContextAsync();

            var query = context.Topics
                .AsNoTracking()
                .Where(t => t.Id == id && !t.IsDeleted && t.SubscriptionId == subscriptionId);

            if (scopeLevel.HasValue && scopeTargetId.HasValue)
            {
                query = query.Where(t => t.Level == scopeLevel.Value && t.TargetId == scopeTargetId.Value);
            }

            return await query
                .Include(t => t.EmissionGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.ActivityGroup)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.ActivityUnit)
                .Include(t => t.Childs.Where(c => !c.IsDeleted))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Unit)
                .Include(t => t.Events.Where(e => !e.IsDeleted))
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching scoped activity topic ID {Id}.", id);
            throw;
        }
    }

    /// <summary>
    /// ตรวจว่ามี Topic ชื่อเดียวกันอยู่แล้วในขอบเขต (Subscription, Level, TargetId) หรือไม่
    /// </summary>
    public async Task<bool> TitleExistsForTargetAsync(
        int subscriptionId,
        eTopicLevel level,
        int targetId,
        string title,
        int excludeTopicId = 0)
    {
        // [2026-09-13][Claude] ใช้บังคับกฎ Topic Integrity ฝั่ง API: (Title, Level, TargetId) ซ้ำ = Already Exists ต้องข้าม
        //     ห้ามสร้างซ้ำและห้ามนำ Source ใหม่ไปต่อท้าย Topic เดิม
        try
        {
            var keyword = title?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keyword)) return false;

            using var context = await CreateContextAsync();

            return await context.Topics
                .AsNoTracking()
                .AnyAsync(t => !t.IsDeleted
                            && t.SubscriptionId == subscriptionId
                            && t.Level == level
                            && t.TargetId == targetId
                            && t.Id != excludeTopicId
                            && t.Title.ToLower() == keyword.ToLower());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking duplicated topic title for target {TargetId}.", targetId);
            throw;
        }
    }

    #endregion
}
