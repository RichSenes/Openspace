using Microsoft.Extensions.Options;
using Openspace.Application.Brain;

namespace Openspace.Api.Endpoints;

/// <summary>A note linking to another, as the backlinks list shows it.</summary>
internal sealed record BacklinkView(string Path, string Title, string? Kind, string? Status, DateTimeOffset Modified);

/// <param name="Text">The whole file: front matter, then Markdown.</param>
/// <param name="Summary">What changed, for the history; "wrote" or "edited" the note when left out.</param>
internal sealed record WikiEdit(string? Text, string? Summary);

/// <summary>What the wiki page needs beyond reading and searching: the tree, links, history, edits, and "why".</summary>
internal static class WikiEndpoints
{
    private const string Backlinks = "/backlinks";
    private const string History = "/history";

    public static void MapWikiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/brain").WithTags("Brain");

        // Every note in its folder, folders first, without bodies.
        group.MapGet("/tree", (Wiki wiki, CancellationToken ct) => wiki.TreeAsync(ct));

        // ".../notes/{path}/backlinks", ".../history" and ".../history/{commit}". A route that catches
        // the whole path must end there, so the verb is read off its end; the constraint keeps plain
        // reads of a note on the route that serves them, and makes this route the stronger match.
        group.MapGet("/notes/{**rest:regex(^.+/(backlinks|history|history/\\w+)$)}", async (string rest, int? limit, Wiki wiki, CancellationToken ct) =>
        {
            var trimmed = rest.TrimEnd('/');
            if (trimmed.EndsWith(Backlinks, StringComparison.OrdinalIgnoreCase))
            {
                return await wiki.BacklinksAsync(trimmed[..^Backlinks.Length], ct) is { } notes
                    ? Results.Ok(notes.Select(n => new BacklinkView(n.Path, n.Title, n.Kind, n.Status, n.Modified)))
                    : Results.NotFound();
            }

            if (trimmed.EndsWith(History, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(await wiki.HistoryAsync(trimmed[..^History.Length], limit, ct));
            }

            var cut = trimmed.LastIndexOf(History + "/", StringComparison.OrdinalIgnoreCase);
            return cut > 0 && await wiki.VersionAsync(trimmed[..cut], trimmed[(cut + History.Length + 1)..], ct) is { } version
                ? Results.Ok(version)
                : Results.NotFound();
        });

        // The person writes a note, the constitution included, committed under their name.
        group.MapPut("/notes/{**path}", async (string path, WikiEdit edit, Wiki wiki, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            if (edit?.Text is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "No text", detail: "Send the note's whole text.");
            }

            var written = await wiki.WriteAsync(path, edit.Text, me.Value.Name, edit.Summary, ct);
            var view = NoteView.Of(written.Note);
            return written.Created ? Results.Created($"/api/brain/notes/{written.Note.Path}", view) : Results.Ok(view);
        });

        // "Why is this so?": accepted decisions whose paths cover a file of a repository, newest
        // first; with an office or room, the general decisions that apply there after them.
        group.MapGet("/why", async (string? path, string? office, string? room, Wiki wiki, CancellationToken ct) =>
            string.IsNullOrWhiteSpace(path)
                ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "No file", detail: "Say which file: ?path=src/web/cart.ts")
                : Results.Ok(await wiki.WhyAsync(path, office, room, ct)));
    }
}
