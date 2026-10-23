using Openspace.Application.Abstractions;

namespace Openspace.Api.Endpoints;

internal static class EngineEndpoints
{
    public static void MapEngineEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/engines", (IAgentEngines engines, CancellationToken ct) => engines.DescribeAsync(ct)).WithTags("Engines");
}
