using Openspace.Application.Brain;

namespace Openspace.Api.Endpoints;

/// <summary>A note as the API shows it: its front matter as fields, its links, its body.</summary>
internal sealed record NoteView(
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
    IReadOnlyList<string> Links,
    DateTimeOffset Modified,
    string Body)
{
    public static NoteView Of(Note note) => new(
        note.Path, note.Kind, note.Status, note.Title, note.Offices, note.Rooms, note.Paths, note.Review,
        note.Supersedes, note.From, note.Author, note.Links, note.Modified, note.Body);
}

internal static class BrainEndpoints
{
    public static void MapBrainEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/brain").WithTags("Brain");

        // Words and scope: notes about the office, room and file asked come first; superseded ones last.
        group.MapGet("/search", (string? q, string? office, string? room, string? path, string? kind, int? limit, BrainSearch search, CancellationToken ct) =>
            search.SearchAsync(new BrainQuery(q, office, room, path, kind, limit ?? BrainSearch.DefaultLimit), ct));

        // The path is the note's place in the vault; ".md" may be left out.
        group.MapGet("/notes/{**path}", async (string path, IVault vault, CancellationToken ct) =>
            await vault.ReadAsync(path, ct) is { } note ? Results.Ok(NoteView.Of(note)) : Results.NotFound());
    }
}
