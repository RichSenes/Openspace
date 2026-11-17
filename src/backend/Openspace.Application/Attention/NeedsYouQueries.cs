using Openspace.Application.Abstractions;
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
}

/// <param name="Id">The task, the proposal or the routine.</param>
/// <param name="Detail">A line under the title: the routine of a proposal, what a failed run said.</param>
/// <param name="Since">When it started waiting.</param>
public sealed record NeedsYouItem(NeedsYouKind Kind, Guid OfficeId, string OfficeName, Guid Id, string Title, string Detail, DateTimeOffset Since);

/// <summary>
/// Everything that waits for the person, from every office, in one list: tasks to approve, to
/// review and failed; routines' proposals and routines whose last run failed. The agents' live
/// questions come from the activity board, as they happen.
/// </summary>
public sealed class NeedsYouQueries(IOfficeRepository offices, IWorkTaskRepository tasks, IRoutineRepository routines)
{
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

        foreach (var officeId in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!names.TryGetValue(officeId, out var office))
            {
                continue;
            }

            var mine = await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false);
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

        // By kind, in the order above; within a kind, the oldest first: it has waited longest.
        return [.. items.OrderBy(i => i.Kind).ThenBy(i => i.Since)];
    }

    private static string Short(string text) => text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 199), "…");
}
