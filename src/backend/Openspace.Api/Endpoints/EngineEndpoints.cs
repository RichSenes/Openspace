using Openspace.Application.Abstractions;
using Openspace.Application.Work;

namespace Openspace.Api.Endpoints;

internal static class EngineEndpoints
{
    public static void MapEngineEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/engines", (IAgentEngines engines, CancellationToken ct) => engines.DescribeAsync(ct)).WithTags("Engines");

        // Spends a few tokens of the smallest model: only on request.
        app.MapPost("/api/engines/claude-usage", async (IAgentEngines engines, CancellationToken ct) =>
            await engines.CheckClaudeUsageAsync(ct) is { } usage
                ? Results.Ok(usage)
                : Results.Problem("Claude Code is not available here, or it did not report the account's limits.", statusCode: StatusCodes.Status409Conflict))
            .WithTags("Engines");

        app.MapGet("/api/usage/desks", (UsageQueries queries, CancellationToken ct) => queries.ByDeskAsync(ct)).WithTags("Engines");
    }
}
