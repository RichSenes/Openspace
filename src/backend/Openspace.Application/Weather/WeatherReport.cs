using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Openspace.Application.Weather;

/// <summary>What the sky is doing, as few kinds as the windows draw.</summary>
public enum Sky
{
    Clear,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Storm,
}

/// <summary>A town found by name, with where it is.</summary>
public sealed record Place(string Name, double Latitude, double Longitude);

/// <summary>The weather in a town right now.</summary>
public sealed record Reading(Sky Sky, double Temperature);

/// <summary>The weather the windows show: in which town, how it is, and when it was read.</summary>
public sealed record WeatherNow(string Place, Sky Sky, double Temperature, DateTimeOffset At);

/// <summary>A public weather service.</summary>
public interface IWeatherSource
{
    /// <summary>The town best matching a name, such as "Turin" or "Turin, Italy"; null when none does.</summary>
    Task<Place?> FindAsync(string name, CancellationToken cancellationToken);

    Task<Reading> CurrentAsync(Place place, CancellationToken cancellationToken);
}

public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>The town whose weather the windows show; empty for a sky that only follows the clock.</summary>
    public string? Place { get; set; }
}

/// <summary>
/// The weather of the person's town, asked again every quarter of an hour at most. When the
/// service cannot be reached the last reading stays, so a hiccup never empties the windows.
/// </summary>
public sealed partial class WeatherReport(IWeatherSource source, IOptionsMonitor<WeatherOptions> options, TimeProvider clock, ILogger<WeatherReport> logger) : IDisposable
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim gate = new(1, 1);
    private string? asked;
    private Place? place;
    private WeatherNow? last;
    private DateTimeOffset tried = DateTimeOffset.MinValue;

    /// <summary>The town set in Openspace, if any.</summary>
    public string? PlaceName => string.IsNullOrWhiteSpace(options.CurrentValue.Place) ? null : options.CurrentValue.Place.Trim();

    /// <summary>The weather now, or null when no town is set, it cannot be found, or nothing could be read yet.</summary>
    public async Task<WeatherNow?> NowAsync(CancellationToken cancellationToken)
    {
        var name = PlaceName;
        if (name is null)
        {
            return null;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = clock.GetUtcNow();
            if (!string.Equals(name, asked, StringComparison.OrdinalIgnoreCase))
            {
                (asked, place, last, tried) = (name, null, null, DateTimeOffset.MinValue);
            }

            if (now - tried < FreshFor)
            {
                return last;
            }

            tried = now;
            try
            {
                place ??= await source.FindAsync(name, cancellationToken).ConfigureAwait(false);
                if (place is null)
                {
                    return null;
                }

                var reading = await source.CurrentAsync(place, cancellationToken).ConfigureAwait(false);
                last = new WeatherNow(place.Name, reading.Sky, reading.Temperature, now);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidDataException or System.Text.Json.JsonException)
            {
                LogUnreachable(logger, name, e.Message);
            }

            return last;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Looks a town up before it is saved, so a typo is caught at once.</summary>
    public Task<Place?> FindAsync(string name, CancellationToken cancellationToken) => source.FindAsync(name.Trim(), cancellationToken);

    public void Dispose() => gate.Dispose();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the weather of {Place}: {Reason}")]
    private static partial void LogUnreachable(ILogger logger, string place, string reason);
}
