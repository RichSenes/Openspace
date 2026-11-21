using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Offices;

public sealed class OfficeQueries(IOfficeRepository offices, IWorkspaceManager workspaces, IPullRequests pullRequests, IWorkTaskRepository tasks)
{
    public async Task<IReadOnlyList<OfficeView>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Select(OfficeView.From)];
    }

    /// <summary>Branches of the office repository that work can be merged into.</summary>
    public async Task<BranchList> BranchesAsync(OfficeId id, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (office.RepositoryPath is null)
        {
            return new BranchList(null, []);
        }

        var list = await workspaces.ListBranchesAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var available = !office.LocalOnly && list.Remotes is { Count: > 0 }
            && await pullRequests.AvailableAsync(office.RepositoryPath, cancellationToken).ConfigureAwait(false);
        var overlaps = list.Current is { } current
            ? await OverlapsAsync(office, current, cancellationToken).ConfigureAwait(false)
            : [];
        return list with { PullRequests = available, Overlaps = overlaps };
    }

    /// <summary>
    /// Pairs of unmerged task branches (in review or accepted) that changed the same files since
    /// leaving <paramref name="target"/>, flagged before a merge: the second to go in will likely conflict.
    /// </summary>
    private async Task<IReadOnlyList<BranchOverlap>> OverlapsAsync(Office office, string target, CancellationToken cancellationToken)
    {
        var all = await tasks.ListAsync(office.Id, null, cancellationToken).ConfigureAwait(false);
        var unmerged = all
            .Where(t => t.Status is WorkTaskStatus.Review or WorkTaskStatus.Done && t.Run is not null && t.MergedInto is null)
            .ToList();
        if (unmerged.Count < 2)
        {
            return [];
        }

        var files = new List<(WorkTask Task, IReadOnlyList<string> Files)>();
        foreach (var task in unmerged)
        {
            try
            {
                files.Add((task, await workspaces.BranchFilesAsync(office.RepositoryPath!, task.Run!.Branch, target, cancellationToken).ConfigureAwait(false)));
            }
            catch (InvalidOperationException)
            {
                // A branch already gone (merged by hand, removed) overlaps with nothing.
            }
        }

        var overlaps = new List<BranchOverlap>();
        for (var i = 0; i < files.Count; i++)
        {
            for (var j = i + 1; j < files.Count; j++)
            {
                var shared = files[i].Files.Intersect(files[j].Files, StringComparer.Ordinal).ToList();
                if (shared.Count > 0)
                {
                    overlaps.Add(new BranchOverlap(files[i].Task.Id.Value, files[i].Task.Title, files[j].Task.Id.Value, files[j].Task.Title, shared));
                }
            }
        }

        return overlaps;
    }

    public async Task<OfficeView> GetAsync(OfficeId id, CancellationToken cancellationToken) =>
        OfficeView.From(await offices.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
