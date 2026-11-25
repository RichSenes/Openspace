using System.Globalization;
using System.Text;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>A task one desk just finished that another desk's task builds on: the two meet and hand it over.</summary>
public sealed record Handover(WorkTask Done, WorkTask Next);

/// <summary>
/// Hand-overs between desks, found by code: a task done and merged in the last minutes, and a
/// task of another desk, still to do, that waits for it. Each gets a short note in the brain,
/// linked to both, written once.
/// </summary>
public static class Handovers
{
    public const string Folder = "Handovers";

    /// <summary>How long after the work is done the two still meet about it.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(20);

    /// <summary>The hand-overs of the last <see cref="Fresh"/> minutes among some tasks, oldest first.</summary>
    /// <param name="all">The tasks to look among: the done ones and those that wait for them.</param>
    public static IReadOnlyList<Handover> Recent(IReadOnlyList<WorkTask> all, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(all);
        var byId = all.ToDictionary(t => t.Id);
        var found = new List<Handover>();
        foreach (var next in all.Where(t => !t.IsClosed && t.DeskId is not null && t.DependsOn.Count > 0))
        {
            foreach (var id in next.DependsOn)
            {
                if (byId.TryGetValue(id, out var done) && done.IsDoneForOthers && done.DeskId is { } from && from != next.DeskId
                    && now - done.StatusChangedAt <= Fresh && done.StatusChangedAt <= now)
                {
                    found.Add(new Handover(done, next));
                }
            }
        }

        return [.. found.OrderBy(h => h.Done.StatusChangedAt).ThenBy(h => h.Next.Id.Value)];
    }

    /// <summary>Where a hand-over's note lives: the day, the two desks and what comes next; the same for the same pair.</summary>
    public static string PathFor(Handover handover, string fromDesk, string toDesk, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(handover);
        var day = TimeZoneInfo.ConvertTime(handover.Done.StatusChangedAt, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{Folder}/{day}-{Agreements.Slug($"{fromDesk} to {toDesk} {handover.Next.Title}")}-{handover.Next.Key[^4..]}.md";
    }

    public static string Title(Handover handover, string fromDesk, string toDesk)
    {
        ArgumentNullException.ThrowIfNull(handover);
        return $"{fromDesk} hands “{handover.Done.Title}” to {toDesk}";
    }

    /// <summary>The note: what was done, what builds on it, what to read first.</summary>
    public static string Body(Handover handover, string office, string fromDesk, string toDesk, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(handover);
        var (done, next) = (handover.Done, handover.Next);
        var when = TimeZoneInfo.ConvertTime(done.StatusChangedAt, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"# {Title(handover, fromDesk, toDesk)}").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"In {office}, {when}. {fromDesk} finished a task that {toDesk}'s next one builds on: they met to hand it over.")
            .AppendLine()
            .AppendLine("## Done").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"“{done.Title}”, by {fromDesk}{(done.MergedInto is { Length: > 0 } into ? $", merged into {into}" : string.Empty)}.");
        if (Excerpt(done.Description) is { } what)
        {
            body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"> {what}");
        }

        body.AppendLine().AppendLine("## Next").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"“{next.Title}”, for {toDesk}: it waited for this and can go ahead now.");
        if (Excerpt(next.Description) is { } plan)
        {
            body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"> {plan}");
        }

        body.AppendLine().AppendLine("Written by Openspace from the two tasks, without a model.");
        return body.ToString();
    }

    private static string? Excerpt(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length == 0 ? null : line.Length <= 280 ? line : string.Concat(line.AsSpan(0, 277).TrimEnd(), "…");
    }
}

/// <summary>Writes the note of every fresh hand-over that has none yet; run with the queue.</summary>
public sealed class HandoverKeeper(IOfficeRepository offices, IWorkTaskRepository tasks, IVault vault, TimeProvider clock)
{
    public const string Author = "Openspace";

    /// <returns>The notes written in this round.</returns>
    public async Task<IReadOnlyList<string>> RoundAsync(IReadOnlyList<WorkTask> waiting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waiting);
        var written = new List<string>();
        var now = clock.GetUtcNow();
        foreach (var handover in await FindAsync(tasks, waiting, now, cancellationToken).ConfigureAwait(false))
        {
            var office = await offices.FindAsync(handover.Next.OfficeId, cancellationToken).ConfigureAwait(false);
            if (office is null || !office.HasDesk(handover.Done.DeskId!.Value) || !office.HasDesk(handover.Next.DeskId!.Value))
            {
                continue;
            }

            var (from, to) = (office.Locate(handover.Done.DeskId!.Value).Desk.Name, office.Locate(handover.Next.DeskId!.Value).Desk.Name);
            var path = Handovers.PathFor(handover, from, to, clock.LocalTimeZone);
            if (await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not null)
            {
                continue;
            }

            var note = new Note(path, "handover", NoteStatus.Accepted, Handovers.Title(handover, from, to), [office.Name], [], [], null, null,
                $"tasks {handover.Done.Id.Value} {handover.Next.Id.Value}", Author, Handovers.Body(handover, office.Name, from, to, clock.LocalTimeZone), []);
            await vault.WriteAsync(note, Author, cancellationToken, $"hand-over {path}").ConfigureAwait(false);
            written.Add(path);
        }

        return written;
    }

    /// <summary>The fresh hand-overs to some tasks still to do: the tasks they wait for are looked up.</summary>
    public static async Task<IReadOnlyList<Handover>> FindAsync(IWorkTaskRepository tasks, IReadOnlyList<WorkTask> waiting, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(waiting);
        var all = new Dictionary<WorkTaskId, WorkTask>();
        foreach (var task in waiting.Where(t => t.DependsOn.Count > 0))
        {
            all[task.Id] = task;
            foreach (var id in task.DependsOn)
            {
                if (!all.ContainsKey(id) && await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } done)
                {
                    all[id] = done;
                }
            }
        }

        return all.Count == 0 ? [] : Handovers.Recent([.. all.Values], now);
    }
}
