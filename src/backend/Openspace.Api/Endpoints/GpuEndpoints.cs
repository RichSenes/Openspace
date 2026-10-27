using Openspace.Application.LocalModels;

namespace Openspace.Api.Endpoints;

/// <param name="Holder">The program that wants the graphics card, such as an image generator.</param>
internal sealed record GpuClaimRequest(string? Holder);

internal static class GpuEndpoints
{
    public static void MapGpuEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/gpu").WithTags("Graphics card");

        group.MapGet("/", (GpuArbiter gpu) => gpu.View());

        // Another program takes the card before local models: they finish or go back to the
        // queue, then the model is unloaded. Renew the claim while the card is needed: it lapses.
        group.MapPost("/claim", (GpuClaimRequest request, GpuArbiter gpu) =>
            string.IsNullOrWhiteSpace(request.Holder)
                ? Results.Problem("Say which program wants the graphics card.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(gpu.Claim(request.Holder)));

        // After a failed load: try again at the next look instead of waiting a few minutes.
        group.MapPost("/retry", (GpuArbiter gpu) =>
        {
            gpu.RetryNow();
            return Results.NoContent();
        });

        group.MapDelete("/claim/{holder}", (string holder, GpuArbiter gpu) => gpu.Release(holder));
    }
}
