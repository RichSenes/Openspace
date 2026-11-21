using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>
/// What agents do with the shared brain: look things up, read a note, keep their own journal and
/// propose decisions; and what their briefing reads from it before they start. Agents only ever
/// write their own journal and proposals: accepting is Shiori's job, the person reviews afterwards.
/// </summary>
public sealed partial class AgentBrain(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IVault vault,
    IBrainIndex index,
    BrainSearch search,
    IWorkInProgress work,
    TimeProvider clock,
    ILogger<AgentBrain> logger)
{
    /// <summary>At most this many decisions go in a briefing; the rest are a search away.</summary>
    public const int BriefingDecisions = 8;

    /// <summary>At most this many of the room's expert notes go in a briefing; the rest are a search away.</summary>
    public const int BriefingNotes = 5;

    /// <summary>At most this many tasks in flight go in a briefing; the rest are in brain_now.</summary>
    public const int BriefingWork = 5;

    /// <summary>What an agent writes in a proposal's offices to mean every office.</summary>
    public const string EveryOffice = "*";

    private const int SearchHits = 10;

    /// <summary>Kinds an agent may propose, and the folder each goes in; the office's own notes live under its name.</summary>
    private static readonly Dictionary<string, string> Folders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["decision"] = "Decisions",
        ["plan"] = "Plans",
        ["study"] = "Studies",
        ["note"] = "Offices",
    };

    public async Task<string> SearchAsync(
        WorkTaskId taskId, string query, string? office, string? room, string? path, string? kind, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(taskId, cancellationToken).ConfigureAwait(false);

        // Scope only ranks: notes about the caller's own office and room come first unless it asks about others.
        var hits = await search.SearchAsync(
            new BrainQuery(query, Blank(office) ?? caller.Office.Name, Blank(room) ?? caller.Room.Name, Blank(path), Blank(kind), SearchHits),
            cancellationToken).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return $"No note matches \"{query}\". Try fewer or other words, or leave the kind out.";
        }

        var text = new StringBuilder();
        foreach (var hit in hits)
        {
            var what = string.Join(", ", new[] { hit.Kind, hit.Status }.Where(w => !string.IsNullOrEmpty(w)));
            text.AppendLine(CultureInfo.InvariantCulture, $"- {hit.Path} · {(what.Length > 0 ? what : "note")} · {hit.Title}{Scope(hit.Offices, hit.Rooms, hit.Paths)}");
            if (hit.Excerpt.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {hit.Excerpt}");
            }
        }

        return text.Append("Read a whole note with brain_read and its path.").ToString();
    }

    public async Task<string> ReadAsync(WorkTaskId taskId, string note, CancellationToken cancellationToken)
    {
        await CallerAsync(taskId, cancellationToken).ConfigureAwait(false);
        var found = await vault.ReadAsync(note, cancellationToken).ConfigureAwait(false)
            ?? throw new RejectedException("brain-note-missing", $"There is no note \"{note}\". brain_search finds notes by their words.");
        return VaultFormat.Write(found);
    }

    /// <summary>Appends a dated line to the calling desk's own journal, and only to that one.</summary>
    /// <returns>The journal's path.</returns>
    public async Task<string> JournalAsync(WorkTaskId taskId, string text, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(taskId, cancellationToken).ConfigureAwait(false);
        var line = Guard(text, "A journal entry", 2000).ReplaceLineEndings(" ");
        return await Journals.AppendAsync(
            vault, caller.Desk.Name, clock.GetLocalNow(), $"{caller.Task.Title}: {line}", caller.Desk.Name, "wrote in its journal", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a new note with <c>status: proposed</c>; it never changes an existing one.</summary>
    /// <param name="offices">The offices it is about; the caller's own when none is given, every office with <see cref="EveryOffice"/>.</param>
    /// <returns>The new note's path.</returns>
    public async Task<string> ProposeAsync(
        WorkTaskId taskId,
        string kind,
        string title,
        string body,
        IReadOnlyList<string>? offices,
        IReadOnlyList<string>? rooms,
        IReadOnlyList<string>? paths,
        string reasons,
        CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(taskId, cancellationToken).ConfigureAwait(false);
        var what = Blank(kind)?.ToLowerInvariant();
        if (what is null || !Folders.TryGetValue(what, out var folder))
        {
            throw new RejectedException(
                "brain-kind", $"Agents propose a decision, a plan, a study or a note, not \"{kind}\". The constitution is the person's; your journal goes through brain_journal.");
        }

        var name = Guard(title, "A proposal's title", 120).ReplaceLineEndings(" ");
        var text = Guard(body, "A proposal's body", 20000);
        var why = Guard(reasons, "A proposal's reasons", 4000);
        var stem = what == "note"
            ? $"{folder}/{Journals.FolderName(caller.Office.Name)}/{Slug(name)}"
            : $"{folder}/{clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{Slug(name)}";

        // A proposal only ever adds: a name already taken gets a number.
        var path = $"{stem}.md";
        for (var n = 2; await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not null; n++)
        {
            path = $"{stem}-{n}.md";
        }

        // "*" means every office, which the front matter says by naming none; the person then
        // decides, since an agent's word for every office is not Shiori's to accept.
        var scope = List(offices);
        IReadOnlyList<string> about = scope.Contains(EveryOffice) ? [] : scope.Count > 0 ? scope : [caller.Office.Name];
        var note = new Note(
            path, what, NoteStatus.Proposed, name,
            about, List(rooms), List(paths), null, null,
            $"task {caller.Task.Id.Value}", caller.Desk.Name,
            $"# {name}\n\n{text}\n\n## Reasons\n\n{why}\n", []);
        await vault.WriteAsync(note, caller.Desk.Name, cancellationToken, $"proposed {path}").ConfigureAwait(false);
        return path;
    }

    /// <summary>The work in flight in the caller's office, with the files each task is changing.</summary>
    public async Task<string> NowAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(taskId, cancellationToken).ConfigureAwait(false);
        var others = work.Current().Where(w => w.OfficeId == caller.Office.Id.Value && w.TaskId != caller.Task.Id.Value).ToList();
        if (others.Count == 0)
        {
            return "Nobody else is working in this office right now.";
        }

        var text = new StringBuilder().AppendLine("Working in this office right now:");
        foreach (var w in others)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {Doing(w, int.MaxValue)}");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// What a task's briefing reads from the brain: the rules, the decisions in force where it
    /// works and the work in flight next to it. Null when the brain has nothing to say, or cannot
    /// be read: the briefing then goes out as it always did, since the work matters more than the brain.
    /// </summary>
    public async Task<BrainBrief?> BriefAsync(Office office, Room room, WorkTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(task);
        try
        {
            var inFlight = work.Current();
            var mine = inFlight.FirstOrDefault(w => w.TaskId == task.Id.Value)?.Paths ?? [];
            var taskPaths = mine.Concat(NamedPaths($"{task.Title}\n{task.Description}")).Distinct(StringComparer.Ordinal).ToList();

            var rules = Rules((await vault.ReadAsync("Constitution.md", cancellationToken).ConfigureAwait(false))?.Body);
            var (decisions, more) = await DecisionsAsync(office, room, taskPaths, cancellationToken).ConfigureAwait(false);
            var (notes, moreNotes) = await ExpertNotesAsync(office, room, cancellationToken).ConfigureAwait(false);
            var nearby = inFlight
                .Where(w => w.OfficeId == office.Id.Value && w.TaskId != task.Id.Value)
                .Select(w => (Work: w, Shared: Shared(w.Paths, taskPaths)))
                .Where(x => x.Shared.Count > 0 || string.Equals(x.Work.Room, room.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Shared.Count)
                .ToList();

            return rules.Count == 0 && decisions.Count == 0 && notes.Count == 0 && nearby.Count == 0
                ? null
                : new BrainBrief(rules, decisions, more, [.. nearby.Select(x => new NearbyWork(Doing(x.Work, 4), x.Shared))], notes, moreNotes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogBrainUnreadable(logger, ex, task.Id.Value);
            return null;
        }
    }

    /// <summary>
    /// The accepted decisions that apply where the task works: not about another office, and about
    /// its room or its files, or open on both. Those naming its files come first, then its room,
    /// then its office, then the newest.
    /// </summary>
    private async Task<(IReadOnlyList<BriefDecision> Shown, int More)> DecisionsAsync(
        Office office, Room room, List<string> taskPaths, CancellationToken cancellationToken)
    {
        static bool Names(IReadOnlyList<string> scope, string name) => scope.Any(s => string.Equals(s.Trim(), name, StringComparison.OrdinalIgnoreCase));

        var found = await index.FindAsync([], "decision", cancellationToken).ConfigureAwait(false);
        var applying = found
            .Where(n => string.Equals(n.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase))
            .Where(n => n.Offices.Count == 0 || Names(n.Offices, office.Name))
            .Select(n =>
            {
                var roomHit = Names(n.Rooms, room.Name);
                var pathHit = n.Paths.Any(g => taskPaths.Any(p => BrainSearch.Covers(p, g)));

                // Files unknown: a decision about some files may well be about this task's, and is kept, last.
                var applies = roomHit || pathHit || (n.Rooms.Count == 0 && (n.Paths.Count == 0 || taskPaths.Count == 0));
                var score = (pathHit ? 6 : 0) + (roomHit ? 2 : 0) + (n.Offices.Count > 0 ? 1 : 0) + (n.Paths.Count == 0 ? 1 : 0);
                return (Note: n, Applies: applies, Score: score);
            })
            .Where(x => x.Applies)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Note.Modified)
            .ThenBy(x => x.Note.Path, StringComparer.Ordinal)
            .Select(x => x.Note)
            .ToList();

        var shown = new List<BriefDecision>();
        foreach (var n in applying.Take(BriefingDecisions))
        {
            var note = await vault.ReadAsync(n.Path, cancellationToken).ConfigureAwait(false);
            shown.Add(new BriefDecision(n.Path, n.Title, Gist(note?.Body ?? n.Excerpt, n.Title)));
        }

        return (shown, applying.Count - shown.Count);
    }

    /// <summary>
    /// The accepted notes of the office (<c>Offices/&lt;office&gt;/</c>) about this room or about
    /// every room. Notes written by the room's seniors come first, the most senior first: someone
    /// who finished many of the room's tasks knows its corners. Then the newest.
    /// </summary>
    private async Task<(IReadOnlyList<BriefNote> Shown, int More)> ExpertNotesAsync(Office office, Room room, CancellationToken cancellationToken)
    {
        var folder = $"{Folders["note"]}/{Journals.FolderName(office.Name)}/";
        var found = (await index.FindAsync([], "note", cancellationToken).ConfigureAwait(false))
            .Where(n => string.Equals(n.Status, NoteStatus.Accepted, StringComparison.OrdinalIgnoreCase))
            .Where(n => n.Path.StartsWith(folder, StringComparison.Ordinal) && !n.Path.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase))
            .Where(n => n.Rooms.Count == 0 || n.Rooms.Any(r => string.Equals(r.Trim(), room.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (found.Count == 0)
        {
            return ([], 0);
        }

        // Seniority is only counted when there are notes to weigh: one query for the office's
        // finished tasks, counted by the room's desks, instead of a lookup per note.
        var deskNames = room.Desks.ToDictionary(d => d.Id, d => d.Name);
        var done = (await tasks.ListAsync(office.Id, WorkTaskStatus.Done, cancellationToken).ConfigureAwait(false))
            .Where(t => t.DeskId is { } id && deskNames.ContainsKey(id))
            .GroupBy(t => deskNames[t.DeskId!.Value], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var weighed = new List<(BriefNote Note, DateTimeOffset Modified)>();
        foreach (var n in found)
        {
            var note = await vault.ReadAsync(n.Path, cancellationToken).ConfigureAwait(false);
            var author = Blank(note?.Author);
            var seniority = author is not null && done.TryGetValue(author, out var count) ? count : 0;
            weighed.Add((new BriefNote(n.Path, n.Title, Gist(note?.Body ?? n.Excerpt, n.Title), author, seniority), n.Modified));
        }

        var ordered = weighed
            .OrderByDescending(x => x.Note.Seniority)
            .ThenByDescending(x => x.Modified)
            .ThenBy(x => x.Note.Path, StringComparer.Ordinal)
            .Select(x => x.Note)
            .ToList();
        return ([.. ordered.Take(BriefingNotes)], Math.Max(0, ordered.Count - BriefingNotes));
    }

    /// <summary>The numbered rules of the constitution, one line each.</summary>
    public static IReadOnlyList<string> Rules(string? constitution)
    {
        if (string.IsNullOrWhiteSpace(constitution))
        {
            return [];
        }

        var rules = new List<StringBuilder>();
        foreach (var line in constitution.ReplaceLineEndings("\n").Split('\n'))
        {
            var numbered = NumberedLine().Match(line);
            if (numbered.Success)
            {
                rules.Add(new StringBuilder($"{numbered.Groups[1].Value}. {numbered.Groups[2].Value.Trim()}"));
            }
            else if (rules.Count > 0 && line.Length > 0 && char.IsWhiteSpace(line[0]) && line.Trim().Length > 0)
            {
                // A rule wrapped over several lines.
                rules[^1].Append(' ').Append(line.Trim());
            }
            else if (rules.Count > 0 && line.Trim().Length > 0)
            {
                // The list ended: what follows is no longer a rule.
                break;
            }
        }

        return [.. rules.Select(r => r.ToString())];
    }

    /// <summary>File paths a task's text names, such as <c>src/web/cart.ts</c> or <c>docs/</c>: a slash and an extension or a trailing slash.</summary>
    public static IReadOnlyList<string> NamedPaths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. PathLike().Matches(text)
            .Where(m => m.Index < 1 || text[m.Index - 1] != '/')
            .Select(m => m.Value.TrimEnd('.', ','))
            .Where(p => !p.Contains("://", StringComparison.Ordinal) && (p.EndsWith('/') || p.Contains("**", StringComparison.Ordinal) || FileName().IsMatch(p)))
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The first line of a note's text that says something: no heading, no front matter.</summary>
    private static string Gist(string body, string title)
    {
        var line = body.ReplaceLineEndings("\n").Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '>', ' '))
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#') && l != title) ?? "";
        return line.Length > 160 ? line[..160].TrimEnd() + "…" : line;
    }

    /// <summary>The files two lists have in common, a folder or glob on either side covering a file on the other.</summary>
    private static List<string> Shared(IReadOnlyList<string> theirs, IReadOnlyList<string> mine) =>
        [.. theirs.Where(t => mine.Any(m => BrainSearch.Covers(t, m) || BrainSearch.Covers(m, t))).Distinct(StringComparer.Ordinal)];

    private static string Doing(WorkInFlight w, int maxPaths)
    {
        var files = w.Paths.Count == 0 ? "no files changed yet"
            : "changing " + string.Join(", ", w.Paths.Take(maxPaths)) + (w.Paths.Count > maxPaths ? $" and {w.Paths.Count - maxPaths} more" : "");
        return $"\"{w.Title}\" at the desk \"{w.Desk}\" (room {w.Room}): {files}";
    }

    private static string Scope(IReadOnlyList<string> offices, IReadOnlyList<string> rooms, IReadOnlyList<string> paths)
    {
        var parts = offices.Concat(rooms).Concat(paths).ToList();
        return parts.Count == 0 ? "" : $" ({string.Join(", ", parts)})";
    }

    /// <summary>A title as a file name: lowercase words joined by dashes, accents dropped.</summary>
    private static string Slug(string title)
    {
        var plain = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = Dashes().Replace(plain.ToString(), "-").Trim('-');
        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        return slug.Length > 0 ? slug : "proposal";
    }

    private static List<string> List(IReadOnlyList<string>? items) =>
        items is null ? [] : [.. items.Select(i => i.Trim()).Where(i => i.Length > 0).Distinct(StringComparer.Ordinal)];

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Guard(string? text, string what, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RejectedException("empty-text", $"{what} cannot be empty.");
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private sealed record Caller(WorkTask Task, Office Office, Room Room, Desk Desk);

    /// <summary>The brain answers a task only while its agent works on it: that is who signs what it writes.</summary>
    private async Task<Caller> CallerAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task is not { Status: WorkTaskStatus.Running, DeskId: not null })
        {
            throw new RejectedException("task-not-running", $"Task '{task.Title}' is {task.Status}: the brain's tools only work while its agent is working on it.");
        }

        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var (room, desk) = office.Locate(task.DeskId.Value);
        return new Caller(task, office, room, desk);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The brain could not be read for the briefing of task {TaskId}: it goes out without it")]
    private static partial void LogBrainUnreadable(ILogger logger, Exception exception, Guid taskId);


    [GeneratedRegex(@"^\s{0,3}(\d{1,3})[.)][ \t]+(\S.*)$")]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"(?:[\w.*-]+/)+[\w.*-]*")]
    private static partial Regex PathLike();

    [GeneratedRegex(@"\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex FileName();

    [GeneratedRegex("-+")]
    private static partial Regex Dashes();
}

/// <summary>What a briefing reads from the brain before the agent starts.</summary>
/// <param name="Rules">The constitution's rules, one line each.</param>
/// <param name="Decisions">The accepted decisions that apply, most relevant first.</param>
/// <param name="MoreDecisions">Decisions that apply too, left out to keep the briefing short.</param>
/// <param name="Nearby">Work in flight in the same room or on the same files.</param>
/// <param name="Notes">The room's expert notes, the seniors' first.</param>
/// <param name="MoreNotes">Expert notes left out to keep the briefing short.</param>
public sealed record BrainBrief(
    IReadOnlyList<string> Rules, IReadOnlyList<BriefDecision> Decisions, int MoreDecisions, IReadOnlyList<NearbyWork> Nearby,
    IReadOnlyList<BriefNote> Notes, int MoreNotes);

/// <param name="Gist">The decision's first line, so the agent knows whether to read it.</param>
public sealed record BriefDecision(string Path, string Title, string Gist);

/// <summary>A note of the office about the room, as the briefing names it.</summary>
/// <param name="Author">The inhabitant who wrote it, when the note says so.</param>
/// <param name="Seniority">How many of the room's tasks its author finished; 0 for anyone else.</param>
public sealed record BriefNote(string Path, string Title, string Gist, string? Author, int Seniority);

/// <param name="Doing">Which task, at which desk, changing which files.</param>
/// <param name="Shared">The files it shares with the task being briefed.</param>
public sealed record NearbyWork(string Doing, IReadOnlyList<string> Shared);
