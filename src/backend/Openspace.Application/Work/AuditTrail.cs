using Openspace.Domain.Common;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>Turns what happens to a task into lines of its audit log.</summary>
public static class AuditTrail
{
    /// <summary>The log line for a domain event, or null when the event is not about a task's life.</summary>
    public static AuditEntry? FromDomainEvent(IDomainEvent domainEvent) => domainEvent switch
    {
        WorkTaskCreated e => Lifecycle(e.TaskId, e.OccurredAt, "Task created"),
        WorkTaskStatusChanged e => Lifecycle(e.TaskId, e.OccurredAt, Describe(e.From, e.To)),
        WorkTaskRunAttached e => Lifecycle(e.TaskId, e.OccurredAt, $"Working copy ready on branch {e.Branch}", e.WorkspacePath),
        WorkTaskMerged e => Lifecycle(e.TaskId, e.OccurredAt, $"Merged into {e.TargetBranch}"),
        WorkTaskNoteAdded e => AuditEntry.Record(e.TaskId, e.OccurredAt, AuditKind.Note, $"Note from {Author(e.Note.Author)}", e.Note.Text),
        _ => null,
    };

    private static AuditEntry Lifecycle(WorkTaskId taskId, DateTimeOffset at, string summary, string? detail = null) =>
        AuditEntry.Record(taskId, at, AuditKind.Lifecycle, summary, detail);

    private static string Describe(WorkTaskStatus from, WorkTaskStatus to) => (from, to) switch
    {
        (WorkTaskStatus.Proposed, WorkTaskStatus.Queued) => "Queued for its desk",
        (WorkTaskStatus.Review, WorkTaskStatus.Queued) => "Sent back to the queue",
        (WorkTaskStatus.Failed, WorkTaskStatus.Queued) => "Back in the queue for another try",
        (WorkTaskStatus.Running, WorkTaskStatus.Queued) => "Back in the queue after an interruption",
        (_, WorkTaskStatus.Running) => "Agent started working",
        (_, WorkTaskStatus.Review) => "Agent finished: waiting for review",
        (_, WorkTaskStatus.Failed) => "Failed",
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
