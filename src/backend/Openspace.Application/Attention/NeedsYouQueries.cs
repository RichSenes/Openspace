using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Attention;

/// <summary>What waits for the person, of one kind.</summary>
public enum NeedsYouKind
{
    /// <summary>A task proposed (by the supervisor, a routine) waiting to be approved.</summary>
    TaskToApprove,

    /// <summary>A task its agent finished, to accept or send back.</summary>
    TaskToReview,

    /// <summary>A task that failed, to try again or let go.</summary>
    TaskFailed,

    /// <summary>A routine's proposal waiting for yes or no.</summary>
    Proposal,

    /// <summary>A routine whose last run failed.</summary>
    RoutineFailed,

    /// <summary>A proposal Shiori accepted, for the person to look at afterwards: fine, open or undo.</summary>
    DecisionToReview,

    /// <summary>A proposal Shiori left for the person, with the reason: accept, open or reject.</summary>
    DecisionToAccept,

    /// <summary>An accepted decision whose review date came, brought back by a decision-reviews routine.</summary>
    DecisionDue,

    /// <summary>Two desks whose work clashes could not settle who goes first: one question, with both proposals.</summary>
    Agreement,
}

/// <param name="Id">The task, the proposal or the routine.</param>
/// <param name="Detail">A line under the title: the routine of a proposal, what a failed run said.</param>
/// <param name="Since">When it started waiting.</param>
/// <param name="Note">The note's path in the vault, for decisions; their id is made from it.</param>
public sealed record NeedsYouItem(NeedsYouKind Kind, Guid OfficeId, string OfficeName, Guid Id, string Title, string Detail, DateTimeOffset Since, string? Note = null);

/// <summary>
/// Everything that waits for the person, from every office, in one list: tasks to approve, to
/// review and failed; routines' proposals and routines whose last run failed. The agents' live
/// questions come from the activity board, as they happen. Decisions come from the vault: those
/// Shiori accepted, those it left for the person, and those a decision-reviews routine brought back.
/// </summary>
public sealed class NeedsYouQueries(
    IOfficeRepository offices, IWorkTaskRepository tasks, IRoutineRepository routines, IVault vault, TimeProvider clock, IAgreementRepository? agreements = null)
{
    /// <summary>The kind of routine that brings decisions back when their review date comes.</summary>
    public const string ReviewsKind = "decision-reviews";

    public async Task<IReadOnlyList<NeedsYouItem>> ListAsync(CancellationToken cancellationToken)
    {
        var names = (await offices.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(o => o.Id, o => o.Name);
        var items = new List<NeedsYouItem>();
        foreach (var (status, kind) in new[]
                 {
                     (WorkTaskStatus.Proposed, NeedsYouKind.TaskToApprove),
                     (WorkTaskStatus.Review, NeedsYouKind.TaskToReview),
                     (WorkTaskStatus.Failed, NeedsYouKind.TaskFailed),
                 })
        {
            foreach (var task in await tasks.ListAsync(null, status, cancellationToken).ConfigureAwait(false))
            {
                if (task.MergedInto is null && names.TryGetValue(task.OfficeId, out var office))
                {
                    items.Add(new NeedsYouItem(kind, task.OfficeId.Value, office, task.Id.Value, task.Title, string.Empty, task.StatusChangedAt));
                }
            }
        }

        // The latest day each office's decision-reviews routine looked: decisions due by then are back.
        var reviewed = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var officeId in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!names.TryGetValue(officeId, out var office))
            {
                continue;
            }

            var mine = await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false);
            foreach (var routine in mine.Where(r => r.Enabled && r.Kind == ReviewsKind))
            {
                var good = (await routines.ListRunsAsync(routine.Id, 10, cancellationToken).ConfigureAwait(false))
                    .Where(r => r.Status == RoutineRunStatus.Succeeded).MaxBy(r => r.StartedAt);
                if (good is not null)
                {
                    var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(good.StartedAt, clock.LocalTimeZone).DateTime);
                    reviewed[office] = reviewed.TryGetValue(office, out var other) && other > day ? other : day;
                }
            }

            foreach (var proposal in await routines.ListProposalsAsync(officeId, ProposalStatus.Pending, cancellationToken).ConfigureAwait(false))
            {
                var routine = mine.FirstOrDefault(r => r.Id == proposal.RoutineId);
                items.Add(new NeedsYouItem(NeedsYouKind.Proposal, officeId.Value, office, proposal.Id.Value, proposal.Title, routine?.Name ?? string.Empty, proposal.CreatedAt));
            }

            foreach (var routine in mine.Where(r => r.Enabled))
            {
                var runs = await routines.ListRunsAsync(routine.Id, 1, cancellationToken).ConfigureAwait(false);
                if (runs.Count > 0 && runs[0] is { Status: RoutineRunStatus.Failed } last)
                {
                    items.Add(new NeedsYouItem(
                        NeedsYouKind.RoutineFailed, officeId.Value, office, routine.Id.Value, routine.Name, Short(last.Summary ?? string.Empty), last.FinishedAt ?? last.StartedAt));
                }
            }
        }

        await AddDecisionsAsync(items, names, reviewed, cancellationToken).ConfigureAwait(false);
        if (agreements is not null)
        {
            foreach (var agreement in (await agreements.ListAsync(null, cancellationToken).ConfigureAwait(false)).Where(a => a.Status == AgreementStatus.WaitingForPerson))
            {
                if (names.TryGetValue(agreement.OfficeId, out var office))
                {
                    items.Add(new NeedsYouItem(
                        NeedsYouKind.Agreement, agreement.OfficeId.Value, office, agreement.Id.Value, $"Who goes first on {agreement.Subject}?",
                        $"{agreement.FirstProposal} {agreement.SecondProposal}", agreement.OpenedAt, agreement.MinutesPath));
                }
            }
        }

        // By kind, in the order above; within a kind, the oldest first: it has waited longest.
        return [.. items.OrderBy(i => i.Kind).ThenBy(i => i.Since)];
    }

    private async Task AddDecisionsAsync(
        List<NeedsYouItem> items, Dictionary<Domain.Offices.OfficeId, string> names, Dictionary<string, DateOnly> reviewed, CancellationToken cancellationToken)
    {
        IReadOnlyList<Note> notes;
        try
        {
            notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A vault that cannot be read leaves the rest of the inbox as it is.
            return;
        }

        var byName = names.ToDictionary(n => n.Value, n => n.Key.Value, StringComparer.OrdinalIgnoreCase);
        (Guid Id, string Name) Office(Note note) => note.Offices.Count switch
        {
            0 => (Guid.Empty, "Every office"),
            1 => (byName.GetValueOrDefault(note.Offices[0].Trim()), note.Offices[0].Trim()),
            _ => (Guid.Empty, string.Join(", ", note.Offices)),
        };

        NeedsYouItem Item(NeedsYouKind kind, Note note, string detail, DateTimeOffset since)
        {
            var (id, name) = Office(note);
            return new NeedsYouItem(kind, id, name, DecisionRules.IdOf(note.Path), note.Title, detail, since, note.Path);
        }

        var accepted = notes.Where(DecisionRules.IsAcceptedDecision).ToList();
        foreach (var note in notes)
        {
            if (DecisionRules.ToReview(note))
            {
                items.Add(Item(NeedsYouKind.DecisionToReview, note, Proposer(note, "Accepted by Shiori"), note.Modified));
            }
            else if (DecisionRules.IsProposed(note))
            {
                // Shiori accepts what it may within moments; what stays proposed says why it waits.
                var why = DecisionRules.WhyWaiting(note, accepted) ?? "Shiori has not read it yet.";
                items.Add(Item(NeedsYouKind.DecisionToAccept, note, Proposer(note, why), note.Modified));
            }
        }

        var due = new Dictionary<string, Note>(StringComparer.Ordinal);
        foreach (var (office, day) in reviewed)
        {
            // One that also waits for its first review shows once, as that.
            foreach (var note in DecisionRules.Due(notes, office, day).Where(n => !DecisionRules.ToReview(n)))
            {
                due.TryAdd(note.Path, note);
            }
        }

        foreach (var note in due.Values)
        {
            var review = note.Review!.Value;
            items.Add(Item(NeedsYouKind.DecisionDue, note, $"Review was due {DecisionRules.Day(review)}", new DateTimeOffset(review.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }
    }

    private static string Proposer(Note note, string detail) =>
        string.IsNullOrWhiteSpace(note.Author) ? detail : $"{detail} · proposed by {note.Author.Trim()}";

    private static string Short(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 199), "…");
}
