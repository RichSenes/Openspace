using System.Globalization;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Upkeep;

/// <summary>Counts, through git, the work of a repository that no remote has: never a file name or a line of it.</summary>
internal sealed class RepositoryStates : IRepositoryStates
{
    public async Task<RepositoryState?> ReadAsync(string folder, CancellationToken cancellationToken)
    {
        async Task<string?> Git(params string[] arguments) =>
            await Commands.RunAsync("git", ["-C", folder, .. arguments], cancellationToken).ConfigureAwait(false) is { ExitCode: 0 } result ? result.Output.Trim() : null;

        if (await Git("rev-parse", "--is-inside-work-tree").ConfigureAwait(false) != "true")
        {
            return null;
        }

        var branch = await Git("rev-parse", "--abbrev-ref", "HEAD").ConfigureAwait(false) ?? "?";
        var changed = Commands.Lines(await Git("status", "--porcelain").ConfigureAwait(false) ?? string.Empty).Length;
        var remotes = Commands.Lines(await Git("remote").ConfigureAwait(false) ?? string.Empty).Length;

        // Commits of the current branch that no remote branch holds; a branch never committed to has none.
        var unpushed = int.TryParse(await Git("rev-list", "--count", "HEAD", "--not", "--remotes").ConfigureAwait(false), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
        return new RepositoryState(branch, changed, unpushed, remotes > 0);
    }
}
