namespace Openspace.Application.Brain;

/// <param name="Paths">Files the task is changing in its copy, and those its plan named, relative to the repository.</param>
public sealed record WorkInFlight(Guid TaskId, Guid OfficeId, string Office, string Room, string Desk, string Title, IReadOnlyList<string> Paths);

/// <summary>
/// Who is changing what, right now: what the briefing, the agents' tools and the queue read
/// to keep work on the same files from running side by side.
/// </summary>
public interface IWorkInProgress
{
    IReadOnlyList<WorkInFlight> Current();
}

/// <summary>Knows of no work: in place until the queue keeps the real list.</summary>
public sealed class NoWorkInProgress : IWorkInProgress
{
    public IReadOnlyList<WorkInFlight> Current() => [];
}
