using System.Globalization;
using System.Text.Json;
using Openspace.Application.Weather;

namespace Openspace.Infrastructure.Weather;

/// <summary>
/// Open-Meteo: free, with no key and no account. Only the town's name and its coordinates
/// leave this computer.
/// </summary>
internal sealed class OpenMeteo(HttpClient http) : IWeatherSource
{
    /// <summary>What a hint after the comma may name: the country, its code, a region or a province.</summary>
    private static readonly string[] Regions = ["country", "country_code", "admin1", "admin2"];

    public async Task<Place?> FindAsync(string name, CancellationToken cancellationToken)
    {
        // The search knows town names only; what follows a comma picks among towns of that name.
        var parts = name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var url = new Uri($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(parts[0])}&count=10&language=en&format=json");
        using var answer = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
        return Pick(answer.RootElement, parts[1..]);
    }

    public async Task<Reading> CurrentAsync(Place place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        var url = new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=temperature_2m,weather_code"));
        using var answer = JsonDocument.Parse(await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false));
        return ReadingOf(answer.RootElement);
    }

    internal static Place? Pick(JsonElement answer, IReadOnlyList<string> hints)
    {
        if (!answer.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var towns = results.EnumerateArray().ToList();
        bool Matches(JsonElement town) => hints.All(h =>
            Regions.Any(k =>
                town.TryGetProperty(k, out var v) && string.Equals(v.GetString(), h, StringComparison.OrdinalIgnoreCase)));
        var chosen = towns.FirstOrDefault(Matches);
        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var label = chosen.GetProperty("name").GetString()!;
        if (chosen.TryGetProperty("country", out var country) && country.GetString() is { } c)
        {
            label += ", " + c;
        }

        return new Place(label, chosen.GetProperty("latitude").GetDouble(), chosen.GetProperty("longitude").GetDouble());
    }

    internal static Reading ReadingOf(JsonElement answer)
    {
        var current = answer.GetProperty("current");
        return new Reading(SkyOf(current.GetProperty("weather_code").GetInt32()), current.GetProperty("temperature_2m").GetDouble());
    }

    /// <summary>The WMO weather codes Open-Meteo answers with, folded into what the windows draw.</summary>
    internal static Sky SkyOf(int code) => code switch
    {
        0 or 1 => Sky.Clear,
        2 or 3 => Sky.Cloudy,
        45 or 48 => Sky.Fog,
        >= 51 and <= 57 => Sky.Drizzle,
        (>= 61 and <= 67) or (>= 80 and <= 82) => Sky.Rain,
        (>= 71 and <= 77) or 85 or 86 => Sky.Snow,
        >= 95 and <= 99 => Sky.Storm,
        _ => Sky.Cloudy,
    };
}
