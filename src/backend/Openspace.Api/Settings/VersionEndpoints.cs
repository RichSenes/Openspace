using System.Reflection;
using Openspace.Application.Releases;

namespace Openspace.Api.Settings;

/// <summary>The version running here and the latest release: Openspace tells, the person updates.</summary>
internal static class VersionEndpoints
{
    public static void MapVersionEndpoints(this IEndpointRouteBuilder app)
    {
        var version = app.MapGroup("/api/version").WithTags("Settings");
        version.MapGet("/", (ReleaseWatch watch) => watch.View());
        version.MapPost("/check", async (ReleaseWatch watch, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await watch.CheckAsync(ct));
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(detail: $"GitHub did not answer: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    /// <summary>The version this API was built as, without the commit the build adds after a "+".</summary>
    public static string Running()
    {
        var informational = typeof(VersionEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return (informational ?? "0.0.0").Split('+')[0];
    }
}
