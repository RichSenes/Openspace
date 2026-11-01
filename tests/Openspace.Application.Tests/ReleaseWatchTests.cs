using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Releases;

namespace Openspace.Application.Tests;

public class ReleaseWatchTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("0.10.0", "0.9.2", true)]
    [InlineData("v1.0.0", "0.9.0", true)]
    [InlineData("0.9.0", "0.9.0", false)]
    [InlineData("0.9", "0.9.0", false)]
    [InlineData("0.8.5", "0.9.0", false)]
    [InlineData("1.2.3-beta+abc", "1.2.2", true)]
    public void Versions_compare_part_by_part(string candidate, string current, bool newer) =>
        Assert.Equal(newer, ReleaseWatch.IsNewer(candidate, current));

    [Fact]
    public async Task A_newer_release_is_told_and_an_older_one_is_not()
    {
        var source = new Source { Latest = new Release("0.10.0", "Openspace 0.10.0", "- Rooms can be renamed", new Uri("https://example.com/releases/v0.10.0"), null) };
        var watch = new ReleaseWatch(source, new AppVersion("0.9.0"), _clock);
        Assert.Equal(new VersionView("0.9.0", null, false, null), watch.View());

        var view = await watch.CheckAsync(default);

        Assert.True(view.Newer);
        Assert.Equal(("0.10.0", _clock.GetUtcNow()), (view.Latest!.Version, view.CheckedAt!.Value));
        source.Latest = source.Latest with { Version = "0.8.0" };
        Assert.False((await watch.CheckAsync(default)).Newer);
    }

    [Fact]
    public async Task A_failed_look_keeps_the_last_release_seen()
    {
        var source = new Source { Latest = new Release("1.0.0", "Openspace 1.0.0", "", new Uri("https://example.com/r"), null) };
        var watch = new ReleaseWatch(source, new AppVersion("0.9.0"), _clock);
        await watch.CheckAsync(default);
        source.Fail = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => watch.CheckAsync(default));

        Assert.Equal("1.0.0", watch.View().Latest!.Version);
    }

    private sealed class Source : IReleaseSource
    {
        public Release? Latest { get; set; }

        public bool Fail { get; set; }

        public Task<Release?> LatestAsync(CancellationToken cancellationToken) =>
            Fail ? throw new HttpRequestException("offline") : Task.FromResult(Latest);
    }
}
