using Openspace.Application.Characters;
using Openspace.Domain.Offices;

namespace Openspace.Api.Endpoints;

/// <summary>The character monitor, for the person. Never exposed to agents through MCP.</summary>
internal static class CharacterEndpoints
{
    public static void MapCharacterEndpoints(this IEndpointRouteBuilder app)
    {
        var characters = app.MapGroup("/api/offices/{officeId:guid}/characters").WithTags("Characters");

        characters.MapGet("/", (Guid officeId, CharacterQueries queries, CancellationToken ct) =>
            queries.GetAsync(new OfficeId(officeId), ct));

        // The office's next days: queued work, routines, reviews, and the calendar when connected.
        app.MapGet("/api/offices/{officeId:guid}/agenda", (Guid officeId, int? days, OfficeAgenda agenda, CancellationToken ct) =>
            agenda.GetAsync(new OfficeId(officeId), days, ct)).WithTags("Characters");

        // The chronicle covers every office: a day's lines, written the first time they are asked.
        var chronicle = app.MapGroup("/api/chronicle").WithTags("Characters");
        chronicle.MapGet("/{day}", (DateOnly day, string? language, Chronicle writer, CancellationToken ct) =>
            writer.DayAsync(day, Language(language), ct));
        chronicle.MapPost("/{day}", (DateOnly day, string? language, Chronicle writer, CancellationToken ct) =>
            writer.WriteAsync(day, Language(language), ct));
    }

    private static string Language(string? language) => string.IsNullOrWhiteSpace(language) ? "English" : language.Trim();
}
