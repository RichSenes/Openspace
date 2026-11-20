using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.Skills;

namespace Openspace.Infrastructure.Brain;

/// <summary>
/// The vault as a folder of Markdown files with its own git. Files are the truth: Openspace writes
/// a note whole or not at all, and commits only the note it wrote, so the person's own uncommitted
/// edits elsewhere in the vault are left as they are.
/// </summary>
internal sealed class FileVault(IOptions<BrainOptions> options) : IVault, IDisposable
{
    /// <summary>Who commits: Openspace itself, whoever the note's author is.</summary>
    public const string Committer = "Openspace";

    private const string Email = "openspace@localhost";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // One write at a time: git keeps a single index and refuses a second commit while one runs.
    private readonly SemaphoreSlim _one = new(1, 1);

    public string Folder { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SkillCatalog.Expand(options.Value.Folder)));

    public void Dispose() => _one.Dispose();

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var created = new List<string>();
            foreach (var (path, text) in VaultTemplate.Files)
            {
                var full = Resolve(path);
                if (File.Exists(full))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await WriteWholeAsync(full, text, cancellationToken).ConfigureAwait(false);
                created.Add(path);
            }

            var fresh = await EnsureGitAsync(cancellationToken).ConfigureAwait(false);
            if (fresh)
            {
                // A folder the person already kept notes in starts its history with them.
                await GitCli.RunAsync(Folder, cancellationToken, "add", "--all").ConfigureAwait(false);
                await CommitAsync(Committer, "Openspace: created the vault", [], cancellationToken).ConfigureAwait(false);
            }
            else if (created.Count > 0)
            {
                await GitCli.RunAsync(Folder, cancellationToken, ["add", "--", .. created]).ConfigureAwait(false);
                await CommitAsync(Committer, "Openspace: added the missing parts of the vault", created, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _one.Release();
        }
    }

    public async Task<IReadOnlyList<Note>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var notes = new List<Note>();
        foreach (var full in Directory.EnumerateFiles(Folder, "*.md", SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(Folder, full).Replace('\\', '/');
            if (path.Split('/').Any(s => s.StartsWith('.')))
            {
                continue;
            }

            if (await ReadFileAsync(path, full, cancellationToken).ConfigureAwait(false) is { } note)
            {
                notes.Add(note);
            }
        }

        return [.. notes.OrderBy(n => n.Path, StringComparer.Ordinal)];
    }

    public Task<Note?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var relative = VaultPath.Normalize(path);
        return ReadFileAsync(relative, Resolve(relative), cancellationToken);
    }

    public async Task WriteAsync(Note note, string author, CancellationToken cancellationToken, string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(note);
        var path = VaultPath.Normalize(note.Path);
        var full = Resolve(path);
        var who = Name(author);

        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await WriteWholeAsync(full, VaultFormat.Write(note with { Path = path }), cancellationToken).ConfigureAwait(false);
            await EnsureGitAsync(cancellationToken).ConfigureAwait(false);
            await GitCli.RunAsync(Folder, cancellationToken, "add", "--", path).ConfigureAwait(false);
            var what = string.IsNullOrWhiteSpace(summary) ? $"wrote {path}" : summary.Trim().ReplaceLineEndings(" ");
            await CommitAsync(who, $"{who}: {what}", [path], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>The full path of a vault path, refusing whatever would land outside the folder.</summary>
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(Folder, relative));
        return full.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full
            : throw new Application.RejectedException(VaultPath.RejectedCode, $"\"{relative}\" is not inside the vault.");
    }

    private static async Task<Note?> ReadFileAsync(string path, string full, CancellationToken cancellationToken)
    {
        if (!File.Exists(full))
        {
            return null;
        }

        try
        {
            var text = await File.ReadAllTextAsync(full, cancellationToken).ConfigureAwait(false);
            return VaultFormat.Parse(path, text) with { Modified = File.GetLastWriteTimeUtc(full) };
        }
        catch (IOException)
        {
            // Deleted or moved while being read: the watcher will tell what became of it.
            return null;
        }
    }

    /// <summary>Writes beside the file and moves it over, so a reader sees the old note or the new one, never half.</summary>
    private static async Task WriteWholeAsync(string full, string text, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, Utf8, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Starts the vault's own repository when there is none; true when it was just made.</summary>
    private async Task<bool> EnsureGitAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(Path.Combine(Folder, ".git")))
        {
            return false;
        }

        await GitCli.RunAsync(Folder, cancellationToken, "init", "--quiet", "--initial-branch", "main").ConfigureAwait(false);

        // Only this repository's settings: the person's own git identity is not touched.
        await GitCli.RunAsync(Folder, cancellationToken, "config", "user.name", Committer).ConfigureAwait(false);
        await GitCli.RunAsync(Folder, cancellationToken, "config", "user.email", Email).ConfigureAwait(false);
        await GitCli.RunAsync(Folder, cancellationToken, "config", "commit.gpgsign", "false").ConfigureAwait(false);
        return true;
    }

    /// <summary>Commits what is staged for these paths (everything when none), and nothing when nothing changed.</summary>
    private async Task CommitAsync(string author, string message, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var staged = await GitCli.RunAsync(Folder, cancellationToken, ["diff", "--cached", "--name-only", "--", .. paths]).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(staged))
        {
            return;
        }

        // The identity is given on each commit too, so a vault whose settings were lost still commits
        // without asking for a name or a signing key.
        await GitCli.RunAsync(Folder, cancellationToken,
        [
            "-c", $"user.name={Committer}", "-c", $"user.email={Email}", "-c", "commit.gpgsign=false",
            "commit", "--quiet", "--no-verify", "--author", $"{author} <{Email}>", "--message", message, "--", .. paths,
        ]).ConfigureAwait(false);
    }

    /// <summary>An author as git can carry it: one line, no angle brackets.</summary>
    private static string Name(string author)
    {
        var name = new string([.. (author ?? "").Where(c => c is not '<' and not '>' && !char.IsControl(c))]).Trim();
        return name.Length > 0 ? name : Committer;
    }
}
