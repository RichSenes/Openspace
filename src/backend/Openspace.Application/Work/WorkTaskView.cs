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
    IReadOnlyList<TaskNoteView> Notes)
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
            [.. task.Notes.Select(TaskNoteView.From)]);
    }
}

public sealed record TaskNoteView(NoteAuthorKind AuthorKind, string? AuthorName, Guid? DeskId, string Text, DateTimeOffset WrittenAt)
{
    public static TaskNoteView From(TaskNote note) =>
        new(note.Author.Kind, note.Author.Name, note.Author.DeskId?.Value, note.Text, note.WrittenAt);
}
