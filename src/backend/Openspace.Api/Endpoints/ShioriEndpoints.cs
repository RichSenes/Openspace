using Microsoft.Extensions.Options;
using Openspace.Application.Shiori;

namespace Openspace.Api.Endpoints;

/// <summary>
/// Shiori's sessions. A message returns at once with the person's line; the answer arrives over the
/// hub as <c>ShioriText</c> pieces, then as a whole <c>ShioriMessage</c> once it is saved.
/// </summary>
internal static class ShioriEndpoints
{
    public sealed record SessionRequest(string? Title, ShioriMode Mode = ShioriMode.Brainstorm);

    /// <summary>A message; <c>Spoken</c> when it was said aloud, for a short spoken answer.</summary>
    public sealed record MessageRequest(string Text, bool Spoken = false);

    public sealed record ModeRequest(ShioriMode Mode);

    public sealed record TitleRequest(string? Title);

    public static void MapShioriEndpoints(this IEndpointRouteBuilder app)
    {
        var sessions = app.MapGroup("/api/shiori/sessions").WithTags("Shiori");

        sessions.MapPost("/", async (SessionRequest? request, ShioriSessions shiori, IOptions<OperatorOptions> me, CancellationToken ct) =>
        {
            var session = await shiori.CreateAsync(request?.Title, request?.Mode ?? ShioriMode.Brainstorm, me.Value.Name, ct);
            return Results.Created($"/api/shiori/sessions/{session.Id}", session);
        });

        sessions.MapGet("/", (ShioriSessions shiori, CancellationToken ct) => shiori.ListAsync(ct));

        sessions.MapGet("/{id:guid}", (Guid id, ShioriSessions shiori, CancellationToken ct) => shiori.GetAsync(id, ct));

        sessions.MapPost("/{id:guid}/messages", async (Guid id, MessageRequest request, ShioriSessions shiori, CancellationToken ct) =>
            Results.Accepted($"/api/shiori/sessions/{id}", await shiori.SendAsync(id, request.Text, request.Spoken, ct)));

        sessions.MapPut("/{id:guid}/mode", (Guid id, ModeRequest request, ShioriSessions shiori, CancellationToken ct) =>
            shiori.SetModeAsync(id, request.Mode, ct));

        sessions.MapPut("/{id:guid}/title", async (Guid id, TitleRequest request, ShioriSessions shiori, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.Problem("Give the session a name.", statusCode: StatusCodes.Status400BadRequest);
            return Results.Ok(await shiori.RenameAsync(id, request.Title, ct));
        });

        sessions.MapDelete("/{id:guid}", async (Guid id, ShioriSessions shiori, CancellationToken ct) =>
        {
            await shiori.DeleteAsync(id, ct);
            return Results.NoContent();
        });

        sessions.MapPost("/{id:guid}/end", (Guid id, ShioriSessions shiori, CancellationToken ct) => shiori.EndAsync(id, ct));
    }
}
