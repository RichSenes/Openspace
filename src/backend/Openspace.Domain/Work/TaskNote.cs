namespace Openspace.Domain.Work;

/// <summary>What someone did, found or left open while working on a task.</summary>
public sealed record TaskNote(NoteAuthor Author, string Text, DateTimeOffset WrittenAt)
{
    // Used by persistence, which sets Author right after construction.
    private TaskNote(string text, DateTimeOffset writtenAt)
        : this(null!, text, writtenAt)
    {
    }
}
