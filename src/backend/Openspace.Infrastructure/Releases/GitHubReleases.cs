using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Openspace.Application.Releases;

namespace Openspace.Infrastructure.Releases;

/// <summary>
/// Releases published on GitHub, read from its public API without a key. The repository
/// is the one in the settings, or else the one this copy of Openspace was cloned from.
/// </summary>
internal sealed partial class GitHubReleases(HttpClient http, IOptionsMonitor<ReleaseOptions> options, IHostEnvironment host) : IReleaseSource
{
    public async Task<Release?> LatestAsync(CancellationToken cancellationToken)
    {
        var repository = options.CurrentValue.Repository is { Length: > 0 } named ? named.Trim() : await OriginAsync(host.ContentRootPath, cancellationToken).ConfigureAwait(false);
        if (repository is null || !RepositoryName().IsMatch(repository))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{repository}/releases/latest"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        return Read(json.RootElement);
    }

    internal static Release? Read(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out var tag) || tag.GetString() is not { Length: > 0 } name
            || !release.TryGetProperty("html_url", out var link) || !Uri.TryCreate(link.GetString(), UriKind.Absolute, out var url))
        {
            return null;
        }

        string Text(string property) => release.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        var published = DateTimeOffset.TryParse(Text("published_at"), out var at) ? at : (DateTimeOffset?)null;
        return new Release(name.TrimStart('v', 'V'), Text("name") is { Length: > 0 } title ? title : name, Text("body"), url, published);
    }

    /// <summary><c>owner/name</c> of the GitHub repository a folder was cloned from, if it was.</summary>
    internal static async Task<string?> OriginAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("git", ["-C", folder, "remote", "get-url", "origin"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var git = Process.Start(start);
            if (git is null)
            {
                return null;
            }

            var url = await git.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await git.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return git.ExitCode == 0 ? RepositoryOf(url) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary><c>owner/name</c> from a GitHub address, https or ssh; null for anything else.</summary>
    internal static string? RepositoryOf(string url)
    {
        var match = GitHubUrl().Match(url.Trim());
        return match.Success ? $"{match.Groups["owner"].Value}/{match.Groups["name"].Value}" : null;
    }

    [GeneratedRegex(@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)(?<owner>[A-Za-z0-9-]+)/(?<name>[A-Za-z0-9._-]+?)(?:\.git)?/?$")]
    private static partial Regex GitHubUrl();

    [GeneratedRegex(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$")]
    private static partial Regex RepositoryName();
}
