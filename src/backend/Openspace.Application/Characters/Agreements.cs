using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Application.Functions;
using Openspace.Application.LocalModels;
using Openspace.Application.Queue;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Characters;

/// <summary>One side of an agreement, with what the constitution weighs: seniority, priority, age.</summary>
/// <param name="Done">Tasks the desk finished in the office: its seniority.</param>
/// <param name="Blocks">Tasks waiting for this one: its priority.</param>
public sealed record AgreementSide(DeskId Desk, string DeskName, WorkTask Task, int Done, int Blocks);

/// <param name="GoesFirst">Null when the rules did not settle it within the turns.</param>
public sealed record Negotiation(string FirstProposal, string SecondProposal, int Turns, WorkTaskId? GoesFirst, string Reason);

/// <param name="Shared">The files both tasks name.</param>
public sealed record Clash(WorkTask First, WorkTask Second, IReadOnlyList<string> Shared);

/// <summary>
/// Agreements between desks, computed by code: two queued tasks of different desks that name the
/// same files and whose desks are in at the same time clash; each side proposes to go first with
/// its grounds, and the constitution's order (seniority, then priority, then age) settles it in a
/// few turns. What is still even after <see cref="Agreement.MaxTurns"/> turns goes to the person.
/// </summary>
public static class Agreements
{
    public const string Folder = "Agreements";

    /// <summary>Two tasks queued closer than this are the same age.</summary>
    public static readonly TimeSpan SameAge = TimeSpan.FromHours(1);

    /// <summary>The clashes among an office's queued tasks, the oldest pair first.</summary>
    /// <param name="all">Every task of the office, for nothing but the queued ones and their desks.</param>
    public static IReadOnlyList<Clash> Clashes(Office office, IReadOnlyList<WorkTask> all)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(all);
        if (!office.HoldOverlappingWork)
        {
            return [];
        }

        var queued = all
            .Where(t => t.Status == WorkTaskStatus.Queued && t.OfficeId == office.Id && t.DeskId is { } d && office.HasDesk(d))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id.Value)
            .Select(t => (Task: t, Paths: PathTokens.Extract($"{t.Title}\n{t.Description}", office.RepositoryPath), Shift: office.Locate(t.DeskId!.Value).Desk.Shift))
            .Where(x => x.Paths.Count > 0)
            .ToList();
        var clashes = new List<Clash>();
        for (var i = 0; i < queued.Count; i++)
        {
            for (var j = i + 1; j < queued.Count; j++)
            {
                var (a, b) = (queued[i], queued[j]);
                if (a.Task.DeskId == b.Task.DeskId || !AtTheSameTime(a.Shift, b.Shift))
                {
                    continue;
                }

                var shared = PathOverlap.Shared(a.Paths, b.Paths);
                if (shared.Count > 0)
                {
                    clashes.Add(new Clash(a.Task, b.Task, shared));
                }
            }
        }

        return clashes;
    }

    /// <summary>A day desk and a night desk are never in together: their work never runs side by side.</summary>
    public static bool AtTheSameTime(Shift a, Shift b) => !((a == Shift.Day && b == Shift.Night) || (a == Shift.Night && b == Shift.Day));

    /// <summary>
    /// The turns: both propose, then seniority, priority and age are weighed one per turn. The
    /// first that differs settles it; when none does, the person is asked.
    /// </summary>
    public static Negotiation Negotiate(AgreementSide first, AgreementSide second, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(zone);
        var (p1, p2) = (Proposal(first, zone), Proposal(second, zone));

        if (first.Done != second.Done)
        {
            var senior = first.Done > second.Done ? first : second;
            return new Negotiation(p1, p2, 2, senior.Task.Id, $"Seniority: {senior.DeskName} finished {Tasks(senior.Done)} in the office, so “{senior.Task.Title}” goes first.");
        }

        if (first.Blocks != second.Blocks)
        {
            var urgent = first.Blocks > second.Blocks ? first : second;
            return new Negotiation(p1, p2, 3, urgent.Task.Id, $"Priority: {Tasks(urgent.Blocks)} wait for “{urgent.Task.Title}”, so it goes first.");
        }

        var gap = second.Task.CreatedAt - first.Task.CreatedAt;
        if (gap.Duration() >= SameAge)
        {
            var older = gap > TimeSpan.Zero ? first : second;
            return new Negotiation(p1, p2, 4, older.Task.Id, $"Age: “{older.Task.Title}” was queued first, and the second waits (rule 6).");
        }

        return new Negotiation(p1, p2, Agreement.MaxTurns, null,
            $"Same seniority, same priority, queued within {SameAge.TotalMinutes:0} minutes of each other: the rules do not settle it.");
    }

    private static string Proposal(AgreementSide side, TimeZoneInfo zone)
    {
        var queued = TimeZoneInfo.ConvertTime(side.Task.CreatedAt, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return $"{side.DeskName} proposes to go first with “{side.Task.Title}”: {Tasks(side.Done)} finished, {Tasks(side.Blocks)} waiting for it, queued {queued}.";
    }

    private static string Tasks(int n) => n == 1 ? "1 task" : $"{n} tasks";

    /// <summary>"src/web/cart.ts and 1 more file".</summary>
    public static string Subject(IReadOnlyList<string> shared)
    {
        ArgumentNullException.ThrowIfNull(shared);
        return shared.Count switch
        {
            0 => "the same files",
            1 => shared[0],
            2 => $"{shared[0]} and 1 more file",
            _ => $"{shared[0]} and {shared.Count - 1} more files",
        };
    }

    /// <summary>A text as a file name: lowercase words joined by dashes, accents dropped.</summary>
    public static string Slug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var plain = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            plain.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = string.Join('-', plain.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "agreement" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }
}

/// <param name="Hold">Why a queued task waits because of an agreement, by task id.</param>
public sealed record AgreementRound(IReadOnlyList<Agreement> Opened, IReadOnlyDictionary<Guid, string> Hold);

/// <param name="FirstName">The first side's desk; the second's below.</param>
public sealed record AgreementView(
    Guid Id, Guid OfficeId, AgreementStatus Status, string Subject,
    Guid FirstDeskId, string FirstDeskName, Guid FirstTaskId, string FirstTaskTitle, string FirstProposal,
    Guid SecondDeskId, string SecondDeskName, Guid SecondTaskId, string SecondTaskTitle, string SecondProposal,
    Guid? GoesFirst, string Reason, int Turns, string MinutesPath, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, string? DecidedBy);

/// <summary>
/// Opens, settles and keeps the agreements: on every round of the queue it looks for clashes,
/// negotiates them by code, writes the minutes into the vault and says which tasks wait for which.
/// An optional one-line summary comes from the model of "Journals and chronicle", never from a
/// free online one; without it the minutes are complete anyway.
/// </summary>
public sealed class AgreementKeeper(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IAgreementRepository agreements,
    IVault vault,
    IUnitOfWork unitOfWork,
    ILocalChat chat,
    IEngineText engines,
    IOptionsMonitor<FunctionModelOptions> models,
    IOptionsMonitor<LocalModelOptions> local,
    TimeProvider clock)
{
    public const string Author = "Openspace";

    public const string WrongStatusCode = "agreement-status";

    /// <summary>One round: new clashes become agreements, finished ones let go, and the holds they put on queued tasks.</summary>
    public async Task<AgreementRound> RoundAsync(CancellationToken cancellationToken)
    {
        var opened = new List<Agreement>();
        var hold = new Dictionary<Guid, string>();
        var now = clock.GetUtcNow();
        var changed = false;
        foreach (var office in await offices.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var known = await agreements.ListAsync(office.Id, cancellationToken).ConfigureAwait(false);
            var clashes = Agreements.Clashes(office, await QueuedAsync(office.Id, cancellationToken).ConfigureAwait(false));
            if (clashes.Count == 0 && known.All(a => a.Status != AgreementStatus.WaitingForPerson && a.Status != AgreementStatus.Settled && a.Status != AgreementStatus.DecidedByPerson))
            {
                continue;
            }

            var all = await tasks.ListAsync(office.Id, null, cancellationToken).ConfigureAwait(false);
            var byId = all.ToDictionary(t => t.Id);
            foreach (var clash in clashes)
            {
                if (known.Concat(opened).Any(a => a.Concerns(clash.First.Id) && a.Concerns(clash.Second.Id)))
                {
                    continue;
                }

                var agreement = await OpenAsync(office, all, clash, now, cancellationToken).ConfigureAwait(false);
                agreements.Add(agreement);
                opened.Add(agreement);
                changed = true;
            }

            foreach (var agreement in known.Concat(opened))
            {
                changed |= Hold(agreement, byId, office, hold, now);
            }
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AgreementRound(opened, hold);
    }

    private async Task<IReadOnlyList<WorkTask>> QueuedAsync(OfficeId office, CancellationToken cancellationToken) =>
        await tasks.ListAsync(office, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);

    /// <summary>The task an agreement puts after the other waits while that one is queued or running.</summary>
    /// <returns>Whether the agreement changed: dropped because a task left the queue.</returns>
    private static bool Hold(Agreement agreement, Dictionary<WorkTaskId, WorkTask> byId, Office office, Dictionary<Guid, string> hold, DateTimeOffset now)
    {
        byId.TryGetValue(agreement.FirstTask, out var first);
        byId.TryGetValue(agreement.SecondTask, out var second);
        string Desk(DeskId id) => office.HasDesk(id) ? office.Locate(id).Desk.Name : "another desk";

        if (agreement.Status == AgreementStatus.WaitingForPerson)
        {
            if (first is not { Status: WorkTaskStatus.Queued } || second is not { Status: WorkTaskStatus.Queued })
            {
                agreement.Drop(now);
                return true;
            }

            hold.TryAdd(first.Id.Value, $"Waits for your answer: does it go before “{second.Title}” ({Desk(agreement.SecondDesk)})?");
            hold.TryAdd(second.Id.Value, $"Waits for your answer: does it go before “{first.Title}” ({Desk(agreement.FirstDesk)})?");
            return false;
        }

        if (agreement.Status is AgreementStatus.Settled or AgreementStatus.DecidedByPerson
            && agreement.GoesFirst is { } firstId && agreement.GoesSecond is { } secondId
            && byId.TryGetValue(firstId, out var goesFirst) && byId.TryGetValue(secondId, out var goesSecond)
            && goesSecond.Status == WorkTaskStatus.Queued && goesFirst.Status is WorkTaskStatus.Queued or WorkTaskStatus.Running)
        {
            var other = goesFirst.DeskId is { } d ? Desk(d) : "another desk";
            hold.TryAdd(goesSecond.Id.Value, $"Waits for “{goesFirst.Title}” to go first, as agreed with {other}.");
        }

        return false;
    }

    private async Task<Agreement> OpenAsync(Office office, IReadOnlyList<WorkTask> all, Clash clash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        AgreementSide Side(WorkTask task)
        {
            var desk = office.Locate(task.DeskId!.Value).Desk;
            var done = all.Count(t => t.DeskId == desk.Id && t.Status == WorkTaskStatus.Done);
            var blocks = all.Count(t => t.DependsOn.Contains(task.Id) && t.Status is not (WorkTaskStatus.Done or WorkTaskStatus.Discarded));
            return new AgreementSide(desk.Id, desk.Name, task, done, blocks);
        }

        var (first, second) = (Side(clash.First), Side(clash.Second));
        var subject = Agreements.Subject(clash.Shared);
        var agreement = Agreement.Open(office.Id, first.Desk, first.Task.Id, second.Desk, second.Task.Id, subject, now);
        var deal = Agreements.Negotiate(first, second, clock.LocalTimeZone);
        var path = await FreePathAsync(
            $"{Agreements.Folder}/{Day(now)}-{Agreements.Slug($"{first.DeskName} and {second.DeskName} {subject}")}", cancellationToken).ConfigureAwait(false);
        agreement.Record(deal.FirstProposal, deal.SecondProposal, deal.Turns, deal.GoesFirst, deal.Reason, path, now);
        var summary = await SummaryAsync(first, second, deal, subject, cancellationToken).ConfigureAwait(false);
        await WriteMinutesAsync(office, agreement, first.Task, second.Task, first.DeskName, second.DeskName, summary, cancellationToken).ConfigureAwait(false);
        return agreement;
    }

    /// <summary>The person's answer: who goes first. Final; the minutes say so.</summary>
    public async Task<AgreementView> DecideAsync(AgreementId id, WorkTaskId goesFirst, string who, CancellationToken cancellationToken)
    {
        var agreement = await agreements.FindAsync(id, cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException("Agreement", id.Value);
        if (agreement.Status != AgreementStatus.WaitingForPerson)
        {
            throw new RejectedException(WrongStatusCode, "This agreement does not wait for you any more.");
        }

        if (!agreement.Concerns(goesFirst))
        {
            throw new RejectedException(WrongStatusCode, "Only one of the agreement's two tasks can go first.");
        }

        agreement.Decide(goesFirst, who, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(agreement.OfficeId, cancellationToken).ConfigureAwait(false);
        var (first, second) = (await tasks.GetAsync(agreement.FirstTask, cancellationToken).ConfigureAwait(false), await tasks.GetAsync(agreement.SecondTask, cancellationToken).ConfigureAwait(false));
        await WriteMinutesAsync(office, agreement, first, second, Name(office, agreement.FirstDesk), Name(office, agreement.SecondDesk), null, cancellationToken).ConfigureAwait(false);
        return View(agreement, office, first.Title, second.Title);
    }

    /// <summary>The office's agreements, the newest first.</summary>
    public async Task<IReadOnlyList<AgreementView>> ListAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var titles = (await tasks.ListAsync(officeId, null, cancellationToken).ConfigureAwait(false)).ToDictionary(t => t.Id, t => t.Title);
        return [.. (await agreements.ListAsync(officeId, cancellationToken).ConfigureAwait(false))
            .Select(a => View(a, office, titles.GetValueOrDefault(a.FirstTask, "A task"), titles.GetValueOrDefault(a.SecondTask, "A task")))];
    }

    private static AgreementView View(Agreement a, Office office, string firstTitle, string secondTitle) => new(
        a.Id.Value, a.OfficeId.Value, a.Status, a.Subject,
        a.FirstDesk.Value, Name(office, a.FirstDesk), a.FirstTask.Value, firstTitle, a.FirstProposal,
        a.SecondDesk.Value, Name(office, a.SecondDesk), a.SecondTask.Value, secondTitle, a.SecondProposal,
        a.GoesFirst?.Value, a.Reason, a.Turns, a.MinutesPath, a.OpenedAt, a.ClosedAt, a.DecidedBy);

    private static string Name(Office office, DeskId desk) => office.HasDesk(desk) ? office.Locate(desk).Desk.Name : "A desk";

    private async Task WriteMinutesAsync(
        Office office, Agreement agreement, WorkTask first, WorkTask second, string firstDesk, string secondDesk, string? summary, CancellationToken cancellationToken)
    {
        var title = $"Agreement: {firstDesk} and {secondDesk} on {agreement.Subject}";
        var previous = await vault.ReadAsync(agreement.MinutesPath, cancellationToken).ConfigureAwait(false);
        summary ??= previous is null ? null : SummaryOf(previous.Body);
        var winner = agreement.GoesFirst == first.Id ? first : agreement.GoesFirst == second.Id ? second : null;
        var body = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"# {title}").AppendLine();
        if (summary is not null)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"> {summary}").AppendLine();
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"Both “{first.Title}” ({firstDesk}) and “{second.Title}” ({secondDesk}) would change {agreement.Subject} at the same time.")
            .AppendLine().AppendLine("## Proposals").AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- {agreement.FirstProposal}")
            .AppendLine(CultureInfo.InvariantCulture, $"- {agreement.SecondProposal}")
            .AppendLine().AppendLine("## Decision").AppendLine()
            .AppendLine(agreement.Reason);
        if (agreement.Status == AgreementStatus.WaitingForPerson)
        {
            body.AppendLine().AppendLine("The question waits for the person under Needs you.");
        }
        else if (agreement.Status == AgreementStatus.DecidedByPerson && winner is not null)
        {
            body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{agreement.DecidedBy} decided: “{winner.Title}” goes first.");
        }
        else if (winner is not null)
        {
            body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"“{winner.Title}” goes first; the other waits for it.");
        }

        body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Turns: {agreement.Turns} of {Agreement.MaxTurns}.");
        var status = agreement.Status == AgreementStatus.WaitingForPerson ? "open" : NoteStatus.Accepted;
        var note = new Note(agreement.MinutesPath, "agreement", status, title, [office.Name], [], [], null, null,
            $"tasks {first.Id.Value} {second.Id.Value}", Author, body.ToString(), []);
        await vault.WriteAsync(note, Author, cancellationToken, $"minutes of {agreement.MinutesPath}").ConfigureAwait(false);
    }

    private static string? SummaryOf(string body) =>
        body.ReplaceLineEndings("\n").Split('\n').FirstOrDefault(l => l.StartsWith("> ", StringComparison.Ordinal))?[2..].Trim();

    /// <summary>One line from the journals model, or null when no model may or can write it: the minutes do without.</summary>
    private async Task<string?> SummaryAsync(AgreementSide first, AgreementSide second, Negotiation deal, string subject, CancellationToken cancellationToken)
    {
        var function = models.CurrentValue.Journals;
        if (!PersonalDataReaders.MayRead(function, models.CurrentValue.FreeModels))
        {
            return null;
        }

        const string System = "You write one warm, light sentence summing up how two office inhabitants agreed on the order of their work. "
            + "Use only the facts given; never invent, never mock, never about the person who runs the office. Plain text, no Markdown.";
        var user = $"{first.DeskName} and {second.DeskName} both planned to change {subject}.\n- {deal.FirstProposal}\n- {deal.SecondProposal}\nOutcome: {deal.Reason}";
        try
        {
            string? text;
            if (function.Engine == ModelEngine.Local)
            {
                var model = function.Model is { Length: > 0 } named ? named.Trim()
                    : local.CurrentValue.Models.FirstOrDefault() is { } any ? $"{local.CurrentValue.Provider}/{any.Id}"
                    : null;
                if (model is null)
                {
                    return null;
                }

                text = await chat.AskAsync(new LocalAsk(model, System, user, MaxTokens: 120, Temperature: 0.5), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                text = await engines.AskAsync(function.Engine, function.Model, System, user, cancellationToken).ConfigureAwait(false);
            }

            var line = text?.ReplaceLineEndings(" ").Trim();
            return string.IsNullOrEmpty(line) ? null : line.Length > 240 ? string.Concat(line.AsSpan(0, 240).TrimEnd(), "…") : line;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<string> FreePathAsync(string stem, CancellationToken cancellationToken)
    {
        var path = $"{stem}.md";
        for (var n = 2; await vault.ReadAsync(path, cancellationToken).ConfigureAwait(false) is not null; n++)
        {
            path = $"{stem}-{n}.md";
        }

        return path;
    }

    private string Day(DateTimeOffset now) =>
        TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
