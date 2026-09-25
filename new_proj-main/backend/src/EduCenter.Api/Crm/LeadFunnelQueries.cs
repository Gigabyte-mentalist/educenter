using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Crm.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Crm;

/// <summary>One lead source's slice of the funnel — "where do the students come from, and which source's leads actually stay".</summary>
public record SourceFunnelDto(string Source, int Leads, int Contacted, int TrialAttended, int Converted, int Lost, double ConversionRate);

/// <summary>One step of the marketing funnel. Both rates are 0–1 fractions.</summary>
/// <param name="ConversionFromPrevious">This step's count over the step before it. Can pass 1 for "Student bo'ldi": a lead may enrol
/// without a trial, so that step is not a subset of the trial steps.</param>
/// <param name="ShareOfLeads">This step's count over every lead of the cohort — never above 1.</param>
public record FunnelStageDto(string Key, string Label, int Count, double ConversionFromPrevious, double ShareOfLeads);

public record SourceStagesDto(string Source, List<FunnelStageDto> Stages);

/// <summary>The marketing funnel of a cohort — every step overall, and the same steps for each source.</summary>
public record FunnelDto(List<FunnelStageDto> Stages, List<SourceStagesDto> BySource);

/// <summary>
/// The lead funnel of a set of branches over a period. It is a <b>cohort</b>: the leads that <i>came in</i> during the period, followed to
/// wherever they have got to by now. (A lead keeps only its current status, not a history, so "contacted" means "no longer a fresh lead"
/// and "lost" is Lost or NotInterested.) Rates are 0–1 fractions, like the rest of the dashboard.
/// </summary>
public record LeadFunnelDto(
    int Leads, int Contacted, int TrialBooked, int TrialAttended, int Converted, int Lost,
    int TrialToStudent, int TrialToPayment,
    double LeadConversionRate, double TrialToStudentRate, double TrialToPaymentRate,
    List<SourceFunnelDto> Sources, List<FunnelStageDto> Stages);

/// <summary>Where one lead of the cohort has got to — the only thing the funnel counts.</summary>
public sealed record LeadJourney(
    Guid LeadId, string Source, LeadStatus Status, bool Contacted, bool TrialBooked, bool TrialAttended, bool Converted, bool Paid)
{
    public bool IsLost => Status is LeadStatus.Lost or LeadStatus.NotInterested;
}

/// <summary>
/// Pure reads for the lead funnel. The branch dashboard's CRM block and the marketing funnel report (<c>GET /api/leads/funnel</c>) both
/// count from <see cref="JourneysAsync"/> and both build their stages with <see cref="Stages"/>, so a number is never two numbers.
/// </summary>
public static class LeadFunnelQueries
{
    public const string UnknownSource = "Ko'rsatilmagan";

    private static double Rate(int part, int whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);

    /// <summary>The leads that came in during the period, each followed to its furthest step. "The period" is a span of the lead's own
    /// branch's calendar days.</summary>
    /// <param name="branches">The branches to look at, each with its timezone.</param>
    public static async Task<List<LeadJourney>> JourneysAsync(
        AppDbContext db, IReadOnlyCollection<(Guid Id, string? Timezone)> branches, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var branchIds = branches.Select(b => b.Id).ToList();
        var timezone = branches.ToDictionary(b => b.Id, b => b.Timezone);
        var fetchFrom = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fetchTo = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var leads = (await db.Leads
                .Where(l => branchIds.Contains(l.BranchId) && l.CreatedAt >= fetchFrom && l.CreatedAt < fetchTo)
                .Select(l => new { l.Id, l.BranchId, l.Source, l.Status, l.CreatedAt, l.ConvertedStudentId })
                .ToListAsync(ct))
            .Where(l =>
            {
                var local = BranchClock.LocalDate(timezone[l.BranchId], l.CreatedAt);
                return local >= from && local <= to;
            })
            .ToList();

        // Which of them were booked into a trial, and how it went. A lead may have been booked more than once (missed, rebooked).
        var trials = await (
            from t in db.TrialLessons
            join l in db.Leads on t.LeadId equals l.Id
            where branchIds.Contains(l.BranchId) && l.CreatedAt >= fetchFrom && l.CreatedAt < fetchTo
            select new { t.LeadId, t.AttendanceStatus }).ToListAsync(ct);
        var booked = trials.Select(t => t.LeadId).ToHashSet();
        var attended = trials.Where(t => t.AttendanceStatus == TrialAttendanceStatus.Attended).Select(t => t.LeadId).ToHashSet();

        // A call that was answered is a contact; one nobody picked up is not (Lead status stays "Yangi" then, see LeadsController.LogCall).
        var called = (await (
            from c in db.CrmCalls
            join l in db.Leads on c.LeadId equals l.Id
            where branchIds.Contains(l.BranchId) && l.CreatedAt >= fetchFrom && l.CreatedAt < fetchTo && c.Outcome != CallOutcome.NoAnswer
            select c.LeadId).Distinct().ToListAsync(ct)).ToHashSet();

        // A student "paid" once any payment of theirs stands (a voided one is as if it never happened).
        var studentIds = leads.Where(l => l.ConvertedStudentId != null).Select(l => l.ConvertedStudentId!.Value).ToList();
        var paid = (await db.Payments.Where(p => !p.IsVoided && studentIds.Contains(p.StudentId))
            .Select(p => p.StudentId).Distinct().ToListAsync(ct)).ToHashSet();

        return leads.Select(l =>
        {
            var converted = l.Status == LeadStatus.Converted;
            var didBook = booked.Contains(l.Id);
            return new LeadJourney(
                l.Id, l.Source, l.Status,
                Contacted: l.Status != LeadStatus.Lead || called.Contains(l.Id) || didBook,
                TrialBooked: didBook,
                TrialAttended: attended.Contains(l.Id),
                Converted: converted,
                Paid: converted && l.ConvertedStudentId is { } studentId && paid.Contains(studentId));
        }).ToList();
    }

    /// <summary>Lidlar → Bog'lanilgan → Trial band → Trial keldi → Student bo'ldi → To'lov qildi.</summary>
    public static List<FunnelStageDto> Stages(IReadOnlyCollection<LeadJourney> journeys)
    {
        var counts = new (string Key, string Label, int Count)[]
        {
            ("leads", "Lidlar", journeys.Count),
            ("contacted", "Bog'lanilgan", journeys.Count(j => j.Contacted)),
            ("trialBooked", "Trial band", journeys.Count(j => j.TrialBooked)),
            ("trialAttended", "Trial keldi", journeys.Count(j => j.TrialAttended)),
            ("student", "Student bo'ldi", journeys.Count(j => j.Converted)),
            ("paid", "To'lov qildi", journeys.Count(j => j.Paid)),
        };

        return counts.Select((c, i) => new FunnelStageDto(
            c.Key, c.Label, c.Count,
            ConversionFromPrevious: i == 0 ? 1 : Rate(c.Count, counts[i - 1].Count),
            ShareOfLeads: i == 0 ? 1 : Rate(c.Count, counts[0].Count))).ToList();
    }

    /// <summary>"Instagram", "instagram " and "INSTAGRAM" are one source; a blank one is <see cref="UnknownSource"/>.</summary>
    public static string SourceKey(string? source)
    {
        var trimmed = source?.Trim() ?? "";
        return trimmed.Length == 0 || trimmed.Equals(UnknownSource, StringComparison.OrdinalIgnoreCase) ? "" : trimmed.ToLowerInvariant();
    }

    /// <summary>The cohort split by source. Each group shows the spelling most leads used ("Instagram", not "instagram").</summary>
    private static List<(string Source, List<LeadJourney> Journeys)> BySource(IReadOnlyCollection<LeadJourney> journeys) =>
        journeys.GroupBy(j => SourceKey(j.Source))
            .Select(g => (
                Source: g.Key.Length == 0
                    ? UnknownSource
                    : g.GroupBy(j => j.Source.Trim()).OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key,
                Journeys: g.ToList()))
            .OrderByDescending(s => s.Journeys.Count).ThenBy(s => s.Source, StringComparer.Ordinal)
            .ToList();

    /// <summary>The marketing funnel: every step overall and per source, optionally for one source only.</summary>
    public static async Task<FunnelDto> FunnelAsync(
        AppDbContext db, IReadOnlyCollection<(Guid Id, string? Timezone)> branches, DateOnly from, DateOnly to,
        string? source = null, CancellationToken ct = default)
    {
        var journeys = await JourneysAsync(db, branches, from, to, ct);
        if (!string.IsNullOrWhiteSpace(source))
        {
            var wanted = SourceKey(source);
            journeys = journeys.Where(j => SourceKey(j.Source) == wanted).ToList();
        }

        return new FunnelDto(Stages(journeys), BySource(journeys).Select(s => new SourceStagesDto(s.Source, Stages(s.Journeys))).ToList());
    }

    /// <summary>The branch dashboard's CRM block — the same journeys and the same stages as the marketing funnel.</summary>
    public static async Task<LeadFunnelDto> ForBranchesAsync(
        AppDbContext db, IReadOnlyCollection<(Guid Id, string? Timezone)> branches, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var journeys = await JourneysAsync(db, branches, from, to, ct);

        var attendedCount = journeys.Count(j => j.TrialAttended);
        var converted = journeys.Count(j => j.Converted);
        var trialToStudent = journeys.Count(j => j.TrialAttended && j.Converted);
        var trialToPayment = journeys.Count(j => j.TrialAttended && j.Paid);

        var sources = BySource(journeys).Select(s =>
            {
                var convertedHere = s.Journeys.Count(j => j.Converted);
                return new SourceFunnelDto(s.Source, s.Journeys.Count, s.Journeys.Count(j => j.Contacted),
                    s.Journeys.Count(j => j.TrialAttended), convertedHere, s.Journeys.Count(j => j.IsLost), Rate(convertedHere, s.Journeys.Count));
            })
            .ToList();

        return new LeadFunnelDto(
            journeys.Count, journeys.Count(j => j.Contacted), journeys.Count(j => j.TrialBooked), attendedCount,
            converted, journeys.Count(j => j.IsLost),
            trialToStudent, trialToPayment,
            Rate(converted, journeys.Count), Rate(trialToStudent, attendedCount), Rate(trialToPayment, attendedCount),
            sources, Stages(journeys));
    }
}
