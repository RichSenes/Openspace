using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed record WorkTaskView(
    Guid Id,
    Guid OfficeId,
    Guid? DeskId,
    string Title,
    string Description,
    string RequestedBy,
    WorkTaskStatus Status,
    DateTimeOffset CreatedAt,
    string? Branch,
    string? MergedInto,
    Guid? EpicId,
    IReadOnlyList<ProcedureStepView> Procedure,
    IReadOnlyList<TaskNoteView> Notes,
    DateTimeOffset StatusChangedAt,
    int Round,
    IReadOnlyList<ManualCheckView> ManualChecks,
    IReadOnlyList<RoundUsageView> Usage,
    IReadOnlyList<Guid> DependsOn,
    string? PullRequestUrl,
    string? ReserveModel)
{
    public static WorkTaskView From(WorkTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return new(
            task.Id.Value,
            task.OfficeId.Value,
            task.DeskId?.Value,
            task.Title,
            task.Description,
            task.RequestedBy,
            task.Status,
            task.CreatedAt,
            task.Run?.Branch,
            task.MergedInto,
            task.EpicId?.Value,
            [.. task.Procedure.Select(s => new ProcedureStepView(s.Number, s.Text, s.Evidence, s.CompletedAt))],
            [.. task.Notes.Select(TaskNoteView.From)],
            task.StatusChangedAt,
            task.Round,
            [.. task.ManualChecks.Select(c => new ManualCheckView(c.Number, c.What, c.How, c.CheckedAt))],
            [.. task.Usage.OrderBy(u => u.Round).Select(u => new RoundUsageView(u.Round, u.Input, u.Output, u.CacheRead, u.CacheWrite))],
            [.. task.DependsOn.Select(d => d.Value)],
            task.PullRequestUrl,
            task.ReserveModel);
    }
}

public sealed record TaskNoteView(NoteAuthorKind AuthorKind, string? AuthorName, Guid? DeskId, string Text, DateTimeOffset WrittenAt)
{
    public static TaskNoteView From(TaskNote note) =>
        new(note.Author.Kind, note.Author.Name, note.Author.DeskId?.Value, note.Text, note.WrittenAt);
}

public sealed record ProcedureStepView(int Number, string Text, string? Evidence, DateTimeOffset? CompletedAt);

public sealed record ManualCheckView(int Number, string What, string How, DateTimeOffset? CheckedAt);

public sealed record RoundUsageView(int Round, long Input, long Output, long CacheRead, long CacheWrite);
