using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Weather;

namespace Openspace.Application.Tests;

public sealed class WeatherReportTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly FakeSky _source = new();
    private readonly WeatherOptions _options = new() { Place = "New York" };
    private readonly WeatherReport _report;

    public WeatherReportTests() =>
        _report = new WeatherReport(_source, new LiveOptions<WeatherOptions>(() => _options), _clock, NullLogger<WeatherReport>.Instance);

    public void Dispose() => _report.Dispose();

    [Fact]
    public async Task No_town_means_no_weather_and_no_calls()
    {
        _options.Place = "  ";

        Assert.Null(await _report.NowAsync(CancellationToken.None));
        Assert.Equal(0, _source.Calls);
    }

    [Fact]
    public async Task The_weather_is_read_once_per_quarter_of_an_hour()
    {
        var first = await _report.NowAsync(CancellationToken.None);
        _source.Now = new Reading(Sky.Snow, -2);
        _clock.Advance(TimeSpan.FromMinutes(14));
        var cached = await _report.NowAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var fresh = await _report.NowAsync(CancellationToken.None);

        Assert.Equal(new WeatherNow("New York, United States", Sky.Rain, 12.5, Start), first);
        Assert.Equal(first, cached);
        Assert.Equal(Sky.Snow, fresh!.Sky);
        Assert.Equal(2, _source.Calls);
        Assert.Equal(1, _source.Finds);
    }

    [Fact]
    public async Task A_failed_reading_keeps_the_last_one()
    {
        var first = await _report.NowAsync(CancellationToken.None);
        _source.Fail = true;
        _clock.Advance(WeatherReport.FreshFor);

        Assert.Equal(first, await _report.NowAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_new_town_is_read_at_once()
    {
        await _report.NowAsync(CancellationToken.None);
        _options.Place = "Nowhere";

        Assert.Null(await _report.NowAsync(CancellationToken.None));
        Assert.Equal(2, _source.Finds);
    }

    private sealed class FakeSky : IWeatherSource
    {
        public Reading Now { get; set; } = new(Sky.Rain, 12.5);

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public int Finds { get; private set; }

        public Task<Place?> FindAsync(string name, CancellationToken cancellationToken)
        {
            Finds++;
            return Task.FromResult(name == "New York" ? new Place("New York, United States", 40.7, -74) : null);
        }

        public Task<Reading> CurrentAsync(Place place, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? throw new HttpRequestException("down") : Task.FromResult(Now);
        }
    }
}
