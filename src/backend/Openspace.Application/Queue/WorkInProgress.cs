using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Queue;

/// <summary>Finds the file paths a person or a plan wrote in a task's words.</summary>
public static partial class PathTokens
{
    // Bare names that look like files but are web addresses far more often.
    private static readonly HashSet<string> NotFiles = new(StringComparer.OrdinalIgnoreCase) { "com", "org", "net", "io" };

    // Abbreviations with a dot inside, once the sentence's last dot is trimmed.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase) { "e.g", "i.e", "a.k.a", "p.s" };

    /// <summary>
    /// The paths named in <paramref name="text"/>, relative to the repository: <c>src/web/cart.ts</c>,
    /// a folder as <c>src/web/</c> (also from a glob such as <c>src/web/**</c>), or a bare file name
    /// such as <c>cart.ts</c>. Web addresses, e-mail addresses and paths outside the repository are
    /// left out: a wrong guess here makes a task wait for nothing.
    /// </summary>
    public static IReadOnlyList<string> Extract(string? text, string? repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var root = repositoryPath?.Replace('\\', '/').TrimEnd('/');
        var found = new List<string>();
        foreach (Match match in Candidate().Matches(Url().Replace(text, " ")))
        {
            if (Clean(match.Value, root) is { } path && !found.Contains(path, StringComparer.Ordinal))
            {
                found.Add(path);
            }
        }

        return found;
    }

    private static string? Clean(string token, string? root)
    {
        if (token.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        var path = token.Replace('\\', '/').TrimEnd('.', ':');
        if (path.StartsWith('/') || path.StartsWith('~'))
        {
            // An absolute path counts only inside the office's own repository.
            if (root is null || !path.StartsWith(root + "/", StringComparison.Ordinal))
            {
                return null;
            }

            path = path[(root.Length + 1)..];
        }

        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        // A glob stands for the folder before its first wildcard.
        var glob = path.IndexOfAny(['*', '?']);
        var isFolder = path.EndsWith('/');
        if (glob >= 0)
        {
            path = path[..glob];
            var slash = path.LastIndexOf('/');
            if (slash < 0)
            {
                return null;
            }

            path = path[..(slash + 1)];
            isFolder = true;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
        {
            return null;
        }

        if (isFolder)
        {
            // "and/or" is not a folder, "src/web/" is: a folder needs its slash at the end.
            return string.Join('/', segments) + "/";
        }

        if (!HasExtension(segments[^1])
            || (segments.Length == 1 && (NotFiles.Contains(segments[0][(segments[0].LastIndexOf('.') + 1)..]) || Abbreviations.Contains(segments[0]))))
        {
            return null;
        }

        return string.Join('/', segments);
    }

    private static bool HasExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return false;
        }

        // "v1.2" is a version, "cart.ts" a file: an extension has a letter.
        var extension = name[(dot + 1)..];
        return extension.Length <= 10 && extension.Any(char.IsLetter) && extension.All(char.IsLetterOrDigit);
    }

    [GeneratedRegex(@"[A-Za-z0-9_\-./\\*?~+]+")]
    private static partial Regex Candidate();

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9+.\-]*://\S+")]
    private static partial Regex Url();
}

/// <summary>When two lists of paths, files or folders, touch the same files.</summary>
public static class PathOverlap
{
    /// <summary>The paths of <paramref name="theirs"/> that touch any of <paramref name="mine"/>, in their order.</summary>
    public static IReadOnlyList<string> Shared(IEnumerable<string> mine, IEnumerable<string> theirs)
    {
        var wanted = mine.ToList();
        return [.. theirs.Where(t => wanted.Exists(w => Touch(w, t) || Touch(t, w))).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Whether <paramref name="pattern"/> (a file, a folder ending in a slash, a bare name) covers <paramref name="path"/>.</summary>
    private static bool Touch(string pattern, string path)
    {
        if (pattern.EndsWith('/'))
        {
            return path.StartsWith(pattern, StringComparison.Ordinal);
        }

        return pattern.Contains('/', StringComparison.Ordinal)
            ? string.Equals(pattern, path, StringComparison.Ordinal)
            : string.Equals(pattern, path, StringComparison.Ordinal) || path.EndsWith("/" + pattern, StringComparison.Ordinal);
    }
}

/// <summary>A running task with what it changed so far and what its words said it would touch.</summary>
/// <param name="Changed">Files changed in its working copy since it left the repository.</param>
/// <param name="Named">Paths its title and description name.</param>
public sealed record RunningWork(WorkInFlight Work, IReadOnlyList<string> Changed, IReadOnlyList<string> Named);

/// <summary>
/// Who is changing what, as <see cref="NowKeeper"/> last saw it: kept in memory for the queue,
/// the briefing and the agents' tools, which read it far more often than git could answer.
/// </summary>
public sealed class WorkInProgress : IWorkInProgress
{
    private readonly Lock _gate = new();
    private IReadOnlyList<RunningWork> _running = [];
    private string? _written;

    public IReadOnlyList<WorkInFlight> Current()
    {
        lock (_gate)
        {
            return [.. _running.Select(r => r.Work)];
        }
    }

    /// <summary>The running tasks with their changed and named files apart.</summary>
    public IReadOnlyList<RunningWork> Running()
    {
        lock (_gate)
        {
            return _running;
        }
    }

    public void Replace(IReadOnlyList<RunningWork> running)
    {
        ArgumentNullException.ThrowIfNull(running);
        lock (_gate)
        {
            _running = running;
        }
    }

    /// <summary>
    /// Records <paramref name="body"/> as what <c>Now.md</c> says; false when it already said so,
    /// so the vault gets a commit only when something changed.
    /// </summary>
    internal bool MarkWritten(string body)
    {
        lock (_gate)
        {
            if (_written == body)
            {
                return false;
            }

            _written = body;
            return true;
        }
    }

    internal bool KnowsWritten
    {
        get
        {
            lock (_gate)
            {
                return _written is not null;
            }
        }
    }
}

/// <summary>
/// Reads, for each running task, the files changed in its working copy and the paths its words
/// name, keeps them in <see cref="WorkInProgress"/> and writes <c>Now.md</c> when they change.
/// Openspace is the note's only author.
/// </summary>
public sealed partial class NowKeeper(
    IWorkTaskRepository tasks,
    IOfficeRepository offices,
    IWorkspaceManager workspaces,
    WorkInProgress work,
    IVault vault,
    ILogger<NowKeeper> logger)
{
    public const string NotePath = "Now.md";
    public const string Author = "Openspace";

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var known = work.Running().ToDictionary(r => r.Work.TaskId);
        var cache = new Dictionary<OfficeId, Office?>();
        var list = new List<RunningWork>();
        foreach (var task in running)
        {
            if (!cache.TryGetValue(task.OfficeId, out var office))
            {
                office = await offices.FindAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
                cache[task.OfficeId] = office;
            }

            if (office is null)
            {
                continue;
            }

            var changed = known.TryGetValue(task.Id.Value, out var before) ? before.Changed : [];
            if (task.Run is { } run && office.RepositoryPath is { } repository)
            {
                try
                {
                    changed = await workspaces.ChangedFilesAsync(repository, run.WorkspacePath, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A copy being created or removed right now: keep what was seen last.
                    LogNoChanges(logger, task.Title, ex);
                }
            }

            list.Add(Describe(task, office, changed));
        }

        work.Replace(list);
        await WriteNoteAsync(list, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What a task is about to touch, before git has seen anything: its words only.</summary>
    public static RunningWork Describe(WorkTask task, Office office, IReadOnlyList<string> changed)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(changed);
        var named = PathTokens.Extract($"{task.Title}\n{task.Description}", office.RepositoryPath);
        var (room, desk) = task.DeskId is { } id && office.HasDesk(id)
            ? (office.Locate(id).Room.Name, office.Locate(id).Desk.Name)
            : (string.Empty, "No desk");
        var paths = changed.Concat(named.Where(n => !changed.Contains(n, StringComparer.Ordinal))).ToList();
        return new RunningWork(
            new WorkInFlight(task.Id.Value, office.Id.Value, office.Name, room, desk, task.Title, paths), changed, named);
    }

    private async Task WriteNoteAsync(IReadOnlyList<RunningWork> list, CancellationToken cancellationToken)
    {
        var body = NowNote.Body(list);
        try
        {
            // After a restart, a note that already says the same thing is not written again.
            if (!work.KnowsWritten && await vault.ReadAsync(NotePath, cancellationToken).ConfigureAwait(false) is { } note
                && Same(note.Body, body))
            {
                work.MarkWritten(body);
                return;
            }

            // Marked before writing: a vault that cannot be written (no git, no rights) is tried
            // again when the content changes, not every few seconds.
            if (!work.MarkWritten(body))
            {
                return;
            }

            var now = Note.Create(NotePath, "Now", body) with { Kind = "note", Author = Author };
            await vault.WriteAsync(now, Author, cancellationToken, "updated who is changing what").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotWritten(logger, ex);
        }
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.ReplaceLineEndings("\n").TrimEnd(), b.TrimEnd(), StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The changed files of \"{Title}\" could not be read")]
    private static partial void LogNoChanges(ILogger logger, string title, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Now.md could not be written in the brain's vault")]
    private static partial void LogNotWritten(ILogger logger, Exception exception);
}

/// <summary>The Markdown of <c>Now.md</c>: office, then desk, then task, then its files.</summary>
public static class NowNote
{
    public const string Nobody = "Nobody is working right now.";

    public static string Body(IReadOnlyList<RunningWork> running)
    {
        ArgumentNullException.ThrowIfNull(running);
        var text = new StringBuilder();
        text.Append("# Now\n\n")
            .Append("Written by Openspace only: the tasks running right now and the files each is changing.\n")
            .Append("Edits made here are replaced on the next update.\n\n");
        if (running.Count == 0)
        {
            return text.Append(Nobody).Append('\n').ToString();
        }

        // No times in the note: it is rewritten only when the work itself changes.
        foreach (var office in running.GroupBy(r => r.Work.Office).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            text.Append("## ").Append(office.Key).Append("\n\n");
            foreach (var desk in office.GroupBy(r => (r.Work.Room, r.Work.Desk)).OrderBy(g => g.Key.Room, StringComparer.Ordinal).ThenBy(g => g.Key.Desk, StringComparer.Ordinal))
            {
                text.Append("### ").Append(desk.Key.Desk);
                if (desk.Key.Room.Length > 0)
                {
                    text.Append(" (").Append(desk.Key.Room).Append(')');
                }

                text.Append("\n\n");
                foreach (var task in desk.OrderBy(r => r.Work.Title, StringComparer.Ordinal))
                {
                    text.Append("**").Append(task.Work.Title).Append("**\n\n");
                    if (task.Work.Paths.Count == 0)
                    {
                        text.Append("- nothing changed yet\n\n");
                        continue;
                    }

                    foreach (var path in task.Work.Paths)
                    {
                        text.Append("- `").Append(path).Append('`');
                        if (!task.Changed.Contains(path, StringComparer.Ordinal))
                        {
                            text.Append(" (named in the task, not changed yet)");
                        }

                        text.Append('\n');
                    }

                    text.Append('\n');
                }
            }
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }
}
