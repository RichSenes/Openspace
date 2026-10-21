using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

public enum NoteAuthorKind
{
    Human,
    Desk,
    System,
}

/// <summary>Who wrote a note: a person, an agent at a desk, or Openspace itself.</summary>
public sealed record NoteAuthor
{
    private NoteAuthor(NoteAuthorKind kind, string? name, DeskId? deskId)
    {
        Kind = kind;
        Name = name;
        DeskId = deskId;
    }

    public NoteAuthorKind Kind { get; }

    public string? Name { get; }

    public DeskId? DeskId { get; }

    /// <summary>
    /// A fresh instance on every call: persistence stores authors as part of each note,
    /// and one shared instance cannot belong to several notes at once.
    /// </summary>
    public static NoteAuthor System => new(NoteAuthorKind.System, null, null);

    public static NoteAuthor Human(string name) =>
        new(NoteAuthorKind.Human, Guard.NotBlank(name, "Author name", 80), null);

    public static NoteAuthor FromDesk(DeskId deskId) => new(NoteAuthorKind.Desk, null, deskId);
}
