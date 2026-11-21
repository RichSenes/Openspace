using System.Globalization;
using System.Text.RegularExpressions;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Git;

namespace Openspace.Infrastructure.Brain;

/// <summary>
/// Reads a note's history from the vault's git. Git is asked for fields split by control
/// characters no commit message carries, so a summary with any punctuation in it reads back
/// whole; paths are taken literally, so a note named with a star is not read as a pattern.
/// </summary>
internal sealed partial class GitVaultHistory(IVault vault) : IVaultHistory
{
    /// <summary>The longest text and diff sent back: a wiki page, not a download.</summary>
    public const int MaxText = 512 * 1024;
    public const int MaxDiff = 256 * 1024;

    private const char Field = '\u001f';
    private const char Record = '\u001e';
    private const string Format = "--format=%H%x1f%an%x1f%aI%x1f%s%x1e";

    public async Task<IReadOnlyList<NoteCommit>> HistoryAsync(string path, int limit, CancellationToken cancellationToken)
    {
        var relative = VaultPath.Normalize(path);
        if (!await HasHistoryAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var log = await GitAsync(cancellationToken,
            "log", "-n", limit.ToString(CultureInfo.InvariantCulture), "--no-color", Format, "--", relative).ConfigureAwait(false);
        return Parse(log);
    }

    public async Task<NoteVersion?> VersionAsync(string path, string commit, CancellationToken cancellationToken)
    {
        var relative = VaultPath.Normalize(path);

        // Only a hash: anything else could be read by git as an option or a range.
        if (string.IsNullOrWhiteSpace(commit) || !Hash().IsMatch(commit) || !await HasHistoryAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string full;
        try
        {
            full = (await GitAsync(cancellationToken, "rev-parse", "--verify", "--quiet", $"{commit}^{{commit}}").ConfigureAwait(false)).Trim();
        }
        catch (InvalidOperationException)
        {
            // Not a commit of this vault.
            return null;
        }

        var meta = Parse(await GitAsync(cancellationToken, "show", "-s", "--no-color", Format, full).ConfigureAwait(false));
        if (meta.Count == 0)
        {
            return null;
        }

        // ls-tree answers with nothing when the file is not there, where cat-file would fail.
        var present = !string.IsNullOrWhiteSpace(await GitAsync(cancellationToken, "ls-tree", full, "--", relative).ConfigureAwait(false));
        var text = present ? await GitAsync(cancellationToken, "cat-file", "blob", $"{full}:{relative}").ConfigureAwait(false) : null;

        // Against the first parent, so a merge reads like any other change; from nothing for the first commit.
        var diff = await GitAsync(cancellationToken,
            "show", "--format=", "--no-color", "--no-ext-diff", "--no-textconv", "--diff-merges=first-parent", "--unified=3", full, "--", relative).ConfigureAwait(false);

        var touching = (await GitAsync(cancellationToken, "log", "-n", "2", "--format=%H", full, "--", relative).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var previous = touching.FirstOrDefault(h => h != full);

        var truncated = (text?.Length ?? 0) > MaxText || diff.Length > MaxDiff;
        return new NoteVersion(meta[0], relative, Cut(text, MaxText), Cut(diff, MaxDiff)!, previous, truncated);
    }

    /// <summary>Commits read from git's output in <see cref="Format"/>; records that do not fit are skipped.</summary>
    internal static IReadOnlyList<NoteCommit> Parse(string output)
    {
        var commits = new List<NoteCommit>();
        foreach (var record in output.Split(Record))
        {
            var fields = record.TrimStart('\r', '\n').Split(Field);
            if (fields.Length != 4 || !Hash().IsMatch(fields[0])
                || !DateTimeOffset.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            commits.Add(new NoteCommit(fields[0], fields[1], date, Summary(fields[1], fields[3].Trim())));
        }

        return commits;
    }

    /// <summary>Openspace's commits say "Author: what"; the author is already its own field.</summary>
    private static string Summary(string author, string subject) =>
        subject.StartsWith(author + ": ", StringComparison.Ordinal) && subject.Length > author.Length + 2
            ? subject[(author.Length + 2)..]
            : subject;

    private static string? Cut(string? text, int max) => text is null || text.Length <= max ? text : text[..max];

    /// <summary>Whether there is a repository with at least one commit to read.</summary>
    private async Task<bool> HasHistoryAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(vault.Folder, ".git")))
        {
            return false;
        }

        try
        {
            await GitAsync(cancellationToken, "rev-parse", "--verify", "--quiet", "HEAD").ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException)
        {
            // A repository with no commit yet.
            return false;
        }
    }

    private Task<string> GitAsync(CancellationToken cancellationToken, params string[] args) =>
        GitCli.RunAsync(vault.Folder, cancellationToken, ["--literal-pathspecs", .. args]);

    [GeneratedRegex("^[0-9a-fA-F]{4,64}$")]
    private static partial Regex Hash();
}
