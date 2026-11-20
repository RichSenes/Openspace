namespace Openspace.Application.Brain;

/// <summary>
/// One Markdown note of the vault with its front matter. Every kind of note has the same fields;
/// those that do not apply stay empty and are left out of the file.
/// </summary>
/// <param name="Path">Where it lives in the vault, with forward slashes: <c>Decisions/2026-11-20-merge-order.md</c>.</param>
/// <param name="Kind">rule, decision, plan, study, session, digest, journal or note; null when the file says nothing.</param>
/// <param name="Status">proposed, accepted, superseded or rejected; null when the file says nothing.</param>
/// <param name="Title">The front matter's title, else the first heading, else the file's name.</param>
/// <param name="Offices">The offices it is about; empty means everywhere.</param>
/// <param name="Rooms">The rooms it is about; empty means everywhere.</param>
/// <param name="Paths">Globs of the files it is about, such as <c>src/web/**</c>.</param>
/// <param name="Review">When to look at it again.</param>
/// <param name="Supersedes">The note it replaces, as a wikilink.</param>
/// <param name="From">Where it came from: a task, a session, a study.</param>
/// <param name="Author">An inhabitant, Shiori, or the person.</param>
/// <param name="Body">The Markdown after the front matter.</param>
/// <param name="Extra">Front matter Openspace does not know, kept as written so a rewrite loses nothing.</param>
public sealed record Note(
    string Path,
    string? Kind,
    string? Status,
    string Title,
    IReadOnlyList<string> Offices,
    IReadOnlyList<string> Rooms,
    IReadOnlyList<string> Paths,
    DateOnly? Review,
    string? Supersedes,
    string? From,
    string? Author,
    string Body,
    IReadOnlyList<KeyValuePair<string, string>> Extra)
{
    /// <summary>A note with nothing but a place, a title and a body.</summary>
    public static Note Create(string path, string title, string body) =>
        new(path, null, null, title, [], [], [], null, null, null, null, body, []);

    /// <summary>The notes this one links to with <c>[[wikilinks]]</c>, in the body or in its fields.</summary>
    public IReadOnlyList<string> Links => VaultFormat.Links(Supersedes is null ? Body : $"{Supersedes}\n{Body}");

    /// <summary>When the file was last changed; not part of what is written.</summary>
    public DateTimeOffset Modified { get; init; }
}

/// <summary>The statuses search knows how to weigh; a note may carry any other word.</summary>
public static class NoteStatus
{
    public const string Proposed = "proposed";
    public const string Accepted = "accepted";
    public const string Superseded = "superseded";
    public const string Rejected = "rejected";
}
