namespace Openspace.Domain.Work;

/// <summary>What someone did, found or left open while working on a task.</summary>
public sealed record TaskNote(NoteAuthor Author, string Text, DateTimeOffset WrittenAt);
