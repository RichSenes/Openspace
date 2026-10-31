using System.Text.Json;
using Openspace.Application.Weather;
using Openspace.Infrastructure.Weather;

namespace Openspace.Infrastructure.Tests;

public class OpenMeteoTests
{
    private const string Towns = """
        {"results": [
          {"name": "Springfield", "latitude": 39.8, "longitude": -89.6, "country": "United States", "country_code": "US", "admin1": "Illinois"},
          {"name": "Springfield", "latitude": 37.2, "longitude": -93.3, "country": "United States", "country_code": "US", "admin1": "Missouri"}
        ]}
        """;

    [Theory]
    [InlineData(0, Sky.Clear)]
    [InlineData(3, Sky.Cloudy)]
    [InlineData(45, Sky.Fog)]
    [InlineData(53, Sky.Drizzle)]
    [InlineData(81, Sky.Rain)]
    [InlineData(86, Sky.Snow)]
    [InlineData(95, Sky.Storm)]
    public void Weather_codes_fold_into_what_the_windows_draw(int code, Sky sky) => Assert.Equal(sky, OpenMeteo.SkyOf(code));

    [Fact]
    public void The_first_town_is_taken_unless_a_hint_picks_another()
    {
        using var answer = JsonDocument.Parse(Towns);

        Assert.Equal(new Place("Springfield, United States", 39.8, -89.6), OpenMeteo.Pick(answer.RootElement, []));
        Assert.Equal(37.2, OpenMeteo.Pick(answer.RootElement, ["missouri"])!.Latitude);
        Assert.Null(OpenMeteo.Pick(answer.RootElement, ["Canada"]));
    }

    [Fact]
    public void No_results_means_no_town()
    {
        using var answer = JsonDocument.Parse("""{"generationtime_ms": 0.4}""");

        Assert.Null(OpenMeteo.Pick(answer.RootElement, []));
    }

    [Fact]
    public void The_current_weather_is_read_from_its_code_and_temperature()
    {
        using var answer = JsonDocument.Parse("""{"current": {"time": "2015-06-24T09:00", "temperature_2m": 21.4, "weather_code": 61}}""");

        Assert.Equal(new Reading(Sky.Rain, 21.4), OpenMeteo.ReadingOf(answer.RootElement));
    }
}
