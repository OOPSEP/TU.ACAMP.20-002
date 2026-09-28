// [2026-09-25][Antigravity] Service สำหรับประเมินและส่งอีเมลแจ้งเตือนเมื่อค่า GHG ประจำปีเข้าใกล้หรือเกินเป้าหมาย (>80%)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Identity;
using TU.ACAMP.Domain.Models.Organization;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;
using TU.ACAMP.WebApp.Services.Activity;
using TU.ACAMP.WebApp.Services.Identity;

namespace TU.ACAMP.WebApp.Services.Dashboard;

public class GhgTargetAlertService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly EmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GhgTargetAlertService> _logger;

    public GhgTargetAlertService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        EmailSender emailSender,
        IConfiguration configuration,
        ILogger<GhgTargetAlertService> logger)
    {
        _dbFactory = dbFactory;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// ตรวจสอบยอดปล่อย GHG ประจำปีเทียบกับ Annual Net-Zero Trajectory Cap
    /// หากยอดปล่อยสะสมแตะเกณฑ์เตือน (>= 80% หรือตามที่กำหนด) และไม่ติด Cooldown จะทำการส่งอีเมลแจ้งเตือน
    /// </summary>
    public async Task<int> CheckAndSendAlertsAsync(
        int? specificSubscriptionId = null,
        int? reportingYear = null,
        CancellationToken ct = default)
    {
        int alertCount = 0;
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync(ct);

            var currentYear = reportingYear ?? AppTimeZone.LocalNow.Year;

            // 1. ดึงเป้าหมาย Net-Zero ที่เปิดใช้งานการแจ้งเตือน (EnableAlert == true)
            var targetsQuery = db.NetZeroTargets
                .Include(t => t.Subscription)
                .Include(t => t.Corporate)
                .Where(t => !t.IsDeleted && t.EnableAlert);

            if (specificSubscriptionId.HasValue && specificSubscriptionId.Value > 0)
            {
                targetsQuery = targetsQuery.Where(t => t.SubscriptionId == specificSubscriptionId.Value);
            }

            var targets = await targetsQuery.ToListAsync(ct);

            if (!targets.Any() && specificSubscriptionId.HasValue)
            {
                // Fallback: หาก Subscription ยังไม่มีบันทึก TbNetZeroTarget ในตาราง ให้ใช้ค่า default
                var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == specificSubscriptionId.Value && !s.IsDeleted, ct);
                if (sub != null)
                {
                    targets.Add(new TbNetZeroTarget
                    {
                        SubscriptionId = sub.Id,
                        Subscription = sub,
                        BaseYear = 2023,
                        TargetYear = 2030,
                        TargetReductionPercent = 50.0m,
                        EnableAlert = true,
                        AlertThresholdPercent = 80.0m
                    });
                }
            }

            foreach (var target in targets)
            {
                if (ct.IsCancellationRequested) break;

                var sent = await EvaluateAndAlertTargetAsync(db, target, currentYear, ct);
                if (sent) alertCount++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while executing CheckAndSendAlertsAsync.");
        }

        return alertCount;
    }

    /// <summary>
    /// ประเมินเป้าหมายและคำนวณสัดส่วนการใช้โควตาปล่อยก๊าซขององค์กร
    /// </summary>
    private async Task<bool> EvaluateAndAlertTargetAsync(
        ApplicationDbContext db,
        TbNetZeroTarget target,
        int reportingYear,
        CancellationToken ct)
    {
        try
        {
            // 1. เตรียมขอบเขต Topics ภายใต้ Subscription และ Corporate (ถ้ามี)
            var topicsQuery = db.Topics
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.SubscriptionId == target.SubscriptionId);

            if (target.CorporateId.HasValue && target.CorporateId.Value > 0)
            {
                var corpId = target.CorporateId.Value;
                var corpCompanyIds = await db.Companies
                    .Where(c => !c.IsDeleted && c.CorporateId == corpId)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                var facilityIds = await db.Facilities
                    .Where(f => !f.IsDeleted && corpCompanyIds.Contains(f.CompanyId))
                    .Select(f => f.Id)
                    .ToListAsync(ct);

                topicsQuery = topicsQuery.Where(t =>
                    (t.Level == eTopicLevel.Corporate && t.TargetId == corpId) ||
                    (t.Level == eTopicLevel.Company && corpCompanyIds.Contains(t.TargetId)) ||
                    (t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId)));
            }

            var inScopeTopics = await topicsQuery
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.ActivityGroup)
                .ToListAsync(ct);

            if (!inScopeTopics.Any())
            {
                return false;
            }

            var inScopeTopicIds = inScopeTopics.Select(t => t.Id).ToHashSet();
            var inScopeTopicDict = inScopeTopics.ToDictionary(t => t.Id, t => t);

            // 2. ดึงผลรวม Activity Events ของ BaseYear และ ReportingYear
            var baseYear = target.BaseYear;
            var minYear = Math.Min(baseYear, reportingYear);
            var fromDate = new DateTime(minYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var toDate = new DateTime(Math.Max(baseYear, reportingYear) + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

            var eventAggs = await db.Events
                .AsNoTracking()
                .Where(e => !e.IsDeleted &&
                            e.SubscriptionId == target.SubscriptionId &&
                            inScopeTopicIds.Contains(e.TopicId) &&
                            e.TimeStamp >= fromDate &&
                            e.TimeStamp < toDate)
                .GroupBy(e => new
                {
                    e.TopicId,
                    Year = e.TimeStamp.Year,
                    Month = e.TimeStamp.Month
                })
                .Select(g => new
                {
                    TopicId = g.Key.TopicId,
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalQuantity = g.Sum(e => e.Quantity)
                })
                .ToListAsync(ct);

            // 3. คำนวณยอดปล่อยปีฐาน (Base Year Emissions) โดยใช้ EmissionCalculator.CalculateKgCO2e [GHG-15]
            decimal baseYearTotalKg = 0m;
            var baseMonthlyKg = new decimal[13];
            var baseAggs = eventAggs.Where(a => a.Year == baseYear).ToList();
            foreach (var agg in baseAggs)
            {
                if (inScopeTopicDict.TryGetValue(agg.TopicId, out var topic))
                {
                    foreach (var child in topic.Childs)
                    {
                        var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                        if (scope != eTgoScopeNo.SeparateReport)
                        {
                            var kg = EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                            baseYearTotalKg += kg;
                            if (agg.Month >= 1 && agg.Month <= 12) baseMonthlyKg[agg.Month] += kg;
                        }
                    }
                }
            }

            decimal baseYearEmissionsTon = baseYearTotalKg / 1000m;
            if (baseYearEmissionsTon <= 0m)
            {
                _logger.LogInformation("Net-Zero Target evaluation skipped for Subscription {SubId}: Base Year {Year} has 0 emissions.",
                    target.SubscriptionId, baseYear);
                return false;
            }

            // 4. คำนวณยอดปล่อยปีปัจจุบันสะสม (Current Year Emissions) แยกตาม Scope และรายเดือน
            decimal currentScope1Kg = 0m;
            decimal currentScope2Kg = 0m;
            decimal currentScope3Kg = 0m;
            var currentMonthlyKg = new decimal[13];
            var currentAggs = eventAggs.Where(a => a.Year == reportingYear).ToList();

            foreach (var agg in currentAggs)
            {
                if (inScopeTopicDict.TryGetValue(agg.TopicId, out var topic))
                {
                    foreach (var child in topic.Childs)
                    {
                        var kg = EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                        var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;

                        switch (scope)
                        {
                            case eTgoScopeNo.Scope1:
                                currentScope1Kg += kg;
                                break;
                            case eTgoScopeNo.Scope2:
                                currentScope2Kg += kg;
                                break;
                            case eTgoScopeNo.Scope3:
                                currentScope3Kg += kg;
                                break;
                        }

                        if (agg.Month >= 1 && agg.Month <= 12 && scope != eTgoScopeNo.SeparateReport)
                        {
                            currentMonthlyKg[agg.Month] += kg;
                        }
                    }
                }
            }

            decimal currentTotalKg = currentScope1Kg + currentScope2Kg + currentScope3Kg;
            decimal currentEmissionsTon = currentTotalKg / 1000m;

            // 5. คำนวณ Annual Target Emission Cap ตามสูตร Linear Net-Zero Trajectory
            decimal elapsedYears = Math.Max(0, reportingYear - baseYear);
            decimal totalTargetYears = Math.Max(1, target.TargetYear - baseYear);
            decimal targetRatioProgress = Math.Min(1.0m, elapsedYears / totalTargetYears);
            decimal currentTargetReductionRatio = 1.0m - ((target.TargetReductionPercent / 100.0m) * targetRatioProgress);
            decimal annualTargetCapTon = baseYearEmissionsTon * currentTargetReductionRatio;

            if (annualTargetCapTon <= 0m) return false;

            decimal consumptionRatio = currentEmissionsTon / annualTargetCapTon;
            decimal consumptionPercent = consumptionRatio * 100.0m;

            // 6. ตรวจสอบเงื่อนไขการแจ้งเตือน (Alert Threshold Evaluation: ทั้ง Annual Cap และ Monthly Trajectory > 80%)
            var thresholdPercent = target.AlertThresholdPercent > 0m ? target.AlertThresholdPercent : 80.0m;
            var thresholdRatio = thresholdPercent / 100.0m;

            var warningMonths = new List<string>();
            string[] monthNames = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
            for (int m = 1; m <= 12; m++)
            {
                decimal mTargetTon = (baseMonthlyKg[m] / 1000m) * currentTargetReductionRatio;
                decimal mActualTon = currentMonthlyKg[m] / 1000m;
                if (mTargetTon > 0 && mActualTon >= mTargetTon * thresholdRatio)
                {
                    warningMonths.Add(monthNames[m - 1]);
                }
            }

            bool isAnnualAlert = consumptionPercent >= thresholdPercent;
            bool isMonthlyAlert = warningMonths.Any();

            if (!isAnnualAlert && !isMonthlyAlert)
            {
                // ยังไม่ถึงเกณฑ์เตือนทั้งแบบรายปีและรายเดือน
                return false;
            }

            // กำหนดระดับ Tier ของการแจ้งเตือน
            string alertTier = consumptionPercent >= 100.0m ? "Exceeded_100" : "Warning_80";

            // 7. ระบบป้องกันอีเมลสแปม / Cooldown Logic
            var lastLog = await db.GhgTargetAlertLogs
                .Where(l => !l.IsDeleted &&
                            l.SubscriptionId == target.SubscriptionId &&
                            l.CorporateId == target.CorporateId &&
                            l.ReportingYear == reportingYear &&
                            l.DeliveryStatus == "Sent")
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (lastLog != null)
            {
                var timeSinceLastAlert = AppTimeZone.UtcNow - lastLog.CreatedAt;

                // หากระดับเดิมเป็น Exceeded_100 อยู่แล้ว และเพิ่งส่งไปไม่เกิน 7 วัน -> ข้ามการส่งซ้ำ
                if (lastLog.AlertTier == "Exceeded_100" && timeSinceLastAlert.TotalDays < 7)
                {
                    return false;
                }

                // หากระดับเดิมเป็น Warning_80 และยังเป็น Warning_80 อยู่ และเพิ่งส่งไปไม่เกิน 7 วัน -> ข้ามการส่งซ้ำ
                if (lastLog.AlertTier == "Warning_80" && alertTier == "Warning_80" && timeSinceLastAlert.TotalDays < 7)
                {
                    return false;
                }

                // หากยอดปล่อยเพิ่มขึ้นเพียงเล็กน้อย (< 2% เพิ่มเติม) ภายใน 3 วัน -> ข้าม
                if (Math.Abs(consumptionPercent - lastLog.ConsumptionPercent) < 2.0m && timeSinceLastAlert.TotalDays < 3)
                {
                    return false;
                }
            }

            // 8. รวบรวมรายชื่อผู้รับอีเมล (Recipients)
            var recipientList = await ResolveRecipientsAsync(db, target, ct);
            if (!recipientList.Any())
            {
                _logger.LogWarning("No valid email recipients found for Net-Zero Target alert on Subscription {SubId}", target.SubscriptionId);
                return false;
            }

            // 9. เตรียมข้อมูลสำหรับส่งอีเมล
            string orgName = target.Corporate?.Title ?? target.Subscription?.Title ?? "Subscription";
            var baseUrl = _configuration["App:BaseUrl"] ?? "https://carbon-one.acamp.local";
            var dashboardUrl = $"{baseUrl.TrimEnd('/')}/dashboard";

            var emailModel = new EmailSender.GhgTargetAlertEmailModel
            {
                OrganizationName = orgName,
                ReportingYear = reportingYear,
                BaseYear = baseYear,
                BaseYearEmissionsTon = baseYearEmissionsTon,
                AnnualTargetCapTon = annualTargetCapTon,
                CurrentEmissionsTon = currentEmissionsTon,
                ConsumptionPercent = consumptionPercent,
                Scope1Ton = currentScope1Kg / 1000m,
                Scope2Ton = currentScope2Kg / 1000m,
                Scope3Ton = currentScope3Kg / 1000m,
                AlertTier = alertTier,
                DashboardUrl = dashboardUrl
            };

            // 10. ส่งอีเมลให้กับผู้รับทุกคน
            var joinedRecipients = string.Join(", ", recipientList);
            string deliveryStatus = "Sent";
            string? errorMessage = null;

            foreach (var email in recipientList)
            {
                try
                {
                    await _emailSender.SendGhgTargetAlertEmailAsync(email, emailModel);
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Error sending GHG target alert email to {Email}", email);
                    deliveryStatus = "Failed";
                    errorMessage = sendEx.Message;
                }
            }

            // 11. บันทึกประวัติการส่งลงในตาราง TbGhgTargetAlertLog
            var alertLog = new TbGhgTargetAlertLog
            {
                SubscriptionId = target.SubscriptionId,
                CorporateId = target.CorporateId,
                ReportingYear = reportingYear,
                CurrentEmissionsTon = currentEmissionsTon,
                TargetCapTon = annualTargetCapTon,
                ConsumptionPercent = consumptionPercent,
                RecipientEmails = joinedRecipients,
                AlertTier = alertTier,
                DeliveryStatus = deliveryStatus,
                ErrorMessage = errorMessage
            };

            db.GhgTargetAlertLogs.Add(alertLog);

            // อัปเดตข้อมูลบน TbNetZeroTarget
            if (target.Id > 0)
            {
                var targetEntity = await db.NetZeroTargets.FirstOrDefaultAsync(t => t.Id == target.Id, ct);
                if (targetEntity != null)
                {
                    targetEntity.LastAlertSentAt = AppTimeZone.UtcNow;
                    targetEntity.LastAlertYear = reportingYear;
                    targetEntity.LastAlertRatio = consumptionRatio;
                    db.NetZeroTargets.Update(targetEntity);
                }
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Successfully recorded GHG target alert log for Subscription {SubId} ({Tier}: {Percent:F1}%)",
                target.SubscriptionId, alertTier, consumptionPercent);

            return deliveryStatus == "Sent";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate and send GHG target alert for Subscription {SubId}", target.SubscriptionId);
            return false;
        }
    }

    /// <summary>
    /// ส่งอีเมลทดสอบการแจ้งเตือน (Test Email) ไปยังที่อยู่ที่ระบุทันที
    /// </summary>
    public async Task<bool> SendTestAlertAsync(
        int subscriptionId,
        int? corporateId,
        string testEmail,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testEmail))
            throw new ArgumentException("Recipient email address cannot be empty.", nameof(testEmail));

        using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted, ct);
        var corp = corporateId.HasValue ? await db.Corporates.FirstOrDefaultAsync(c => c.Id == corporateId.Value && !c.IsDeleted, ct) : null;
        var orgName = corp?.Title ?? sub?.Title ?? "Test Organization";

        var currentYear = AppTimeZone.LocalNow.Year;
        var baseUrl = _configuration["App:BaseUrl"] ?? "https://carbon-one.acamp.local";

        var emailModel = new EmailSender.GhgTargetAlertEmailModel
        {
            OrganizationName = orgName,
            ReportingYear = currentYear,
            BaseYear = 2023,
            BaseYearEmissionsTon = 1250.0m,
            AnnualTargetCapTon = 1000.0m,
            CurrentEmissionsTon = 845.5m,
            ConsumptionPercent = 84.55m,
            Scope1Ton = 320.0m,
            Scope2Ton = 410.5m,
            Scope3Ton = 115.0m,
            AlertTier = "Test",
            DashboardUrl = $"{baseUrl.TrimEnd('/')}/dashboard"
        };

        try
        {
            await _emailSender.SendGhgTargetAlertEmailAsync(testEmail.Trim(), emailModel);

            // บันทึก Log สำหรับการทดสอบ
            var log = new TbGhgTargetAlertLog
            {
                SubscriptionId = subscriptionId,
                CorporateId = corporateId,
                ReportingYear = currentYear,
                CurrentEmissionsTon = emailModel.CurrentEmissionsTon,
                TargetCapTon = emailModel.AnnualTargetCapTon,
                ConsumptionPercent = emailModel.ConsumptionPercent,
                RecipientEmails = testEmail.Trim(),
                AlertTier = "Test",
                DeliveryStatus = "Sent"
            };
            db.GhgTargetAlertLogs.Add(log);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send test alert email to {Email}", testEmail);

            var log = new TbGhgTargetAlertLog
            {
                SubscriptionId = subscriptionId,
                CorporateId = corporateId,
                ReportingYear = currentYear,
                CurrentEmissionsTon = emailModel.CurrentEmissionsTon,
                TargetCapTon = emailModel.AnnualTargetCapTon,
                ConsumptionPercent = emailModel.ConsumptionPercent,
                RecipientEmails = testEmail.Trim(),
                AlertTier = "Test",
                DeliveryStatus = "Failed",
                ErrorMessage = ex.Message
            };
            db.GhgTargetAlertLogs.Add(log);
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// [2026-09-25][Gemini] ค้นหาและรวบรวมรายชื่อผู้รับอีเมล: Subscription Owner + สมาชิกทุกคนใน Subscription (All Subscription Members) + AlertEmails
    /// </summary>
    private async Task<List<string>> ResolveRecipientsAsync(
        ApplicationDbContext db,
        TbNetZeroTarget target,
        CancellationToken ct)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. ดึง Subscription Owner Email
        var sub = target.Subscription ?? await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == target.SubscriptionId && !s.IsDeleted, ct);
        if (sub != null && !string.IsNullOrWhiteSpace(sub.OwnerEmail) && sub.OwnerEmail.Contains('@'))
        {
            recipients.Add(sub.OwnerEmail.Trim());
        }

        // 2. ดึงสมาชิกทุกคนใน Subscription (All Active Subscription Members)
        var subUsers = await db.SubscriptionUsers
            .Where(u => !u.IsDeleted && u.IsActive && u.SubscriptionId == target.SubscriptionId)
            .Select(u => u.Email)
            .ToListAsync(ct);

        foreach (var uEmail in subUsers)
        {
            if (!string.IsNullOrWhiteSpace(uEmail) && uEmail.Contains('@'))
            {
                recipients.Add(uEmail.Trim());
            }
        }

        // 3. รวมอีเมลเพิ่มเติมที่ระบุไว้ใน AlertEmails (ถ้ามี)
        if (!string.IsNullOrWhiteSpace(target.AlertEmails))
        {
            var rawEmails = target.AlertEmails.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var email in rawEmails)
            {
                var trimmed = email.Trim();
                if (trimmed.Contains('@') && trimmed.Contains('.'))
                {
                    recipients.Add(trimmed);
                }
            }
        }

        return recipients.ToList();
    }

    /// <summary>
    /// [2026-09-25][Gemini] ส่งอีเมลแจ้งเตือนสถานะ Monthly GHG Trajectory & Net-Zero Pathway (>80%) ไปยังสมาชิกทุกคนใน Subscription ทันที
    /// </summary>
    public async Task<(bool Success, int RecipientCount, string Message)> NotifyAllSubscriptionMembersAsync(
        int subscriptionId,
        int? corporateId = null,
        int? reportingYear = null,
        CancellationToken ct = default)
    {
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync(ct);
            var currentYear = reportingYear ?? AppTimeZone.LocalNow.Year;

            var target = await db.NetZeroTargets
                .Include(t => t.Subscription)
                .Include(t => t.Corporate)
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.SubscriptionId == subscriptionId && t.CorporateId == corporateId, ct);

            if (target == null)
            {
                var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId && !s.IsDeleted, ct);
                if (sub == null)
                {
                    return (false, 0, $"Subscription ID {subscriptionId} not found.");
                }

                target = new TbNetZeroTarget
                {
                    SubscriptionId = sub.Id,
                    Subscription = sub,
                    CorporateId = corporateId,
                    BaseYear = 2023,
                    TargetYear = 2030,
                    TargetReductionPercent = 50.0m,
                    EnableAlert = true,
                    AlertThresholdPercent = 80.0m
                };
            }

            var recipients = await ResolveRecipientsAsync(db, target, ct);
            if (!recipients.Any())
            {
                return (false, 0, "No email recipients found for this subscription. Please add member emails or configure Alert Recipients in Pathway Settings.");
            }

            // คำนวณ emissions สำหรับ reportingYear
            var topicsQuery = db.Topics
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.SubscriptionId == target.SubscriptionId);

            if (target.CorporateId.HasValue && target.CorporateId.Value > 0)
            {
                var corpId = target.CorporateId.Value;
                var corpCompanyIds = await db.Companies
                    .Where(c => !c.IsDeleted && c.CorporateId == corpId)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                var facilityIds = await db.Facilities
                    .Where(f => !f.IsDeleted && corpCompanyIds.Contains(f.CompanyId))
                    .Select(f => f.Id)
                    .ToListAsync(ct);

                topicsQuery = topicsQuery.Where(t =>
                    (t.Level == eTopicLevel.Corporate && t.TargetId == corpId) ||
                    (t.Level == eTopicLevel.Company && corpCompanyIds.Contains(t.TargetId)) ||
                    (t.Level == eTopicLevel.Facility && facilityIds.Contains(t.TargetId)));
            }

            var inScopeTopics = await topicsQuery
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.Source)
                        .ThenInclude(s => s!.Group)
                .Include(t => t.Childs.Where(c => !c.IsDeleted && c.IsActive))
                    .ThenInclude(c => c.ActivityGroup)
                .ToListAsync(ct);

            var inScopeTopicIds = inScopeTopics.Select(t => t.Id).ToHashSet();
            var inScopeTopicDict = inScopeTopics.ToDictionary(t => t.Id, t => t);

            var baseYear = target.BaseYear;
            var minYear = Math.Min(baseYear, currentYear);
            var fromDate = new DateTime(minYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            var toDate = new DateTime(Math.Max(baseYear, currentYear) + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

            var eventAggs = await db.Events
                .AsNoTracking()
                .Where(e => !e.IsDeleted &&
                            e.SubscriptionId == target.SubscriptionId &&
                            inScopeTopicIds.Contains(e.TopicId) &&
                            e.TimeStamp >= fromDate &&
                            e.TimeStamp < toDate)
                .GroupBy(e => new
                {
                    e.TopicId,
                    Year = e.TimeStamp.Year,
                    Month = e.TimeStamp.Month
                })
                .Select(g => new
                {
                    TopicId = g.Key.TopicId,
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    TotalQuantity = g.Sum(e => e.Quantity)
                })
                .ToListAsync(ct);

            decimal baseYearTotalKg = 0m;
            foreach (var agg in eventAggs.Where(a => a.Year == baseYear))
            {
                if (inScopeTopicDict.TryGetValue(agg.TopicId, out var topic))
                {
                    foreach (var child in topic.Childs)
                    {
                        var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                        if (scope != eTgoScopeNo.SeparateReport)
                        {
                            baseYearTotalKg += EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                        }
                    }
                }
            }

            decimal baseYearEmissionsTon = baseYearTotalKg / 1000m;

            decimal currentScope1Kg = 0m, currentScope2Kg = 0m, currentScope3Kg = 0m;
            foreach (var agg in eventAggs.Where(a => a.Year == currentYear))
            {
                if (inScopeTopicDict.TryGetValue(agg.TopicId, out var topic))
                {
                    foreach (var child in topic.Childs)
                    {
                        var kg = EmissionCalculator.CalculateKgCO2e(agg.TotalQuantity, child);
                        var scope = child.ActivityGroup?.TgoScopeNo ?? child.Source?.Group?.TgoScopeNo ?? eTgoScopeNo.Scope1;
                        switch (scope)
                        {
                            case eTgoScopeNo.Scope1: currentScope1Kg += kg; break;
                            case eTgoScopeNo.Scope2: currentScope2Kg += kg; break;
                            case eTgoScopeNo.Scope3: currentScope3Kg += kg; break;
                        }
                    }
                }
            }

            decimal currentEmissionsTon = (currentScope1Kg + currentScope2Kg + currentScope3Kg) / 1000m;

            decimal elapsedYears = Math.Max(0, currentYear - baseYear);
            decimal totalTargetYears = Math.Max(1, target.TargetYear - baseYear);
            decimal targetRatioProgress = Math.Min(1.0m, elapsedYears / totalTargetYears);
            decimal currentTargetReductionRatio = 1.0m - ((target.TargetReductionPercent / 100.0m) * targetRatioProgress);
            decimal annualTargetCapTon = baseYearEmissionsTon * currentTargetReductionRatio;

            decimal consumptionPercent = annualTargetCapTon > 0 ? (currentEmissionsTon / annualTargetCapTon) * 100.0m : 0m;

            string alertTier = consumptionPercent >= 100.0m ? "Exceeded_100" : (consumptionPercent >= (target.AlertThresholdPercent > 0 ? target.AlertThresholdPercent : 80.0m) ? "Warning_80" : "Trajectory_Update");

            string orgName = target.Corporate?.Title ?? target.Subscription?.Title ?? "Subscription Organization";
            var baseUrl = _configuration["App:BaseUrl"] ?? "https://carbon-one.acamp.local";
            var dashboardUrl = $"{baseUrl.TrimEnd('/')}/dashboard";

            var emailModel = new EmailSender.GhgTargetAlertEmailModel
            {
                OrganizationName = orgName,
                ReportingYear = currentYear,
                BaseYear = baseYear,
                BaseYearEmissionsTon = baseYearEmissionsTon,
                AnnualTargetCapTon = annualTargetCapTon > 0 ? annualTargetCapTon : (baseYearEmissionsTon > 0 ? baseYearEmissionsTon : 1000m),
                CurrentEmissionsTon = currentEmissionsTon,
                ConsumptionPercent = consumptionPercent,
                Scope1Ton = currentScope1Kg / 1000m,
                Scope2Ton = currentScope2Kg / 1000m,
                Scope3Ton = currentScope3Kg / 1000m,
                AlertTier = alertTier,
                DashboardUrl = dashboardUrl
            };

            int sentCount = 0;
            string? lastError = null;

            foreach (var email in recipients)
            {
                try
                {
                    await _emailSender.SendGhgTargetAlertEmailAsync(email, emailModel);
                    sentCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send trajectory alert email to {Email}", email);
                    lastError = ex.Message;
                }
            }

            var log = new TbGhgTargetAlertLog
            {
                SubscriptionId = subscriptionId,
                CorporateId = corporateId,
                ReportingYear = currentYear,
                CurrentEmissionsTon = currentEmissionsTon,
                TargetCapTon = emailModel.AnnualTargetCapTon,
                ConsumptionPercent = consumptionPercent,
                RecipientEmails = string.Join(", ", recipients),
                AlertTier = alertTier,
                DeliveryStatus = sentCount > 0 ? "Sent" : "Failed",
                ErrorMessage = lastError
            };
            db.GhgTargetAlertLogs.Add(log);

            if (target.Id > 0)
            {
                var targetEntity = await db.NetZeroTargets.FirstOrDefaultAsync(t => t.Id == target.Id, ct);
                if (targetEntity != null)
                {
                    targetEntity.LastAlertSentAt = AppTimeZone.UtcNow;
                    targetEntity.LastAlertYear = currentYear;
                    targetEntity.LastAlertRatio = annualTargetCapTon > 0 ? (currentEmissionsTon / annualTargetCapTon) : 0m;
                    db.NetZeroTargets.Update(targetEntity);
                }
            }

            await db.SaveChangesAsync(ct);

            if (sentCount > 0)
            {
                return (true, sentCount, $"Successfully sent GHG Net-Zero trajectory notification to {sentCount} subscription member(s).");
            }
            else
            {
                return (false, 0, $"Failed to deliver email: {lastError ?? "Unknown error"}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing NotifyAllSubscriptionMembersAsync for subscription {SubId}", subscriptionId);
            return (false, 0, ex.Message);
        }
    }
}
