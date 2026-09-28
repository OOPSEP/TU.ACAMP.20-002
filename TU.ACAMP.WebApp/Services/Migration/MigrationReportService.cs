using Microsoft.EntityFrameworkCore;
using TU.ACAMP.Domain.Enums;
using TU.ACAMP.Domain.Tools;
using TU.ACAMP.WebApp.Data;

namespace TU.ACAMP.WebApp.Services.Migration;

/// <summary>
/// [2026-09-23][Claude] รายงานผลการ migrate (อ่านอย่างเดียว) ของ 1 Subscription สำหรับตรวจเทียบกับ V16
/// Emission Units / Groups / Sources (Default + Custom) และโครงสร้าง Corporate -> Company -> Facility พร้อม Code (V16-ORG/SUB/FAC-{id}), Key สาธารณะ และจำนวน Topic / Event
/// includeTopics = true จะแนบรายการ Topic ของแต่ละระดับ พร้อมจำนวน Event, ผลรวม Quantity และช่วงเดือน
/// เขียน query แบบ provider-agnostic (ไม่มี SQL เฉพาะ MySQL/PostgreSQL); soft delete ถูกกรองโดย global query filter
/// </summary>
public class MigrationReportService
{
    #region class field
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    #endregion

    #region class constructor
    public MigrationReportService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        #region hide [2026-09-23]
        _dbFactory = dbFactory;
        #endregion
    }
    #endregion

    #region contracts
    public sealed record TopicReport(string TopicKey, string Title, bool IsActive, int EventCount, decimal TotalQuantity, string? FirstPeriod, string? LastPeriod);

    public sealed record FacilityReport(string FacilityKey, string Code, string Title, bool IsActive, int TopicCount, int EventCount, List<TopicReport>? Topics);

    public sealed record CompanyReport(string CompanyKey, string Code, string Title, float EquityShare, bool IsActive, int TopicCount, int EventCount, List<TopicReport>? Topics, List<FacilityReport> Facilities);

    public sealed record CorporateReport(string CorporateKey, string Code, string Title, bool IsActive, int TopicCount, int EventCount, List<TopicReport>? Topics, List<CompanyReport> Companies);

    public sealed record UnitReport(string UnitKey, string Title, string Remark, string SourceType, bool IsActive);

    public sealed record GroupReport(string GroupKey, string Code, string Title, int Scope, string Remark, string SourceType, bool IsActive);

    /// <summary>[2026-09-23][Claude] Emission Source + ค่าก๊าซ/GWP สำหรับเทียบค่า EF กับ V16</summary>
    public sealed record SourceReport(string SourceKey, string Title, string GroupKey, int Scope, string UnitKey,
        decimal CO2, decimal FossilCH4, decimal CH4, decimal N2O, decimal SF6, decimal NF3, decimal HFCs, decimal PFCs,
        decimal GwpCO2, decimal GwpFossilCH4, decimal GwpCH4, decimal GwpN2O, decimal GwpSF6, decimal GwpNF3, decimal GwpHFCs, decimal GwpPFCs,
        string GwpVersion, decimal Ef, string Ref, string Remark, string SourceType, bool IsActive);

    public sealed record ReportTotals(int Corporates, int Companies, int Facilities, int Topics, int Events, decimal TotalQuantity, int CustomUnits, int CustomGroups, int CustomSources);

    public sealed record SubscriptionReport(int SubscriptionId, string Title, string Plan, DateTime StartDate, DateTime ExpiryDate, string Remarks, ReportTotals Totals, List<UnitReport> Units, List<GroupReport> Groups, List<SourceReport> Sources, List<CorporateReport> Corporates);
    #endregion

    #region methods
    /// <summary>คืน null เมื่อไม่พบ Subscription (หรือถูกลบ)</summary>
    public async Task<SubscriptionReport?> GetReportAsync(int subscriptionId, bool includeTopics, CancellationToken ct = default)
    {
        #region hide [2026-09-23]
        using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Id == subscriptionId && !s.IsDeleted)
            .Select(s => new { s.Id, s.Title, s.SubscriptionPlan, s.StartDate, s.ExpiryDate, s.Remarks })
            .FirstOrDefaultAsync(ct);
        if (sub == null) return null;

        var corporates = await db.Corporates.AsNoTracking()
            .Where(c => c.SubscriptionId == subscriptionId && !c.IsDeleted)
            .OrderBy(c => c.OrderId).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.Code, c.Title, c.IsActive })
            .ToListAsync(ct);
        var corporateIds = corporates.Select(c => c.Id).ToList();

        var companies = await db.Companies.AsNoTracking()
            .Where(c => corporateIds.Contains(c.CorporateId) && !c.IsDeleted)
            .OrderBy(c => c.OrderId).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.CorporateId, c.Code, c.Title, c.EquityShare, c.IsActive })
            .ToListAsync(ct);
        var companyIds = companies.Select(c => c.Id).ToList();

        var facilities = await db.Facilities.AsNoTracking()
            .Where(f => companyIds.Contains(f.CompanyId) && !f.IsDeleted)
            .OrderBy(f => f.OrderId).ThenBy(f => f.Id)
            .Select(f => new { f.Id, f.CompanyId, f.Code, f.Title, f.IsActive })
            .ToListAsync(ct);

        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.SubscriptionId == subscriptionId && !t.IsDeleted)
            .OrderBy(t => t.OrderId).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Level, t.TargetId, t.Title, t.IsActive })
            .ToListAsync(ct);

        var eventStats = await db.Events.AsNoTracking()
            .Where(e => e.SubscriptionId == subscriptionId && !e.IsDeleted)
            .GroupBy(e => e.TopicId)
            .Select(g => new
            {
                TopicId = g.Key,
                Count = g.Count(),
                Total = g.Sum(e => e.Quantity),
                First = g.Min(e => e.TimeStamp),
                Last = g.Max(e => e.TimeStamp)
            })
            .ToListAsync(ct);
        var statsByTopic = eventStats.ToDictionary(x => x.TopicId);

        TopicReport ToTopicReport(int id, string title, bool isActive)
        {
            statsByTopic.TryGetValue(id, out var st);
            return new TopicReport(
                PublicKeyCodec.Encode(PublicKeyCodec.TopicPrefix, id),
                title,
                isActive,
                st?.Count ?? 0,
                st?.Total ?? 0m,
                st?.First.ToString("yyyy-MM"),
                st?.Last.ToString("yyyy-MM"));
        }

        // Topic ของแต่ละเป้าหมาย (Level + TargetId)
        var topicsByTarget = topics
            .GroupBy(t => (t.Level, t.TargetId))
            .ToDictionary(g => g.Key, g => g.Select(t => ToTopicReport(t.Id, t.Title, t.IsActive)).ToList());

        List<TopicReport> TopicsOf(eTopicLevel level, int targetId) =>
            topicsByTarget.TryGetValue((level, targetId), out var list) ? list : new List<TopicReport>();

        var corporateReports = corporates.Select(corp =>
        {
            var corpTopics = TopicsOf(eTopicLevel.Corporate, corp.Id);
            var companyReports = companies.Where(c => c.CorporateId == corp.Id).Select(comp =>
            {
                var compTopics = TopicsOf(eTopicLevel.Company, comp.Id);
                var facilityReports = facilities.Where(f => f.CompanyId == comp.Id).Select(fac =>
                {
                    var facTopics = TopicsOf(eTopicLevel.Facility, fac.Id);
                    return new FacilityReport(
                        PublicKeyCodec.Encode(PublicKeyCodec.FacilityPrefix, fac.Id),
                        fac.Code, fac.Title, fac.IsActive,
                        facTopics.Count, facTopics.Sum(t => t.EventCount),
                        includeTopics ? facTopics : null);
                }).ToList();

                return new CompanyReport(
                    PublicKeyCodec.Encode(PublicKeyCodec.CompanyPrefix, comp.Id),
                    comp.Code, comp.Title, comp.EquityShare, comp.IsActive,
                    compTopics.Count, compTopics.Sum(t => t.EventCount),
                    includeTopics ? compTopics : null,
                    facilityReports);
            }).ToList();

            return new CorporateReport(
                PublicKeyCodec.Encode(PublicKeyCodec.CorporatePrefix, corp.Id),
                corp.Code, corp.Title, corp.IsActive,
                corpTopics.Count, corpTopics.Sum(t => t.EventCount),
                includeTopics ? corpTopics : null,
                companyReports);
        }).ToList();

        // [2026-09-23][Claude] Emission Units ที่ Subscription ใช้ได้ (Default ของระบบ + Custom ของ Subscription)
        var units = await db.Units.AsNoTracking()
            .Where(u => !u.IsDeleted && (u.SourceType == eSourceType.Default || (u.SourceType == eSourceType.Custom && u.SubscriptionId == subscriptionId)))
            .OrderBy(u => u.SourceType).ThenBy(u => u.Title)
            .Select(u => new { u.Id, u.Title, u.Remark, u.SourceType, u.IsActive })
            .ToListAsync(ct);
        var unitReports = units
            .Select(u => new UnitReport(PublicKeyCodec.Encode(PublicKeyCodec.UnitPrefix, u.Id), u.Title, u.Remark, u.SourceType.ToString(), u.IsActive))
            .ToList();

        // [2026-09-23][Claude] Emission Groups ที่ Subscription ใช้ได้ (Default + Custom)
        var groups = await db.EmissionGroups.AsNoTracking()
            .Where(g => !g.IsDeleted && (g.SourceType == eSourceType.Default || (g.SourceType == eSourceType.Custom && g.SubscriptionId == subscriptionId)))
            .OrderBy(g => g.TgoScopeNo).ThenBy(g => g.Code)
            .Select(g => new { g.Id, g.Code, g.Title, g.TgoScopeNo, g.Remark, g.SourceType, g.IsActive })
            .ToListAsync(ct);
        var groupReports = groups
            .Select(g => new GroupReport(PublicKeyCodec.Encode(PublicKeyCodec.EmissionGroupPrefix, g.Id), g.Code, g.Title, (int)g.TgoScopeNo, g.Remark, g.SourceType.ToString(), g.IsActive))
            .ToList();

        // [2026-09-23][Claude] Emission Sources ที่ Subscription ใช้ได้ (Default + Custom) — EF คำนวณในหน่วยความจำ (EF เป็น [NotMapped] แปลเป็น SQL ไม่ได้)
        var groupScopes = groups.ToDictionary(g => g.Id, g => (int)g.TgoScopeNo);
        var sources = await db.EmissionSources.AsNoTracking()
            .Where(s => !s.IsDeleted && (s.SourceType == eSourceType.Default || (s.SourceType == eSourceType.Custom && s.SubscriptionId == subscriptionId)))
            .OrderBy(s => s.SourceType).ThenBy(s => s.Title)
            .Select(s => new
            {
                s.Id, s.Title, s.GroupId, s.UnitId,
                s.CO2, s.FossilCH4, s.CH4, s.N2O, s.SF6, s.NF3, s.HFCs, s.PFCs,
                s.gwpCO2, s.gwpFossilCH4, s.gwpCH4, s.gwpN2O, s.gwpSF6, s.gwpNF3, s.gwpHFCs, s.gwpPFCs,
                s.GwpVersion, s.Ref, s.Remark, s.SourceType, s.IsActive
            })
            .ToListAsync(ct);
        var sourceReports = sources
            .Select(s => new SourceReport(
                PublicKeyCodec.Encode(PublicKeyCodec.EmissionSourcePrefix, s.Id), s.Title,
                PublicKeyCodec.Encode(PublicKeyCodec.EmissionGroupPrefix, s.GroupId), groupScopes.TryGetValue(s.GroupId, out var scope) ? scope : 0,
                PublicKeyCodec.Encode(PublicKeyCodec.UnitPrefix, s.UnitId),
                s.CO2, s.FossilCH4, s.CH4, s.N2O, s.SF6, s.NF3, s.HFCs, s.PFCs,
                s.gwpCO2, s.gwpFossilCH4, s.gwpCH4, s.gwpN2O, s.gwpSF6, s.gwpNF3, s.gwpHFCs, s.gwpPFCs,
                s.GwpVersion.ToString(),
                (s.CO2 * s.gwpCO2) + (s.FossilCH4 * s.gwpFossilCH4) + (s.CH4 * s.gwpCH4) + (s.N2O * s.gwpN2O) +
                (s.SF6 * s.gwpSF6) + (s.NF3 * s.gwpNF3) + (s.HFCs * s.gwpHFCs) + (s.PFCs * s.gwpPFCs),
                s.Ref, s.Remark, s.SourceType.ToString(), s.IsActive))
            .ToList();

        var totals = new ReportTotals(
            corporates.Count,
            companies.Count,
            facilities.Count,
            topics.Count,
            eventStats.Sum(x => x.Count),
            eventStats.Sum(x => x.Total),
            units.Count(u => u.SourceType == eSourceType.Custom),
            groups.Count(g => g.SourceType == eSourceType.Custom),
            sources.Count(s => s.SourceType == eSourceType.Custom));

        return new SubscriptionReport(sub.Id, sub.Title, sub.SubscriptionPlan.ToString(), sub.StartDate, sub.ExpiryDate, sub.Remarks, totals, unitReports, groupReports, sourceReports, corporateReports);
        #endregion
    }
    #endregion
}
