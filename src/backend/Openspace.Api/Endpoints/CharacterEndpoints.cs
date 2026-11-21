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
    }
}
