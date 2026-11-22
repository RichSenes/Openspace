using Microsoft.Extensions.Options;
using Openspace.Application.Characters;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

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

        // Agreements between desks whose planned work clashed, and the person's answer when asked.
        characters.MapGet("/agreements", (Guid officeId, AgreementKeeper keeper, CancellationToken ct) =>
            keeper.ListAsync(new OfficeId(officeId), ct));
        app.MapPost("/api/agreements/{agreementId:guid}/decide", (Guid agreementId, DecideRequest request, AgreementKeeper keeper, IOptions<OperatorOptions> me, CancellationToken ct) =>
            keeper.DecideAsync(new AgreementId(agreementId), new WorkTaskId(request.GoesFirst), me.Value.Name, ct)).WithTags("Characters");

        // The chronicle covers every office: a day's lines, written the first time they are asked.
        var chronicle = app.MapGroup("/api/chronicle").WithTags("Characters");
        chronicle.MapGet("/{day}", (DateOnly day, string? language, Chronicle writer, CancellationToken ct) =>
            writer.DayAsync(day, Language(language), ct));
        chronicle.MapPost("/{day}", (DateOnly day, string? language, Chronicle writer, CancellationToken ct) =>
            writer.WriteAsync(day, Language(language), ct));
    }

    /// <param name="GoesFirst">The task that goes first: one of the agreement's two.</param>
    internal sealed record DecideRequest(Guid GoesFirst);

    private static string Language(string? language) => string.IsNullOrWhiteSpace(language) ? "English" : language.Trim();
}
