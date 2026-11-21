using Openspace.Domain.Common;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>Turns what happens to a task into lines of its audit log.</summary>
public static class AuditTrail
{
    /// <summary>The line of work sent back by a person or the reviewer; the character engine reads it.</summary>
    public const string SentBack = "Sent back to the queue";

    public const string Failed = "Failed";

    /// <summary>The line of accepted work sent back to its desk because it no longer merges.</summary>
    public const string CatchUp = "Sent back to catch up with its branch";

    /// <summary>The start of the line of work merged into a branch.</summary>
    public const string MergedInto = "Merged into ";

    /// <summary>The log line for a domain event, or null when the event is not about a task's life.</summary>
    public static AuditEntry? FromDomainEvent(IDomainEvent domainEvent) => domainEvent switch
    {
        WorkTaskCreated e => Lifecycle(e.TaskId, e.OccurredAt, "Task created"),
        WorkTaskStatusChanged e => Lifecycle(e.TaskId, e.OccurredAt, Describe(e.From, e.To)),
        WorkTaskRunAttached e => Lifecycle(e.TaskId, e.OccurredAt, $"Working copy ready on branch {e.Branch}", e.WorkspacePath),
        WorkTaskMerged e => Lifecycle(e.TaskId, e.OccurredAt, $"{MergedInto}{e.TargetBranch}"),
        WorkTaskPullRequestOpened e => Lifecycle(e.TaskId, e.OccurredAt, $"Pull request opened into {e.TargetBranch}: {e.Url}"),
        WorkTaskCheckListed e => AuditEntry.Record(e.TaskId, e.OccurredAt, AuditKind.Procedure, $"Listed a check to try by hand: {e.What}"),
        WorkTaskStepCompleted e => AuditEntry.Record(e.TaskId, e.OccurredAt, AuditKind.Procedure, $"Procedure step {e.Number} done: {e.Text}", e.Evidence),
        WorkTaskNoteAdded e => AuditEntry.Record(e.TaskId, e.OccurredAt, AuditKind.Note, $"Note from {Author(e.Note.Author)}", e.Note.Text),
        _ => null,
    };

    private static AuditEntry Lifecycle(WorkTaskId taskId, DateTimeOffset at, string summary, string? detail = null) =>
        AuditEntry.Record(taskId, at, AuditKind.Lifecycle, summary, detail);

    private static string Describe(WorkTaskStatus from, WorkTaskStatus to) => (from, to) switch
    {
        (WorkTaskStatus.Proposed, WorkTaskStatus.Queued) => "Queued for its desk",
        (WorkTaskStatus.Review, WorkTaskStatus.Queued) => SentBack,
        (WorkTaskStatus.Done, WorkTaskStatus.Queued) => CatchUp,
        (WorkTaskStatus.Failed, WorkTaskStatus.Queued) => "Back in the queue for another try",
        (WorkTaskStatus.Running, WorkTaskStatus.Queued) => "Back in the queue after an interruption",
        (_, WorkTaskStatus.Running) => "Agent started working",
        (_, WorkTaskStatus.Review) => "Agent finished: waiting for review",
        (_, WorkTaskStatus.Failed) => Failed,
        (_, WorkTaskStatus.Done) => "Accepted",
        (_, WorkTaskStatus.Discarded) => "Discarded",
        _ => $"Now {to}",
    };

    private static string Author(NoteAuthor author) => author.Kind switch
    {
        NoteAuthorKind.Human => author.Name ?? "someone",
        NoteAuthorKind.Desk => "the agent",
        _ => "Openspace",
    };
}
