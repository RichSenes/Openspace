using Microsoft.Extensions.Options;
using Openspace.Application.Weather;
using Openspace.Domain.Common;

namespace Openspace.Api.Settings;

/// <param name="Place">The town set in Openspace; null for a sky that only follows the clock.</param>
/// <param name="Now">Its weather, once read.</param>
public sealed record WeatherView(string? Place, WeatherNow? Now);

/// <param name="Place">A town, such as "Turin" or "Turin, Italy"; empty to stop showing the weather.</param>
public sealed record WeatherRequest(string? Place);

/// <summary>The weather at the windows, and the town it comes from.</summary>
internal static class WeatherEndpoints
{
    public const int MaxPlace = 120;

    public static void MapWeatherEndpoints(this IEndpointRouteBuilder app)
    {
        var weather = app.MapGroup("/api/weather").WithTags("Settings");
        weather.MapGet("/", async (WeatherReport report, CancellationToken ct) => new WeatherView(report.PlaceName, await report.NowAsync(ct)));
        weather.MapPut("/", async (WeatherRequest request, WeatherReport report, IConfiguration configuration, IOptions<SettingsFileOptions> file, CancellationToken ct) =>
        {
            var place = await PlaceOf(request, report, ct);
            SettingsEndpoints.Patch(file.Value.Path, section => section(WeatherOptions.SectionName)["Place"] = place ?? string.Empty);
            ((IConfigurationRoot)configuration).Reload();
            return new WeatherView(report.PlaceName, await report.NowAsync(ct));
        });
    }

    /// <summary>The town as the weather service names it, so the same one is found every time.</summary>
    private static async Task<string?> PlaceOf(WeatherRequest request, WeatherReport report, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Place))
        {
            return null;
        }

        if (request.Place.Length > MaxPlace)
        {
            throw new DomainException($"A town takes at most {MaxPlace} characters.");
        }

        Place? found;
        try
        {
            found = await report.FindAsync(request.Place, ct);
        }
        catch (HttpRequestException)
        {
            throw new DomainException("The weather service cannot be reached now: try again in a while.");
        }

        return found?.Name ?? throw new DomainException($"No town called “{request.Place.Trim()}” was found: try its name in English, or add the country after a comma.");
    }
}
