using System.Net;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Openspace.Application.Releases;
using Openspace.Infrastructure.Releases;

namespace Openspace.Infrastructure.Tests;

public class GitHubReleasesTests
{
    [Theory]
    [InlineData("https://github.com/example-owner/openspace.git", "example-owner/openspace")]
    [InlineData("https://github.com/example-owner/openspace", "example-owner/openspace")]
    [InlineData("git@github.com:example-owner/open.space.git\n", "example-owner/open.space")]
    [InlineData("ssh://git@github.com/example-owner/openspace.git", "example-owner/openspace")]
    [InlineData("https://gitlab.example/example-owner/openspace.git", null)]
    public void The_repository_comes_from_a_github_address(string url, string? repository) =>
        Assert.Equal(repository, GitHubReleases.RepositoryOf(url));

    [Fact]
    public async Task The_latest_release_is_read_from_github_without_its_v()
    {
        var github = new FakeGitHub(HttpStatusCode.OK, """
            {"tag_name":"v0.10.0","name":"Openspace 0.10.0","body":"- Rooms can be renamed","html_url":"https://github.com/example-owner/openspace/releases/tag/v0.10.0","published_at":"2026-03-01T10:00:00Z"}
            """);

        var release = await Releases(github, "example-owner/openspace").LatestAsync(default);

        Assert.Equal(new Release("0.10.0", "Openspace 0.10.0", "- Rooms can be renamed", new Uri("https://github.com/example-owner/openspace/releases/tag/v0.10.0"), new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)), release);
        Assert.Equal("https://api.github.com/repos/example-owner/openspace/releases/latest", github.Asked!.ToString());
    }

    [Fact]
    public async Task No_release_yet_or_no_repository_is_no_news()
    {
        Assert.Null(await Releases(new FakeGitHub(HttpStatusCode.NotFound, "{}"), "example-owner/openspace").LatestAsync(default));
        var untouched = new FakeGitHub(HttpStatusCode.OK, "{}");
        Assert.Null(await Releases(untouched, "not a repository; rm -rf").LatestAsync(default));
        Assert.Null(untouched.Asked);
        Assert.Null(GitHubReleases.Read(JsonDocument.Parse("""{"name":"no tag"}""").RootElement));
    }

    private static GitHubReleases Releases(FakeGitHub github, string repository) =>
        new(new HttpClient(github), new LiveOptions<ReleaseOptions>(new ReleaseOptions { Repository = repository }), new Host());

    private sealed class FakeGitHub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";

        public string ApplicationName { get; set; } = "Openspace";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
