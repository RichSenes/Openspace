using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Infrastructure.Git;

namespace Openspace.Infrastructure.Tests;

/// <summary>A stand-in gh that answers like the GitHub CLI, in a repository with a GitHub origin.</summary>
public sealed class GitHubPullRequestsTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("openspace-gh-").FullName;
    private string Repo => Path.Combine(_root, "repo");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Repo);
        await GitCli.RunAsync(Repo, default, "init", "--quiet");
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    private GitHubPullRequests WithFakeGh(string script)
    {
        var path = Path.Combine(_root, "gh");
        File.WriteAllText(path, "#!/bin/sh\n" + script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new GitHubPullRequests(Options.Create(new GitHubOptions { Command = path }));
    }

    [Fact]
    public async Task Pull_requests_need_a_GitHub_origin_and_the_CLI_signed_in()
    {
        var signedIn = WithFakeGh("exit 0\n");
        Assert.False(await signedIn.AvailableAsync(Repo, default));

        await GitCli.RunAsync(Repo, default, "remote", "add", "origin", "https://example.org/e-corp/shop.git");
        Assert.False(await signedIn.AvailableAsync(Repo, default));

        await GitCli.RunAsync(Repo, default, "remote", "set-url", "origin", "https://github.com/e-corp/shop.git");
        Assert.True(await signedIn.AvailableAsync(Repo, default));
        Assert.False(await WithFakeGh("echo 'not logged in' >&2; exit 1\n").AvailableAsync(Repo, default));
    }

    [Fact]
    public async Task Opening_returns_the_address_and_the_state_is_read_back()
    {
        var gh = WithFakeGh("""
            case "$1 $2" in
              "pr create") echo "Creating pull request for openspace/logout into main"; echo "https://github.com/e-corp/shop/pull/7" ;;
              "pr view") echo '{"state":"MERGED"}' ;;
            esac
            """);

        var url = await gh.OpenAsync(Repo, "openspace/logout", "main", "Add logout", "Body", default);

        Assert.Equal("https://github.com/e-corp/shop/pull/7", url);
        Assert.Equal(PullRequestState.Merged, await gh.StateAsync(Repo, url, default));
    }

    [Fact]
    public async Task A_refusal_from_GitHub_says_why()
    {
        var gh = WithFakeGh("echo 'a pull request for branch openspace/logout already exists' >&2; exit 1\n");

        var error = await Assert.ThrowsAsync<Application.RejectedException>(() => gh.OpenAsync(Repo, "openspace/logout", "main", "Add logout", "Body", default));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
    }
}
