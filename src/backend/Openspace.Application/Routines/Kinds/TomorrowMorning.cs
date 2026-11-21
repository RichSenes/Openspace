using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Application.Functions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Work;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// In the evening, the next day in one place: the calendar's events and the tasks due, what waits
/// for the person in Openspace, the work queued in each office and the points Shiori's sessions
/// left open. It writes the agenda into the vault, <c>Agenda/&lt;date&gt;.md</c>, and sends a short
/// message. A few opening lines come from the model of "Digests and summaries", never from a free
/// online one, since the calendar is personal; without a model the plain template says it all.
/// </summary>
public sealed partial class TomorrowMorning(
    IAgenda agenda, NeedsYouQueries needsYou, IWorkTaskRepository tasks, IOfficeRepository offices, IVault vault, ILocalChat chat, IEngineText engines,
    IOptionsMonitor<FunctionModelOptions> models, IOptionsMonitor<LocalModelOptions> local) : IRoutineKind
{
    public const string Folder = "Agenda";

    public string Key => "tomorrow";

    public string Name => "Tomorrow morning";

    public string Description =>
        "In the evening writes tomorrow's agenda into the brain (calendar, tasks due, what waits for you, work queued, open points from your sessions) and sends you a short message.";

    public string DefaultSettings => """
        {
          "title": "",
          "account": "",
          "channel": "",
          "language": "Italian",
          "sessionDays": 7
        }
        """;

    /// <summary>Before this hour the agenda is for today, after it for tomorrow: an early run prepares the day it is in.</summary>
    private const int Noon = 12;

    /// <summary>The most lines of one kind the message shows; the note has them all.</summary>
    private const int MessageLines = 4;

    /// <summary>"Agenda/2026-11-22.md".</summary>
    public static string PathOf(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"{Folder}/{day:yyyy-MM-dd}.md");

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = TomorrowSettings.Read(context.Settings);
        var words = TomorrowWords.For(settings.Language);
        var zone = context.Clock.LocalTimeZone;
        var now = TimeZoneInfo.ConvertTime(context.Now, zone);
        var day = DateOnly.FromDateTime(now.DateTime).AddDays(now.Hour < Noon ? 0 : 1);

        var entries = await CalendarAsync(context, settings, day, zone, cancellationToken).ConfigureAwait(false);
        context.Progress("Looking at what waits for you");
        var waiting = await needsYou.ListAsync(cancellationToken).ConfigureAwait(false);
        var queued = await QueuedAsync(cancellationToken).ConfigureAwait(false);
        context.Progress("Reading the open points of the sessions");
        var points = await OpenPointsAsync(context, settings, cancellationToken).ConfigureAwait(false);

        var plan = new DayPlan(
            day,
            entries is null ? null : [.. entries.Where(e => e.IsEvent && e.Date == day).OrderBy(e => e.Time ?? TimeOnly.MinValue)],
            entries is null ? null : [.. entries.Where(e => !e.IsEvent && e.Date <= day).OrderBy(e => e.Date)],
            waiting,
            queued,
            points);

        var summary = (plan.IsEmpty ? null : await ComposeAsync(context, settings, plan, cancellationToken).ConfigureAwait(false)) ?? words.Summary(plan);
        var title = settings.Title.Length > 0 ? settings.Title : now.Hour < Noon ? words.Today : words.Title;
        var path = PathOf(day);

        var body = Body(plan, words, title, summary);
        try
        {
            var note = Note.Create(path, $"{title} · {words.Day(day)}", body) with { Kind = "agenda", From = $"routine {context.Routine.Name}", Author = DecisionRules.Shiori };
            await vault.WriteAsync(note, DecisionRules.Shiori, cancellationToken, $"wrote the agenda of {day:yyyy-MM-dd}").ConfigureAwait(false);
            context.Write($"Kept in the brain as {path}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The message still goes: a vault that cannot be written does not make the run fail.
            context.Write($"The agenda could not be kept in the brain ({ex.Message}).");
        }

        await context.PostAsync(Message(plan, words, title, summary, path), cancellationToken).ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture,
            $"Agenda of {day:yyyy-MM-dd}: {(plan.Events is null ? "no calendar" : $"{plan.Events.Count} events, {plan.Tasks!.Count} tasks")}, "
            + $"{plan.Waiting.Count} waiting for you, {plan.Queued.Sum(q => q.Titles.Count)} queued, {plan.Points.Count} open points.");
    }

    private async Task<IReadOnlyList<AgendaEntry>?> CalendarAsync(
        RoutineContext context, TomorrowSettings settings, DateOnly day, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (settings.Account.Length == 0)
        {
            context.Write("No Google account in the settings: the agenda leaves out the calendar and the tasks.");
            return null;
        }

        context.Progress("Reading the calendar and the tasks");
        try
        {
            return await agenda.ReadAsync(settings.Account, day, day.AddDays(1), zone, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"The calendar could not be read ({ex.Message}).");
            return null;
        }
    }

    /// <summary>The tasks waiting for a free seat, by office, in the order offices are listed.</summary>
    private async Task<List<QueuedWork>> QueuedAsync(CancellationToken cancellationToken)
    {
        var names = (await offices.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(o => o.Id, o => o.Name);
        var queued = await tasks.ListAsync(null, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);
        return [.. queued.Where(t => names.ContainsKey(t.OfficeId))
            .GroupBy(t => t.OfficeId)
            .Select(g => new QueuedWork(names[g.Key], [.. g.Select(t => t.Title)]))
            .OrderBy(q => q.Office, StringComparer.CurrentCulture)];
    }

    /// <summary>
    /// The points left open by recent sessions: the items listed under a heading that says open or
    /// next ("Open points", "Next", "Punti aperti", "Prossimi passi"), not ticked yet.
    /// </summary>
    private async Task<List<OpenPoint>> OpenPointsAsync(RoutineContext context, TomorrowSettings settings, CancellationToken cancellationToken)
    {
        IReadOnlyList<Note> notes;
        try
        {
            notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            context.Write($"The brain could not be read ({ex.Message}): the agenda leaves out the sessions.");
            return [];
        }

        var since = context.Now - TimeSpan.FromDays(settings.SessionDays);
        var points = new List<OpenPoint>();
        foreach (var note in notes
                     .Where(n => n.Path.StartsWith("Sessions/", StringComparison.Ordinal)
                                 && !n.Path.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase)
                                 && n.Modified >= since)
                     .OrderByDescending(n => n.Modified).ThenBy(n => n.Path, StringComparer.Ordinal))
        {
            var link = $"[[{note.Path[..^Path.GetExtension(note.Path).Length]}]]";
            points.AddRange(OpenItems(note.Body).Select(item => new OpenPoint(item, note.Title, link)));
        }

        return points;
    }

    /// <summary>The unticked list items under the body's "open" or "next" headings.</summary>
    public static IReadOnlyList<string> OpenItems(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var items = new List<string>();
        int? level = null;
        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            if (HeadingPattern().Match(line) is { Success: true } heading)
            {
                var depth = heading.Groups[1].Length;
                if (level is { } open && depth > open)
                {
                    continue;
                }

                level = OpenHeadingPattern().IsMatch(heading.Groups[2].Value) ? depth : null;
                continue;
            }

            if (level is not null && ItemPattern().Match(line) is { Success: true } item && !item.Groups[1].Value.Contains('x', StringComparison.OrdinalIgnoreCase))
            {
                var text = item.Groups[2].Value.Trim();
                if (text.Length > 0)
                {
                    items.Add(text);
                }
            }
        }

        return items;
    }

    /// <summary>
    /// A few opening lines from the model of "Digests and summaries"; null when none may or can
    /// write them, and the run's log says why.
    /// </summary>
    private async Task<string?> ComposeAsync(RoutineContext context, TomorrowSettings settings, DayPlan plan, CancellationToken cancellationToken)
    {
        var function = models.CurrentValue.Digests;
        if (!PersonalDataReaders.MayRead(function, models.CurrentValue.FreeModels))
        {
            context.Write($"The digests model ({(string.IsNullOrWhiteSpace(function.Model) ? "OpenCode's default" : function.Model.Trim())}) may be a free online model, "
                + "and the calendar is personal: the agenda is written from the plain template, without a model.");
            return null;
        }

        var system = $"You help one person get ready for {(plan.Day == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone).DateTime) ? "the day" : "tomorrow")}. "
            + $"Write in {settings.Language}. Use only what the list says: never invent appointments, times, names or advice. Plain text: no Markdown, no lists, no greeting.";
        var user = $"""
            The reader's day, {plan.Day.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}:
            {Facts(plan)}

            In two or three short sentences, plain and friendly, tell the reader what the day looks like: what comes first, what is due, what waits for them.
            """;
        try
        {
            context.Progress("Writing the opening lines");
            string? text;
            if (function.Engine == ModelEngine.Local)
            {
                var model = function.Model is { Length: > 0 } named ? named.Trim()
                    : local.CurrentValue.Models.FirstOrDefault() is { } first ? $"{local.CurrentValue.Provider}/{first.Id}"
                    : throw new InvalidOperationException("no local model in Settings › Local AI");
                var answer = await chat.AskAsync(context, new LocalAsk(model, system, user, IntroSchema, MaxTokens: 300, Temperature: 0.4), cancellationToken).ConfigureAwait(false);
                text = JsonSerializer.Deserialize<IntroAnswer>(answer, JsonSerializerOptions.Web)?.Intro;
            }
            else
            {
                text = await engines.AskAsync(function.Engine, function.Model, system, user, cancellationToken).ConfigureAwait(false);
            }

            text = text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                context.Write("The model wrote nothing: the agenda opens with the plain template.");
                return null;
            }

            return Short(text, 600);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"No model answered ({ex.Message}): the agenda opens with the plain template.");
            return null;
        }
    }

    /// <summary>The day as plain lines for the model, in English whatever the reader's language.</summary>
    private static string Facts(DayPlan plan)
    {
        var lines = new List<string>();
        lines.AddRange(plan.Events?.Select(e => $"Event: {EventLine(e, TomorrowWords.English)}") ?? []);
        lines.AddRange(plan.Tasks?.Select(t => t.Date < plan.Day ? $"Task, late (was due {t.Date.ToString("d MMMM", CultureInfo.InvariantCulture)}): {t.Title}" : $"Task due: {t.Title}") ?? []);
        lines.AddRange(plan.Waiting.Select(w => $"Waiting in Openspace ({TomorrowWords.English.Kind(w.Kind)}): {w.Title}"));
        lines.AddRange(plan.Queued.Select(q => $"Queued in the office {q.Office}: {q.Titles.Count} tasks"));
        lines.AddRange(plan.Points.Select(p => $"Left open in a session: {p.Text}"));
        return string.Join('\n', lines);
    }

    private static string Body(DayPlan plan, TomorrowWords words, string title, string summary)
    {
        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# {title} · {words.Day(plan.Day)}\n\n{summary}\n");
        void Section(string heading, IEnumerable<string> lines, string? none = null)
        {
            var list = lines.ToList();
            if (list.Count == 0 && none is null)
            {
                return;
            }

            text.Append(CultureInfo.InvariantCulture, $"\n## {heading}\n\n");
            foreach (var line in list.Count == 0 ? [none!] : list.Select(l => $"- {l}"))
            {
                text.Append(line).Append('\n');
            }
        }

        if (plan.Events is not null)
        {
            Section(words.Calendar, plan.Events.Select(e => EventLine(e, words)), words.NoEvents);
            Section(words.Tasks, plan.Tasks!.Select(t => TaskLine(t, plan.Day, words)), words.NoTasks);
        }

        Section(words.Waiting, plan.Waiting.Select(w => $"{words.Kind(w.Kind)}: {w.Title}{(w.Detail.Length > 0 ? $" ({w.Detail})" : string.Empty)} · {w.OfficeName}"));
        Section(words.Queued, plan.Queued.Select(q => $"{q.Office} ({q.Titles.Count}): {string.Join(", ", q.Titles)}"));
        Section(words.Sessions, plan.Points.Select(p => $"{p.Text} · {p.Link}"));
        return text.ToString();
    }

    private static OutboxMessage Message(DayPlan plan, TomorrowWords words, string title, string summary, string path)
    {
        var html = new StringBuilder();
        var plain = new StringBuilder();
        void Add(string? bold, string text)
        {
            html.Append(bold is null ? OutboxMessage.Escape(text) : $"<b>{OutboxMessage.Escape(bold)}</b>{OutboxMessage.Escape(text)}");
            plain.Append(bold).Append(text);
        }

        Add($"{title} · {words.Day(plan.Day)}", string.Empty);
        Add(null, $"\n\n{summary}");
        if (plan.Events is { Count: > 0 } events)
        {
            Add(null, "\n");
            foreach (var line in Few(events.Select(e => EventLine(e, words)), words))
            {
                Add(null, $"\n• {line}");
            }
        }

        if (plan.Tasks is { Count: > 0 } due)
        {
            Add(null, "\n\n");
            Add($"{words.Tasks} ({due.Count})", string.Empty);
            foreach (var line in Few(due.Select(t => TaskLine(t, plan.Day, words)), words))
            {
                Add(null, $"\n• {line}");
            }
        }

        var counts = new List<string>();
        if (plan.Waiting.Count > 0)
        {
            counts.Add($"{words.Waiting}: {plan.Waiting.Count}");
        }

        if (plan.Queued.Count > 0)
        {
            counts.Add($"{words.Queued}: {string.Join(", ", plan.Queued.Select(q => $"{q.Office} {q.Titles.Count}"))}");
        }

        if (plan.Points.Count > 0)
        {
            counts.Add($"{words.Sessions}: {plan.Points.Count}");
        }

        if (counts.Count > 0)
        {
            Add(null, "\n");
            foreach (var count in counts)
            {
                Add(null, $"\n• {count}");
            }
        }

        html.Append(CultureInfo.InvariantCulture, $"\n\n<i>{OutboxMessage.Escape(words.InTheBrain(path))}</i>");
        plain.Append(CultureInfo.InvariantCulture, $"\n\n{words.InTheBrain(path)}");
        return new OutboxMessage(html.ToString(), plain.ToString());
    }

    /// <summary>The first lines, and how many more the note has.</summary>
    private static List<string> Few(IEnumerable<string> lines, TomorrowWords words)
    {
        var list = lines.ToList();
        return list.Count <= MessageLines ? list : [.. list.Take(MessageLines - 1), words.More(list.Count - MessageLines + 1)];
    }

    /// <summary>"10:30 Dentist (Clinic)", "All day: Trip".</summary>
    private static string EventLine(AgendaEntry entry, TomorrowWords words) =>
        (entry.Time is { } time ? $"{time.ToString("HH:mm", CultureInfo.InvariantCulture)} " : $"{words.WholeDay}: ")
        + entry.Title + (entry.Place.Length > 0 ? $" ({entry.Place})" : string.Empty);

    private static string TaskLine(AgendaEntry task, DateOnly day, TomorrowWords words) =>
        task.Date < day ? $"{task.Title} — {words.WasDue} {task.Date.ToString("ddd d MMM", words.Culture)}" : task.Title;

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    [GeneratedRegex(@"^(#{1,6})\s+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\b(open|next|apert[aeio]|prossim[aeio])", RegexOptions.IgnoreCase)]
    private static partial Regex OpenHeadingPattern();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+[.)])\s+(?:\[([ xX])\]\s*)?(.*)$")]
    private static partial Regex ItemPattern();

    private const string IntroSchema = """
        {"type":"object","properties":{"intro":{"type":"string"}},"required":["intro"]}
        """;

    private sealed record IntroAnswer(string? Intro);
}

/// <summary>What the agenda gathered for one day.</summary>
/// <param name="Events">The calendar's events of the day; null when the calendar was not read.</param>
/// <param name="Tasks">The tasks due by then and not done, late ones included; null when they were not read.</param>
internal sealed record DayPlan(
    DateOnly Day, IReadOnlyList<AgendaEntry>? Events, IReadOnlyList<AgendaEntry>? Tasks, IReadOnlyList<NeedsYouItem> Waiting,
    IReadOnlyList<QueuedWork> Queued, IReadOnlyList<OpenPoint> Points)
{
    public bool IsEmpty => (Events?.Count ?? 0) + (Tasks?.Count ?? 0) + Waiting.Count + Queued.Count + Points.Count == 0;
}

internal sealed record QueuedWork(string Office, IReadOnlyList<string> Titles);

/// <param name="Link">The session it comes from, as a wikilink.</param>
internal sealed record OpenPoint(string Text, string Session, string Link);

/// <summary>The agenda's settings, read from its JSON.</summary>
/// <param name="Title">What heads the agenda; empty for "Domani" or "Tomorrow", in its language.</param>
/// <param name="Account">The Google account whose calendar and tasks it reads; none leaves them out.</param>
/// <param name="Language">The language the agenda is written in: Italian unless the person picks another.</param>
/// <param name="SessionDays">How far back Shiori's sessions count for their open points.</param>
public sealed record TomorrowSettings(string Title, string Account, string Language, int SessionDays)
{
    public static TomorrowSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        return new TomorrowSettings(
            Text("title", string.Empty), Text("account", string.Empty), Text("language", "Italian"),
            settings.TryGetProperty("sessionDays", out var d) && d.TryGetInt32(out var days) ? Math.Clamp(days, 1, 90) : 7);
    }
}

/// <summary>The agenda's fixed words, in the person's language: Italian or English; any other language gets English words around the model's lines.</summary>
internal sealed record TomorrowWords(
    string Title, string Today, string Calendar, string NoEvents, string Tasks, string NoTasks, string WasDue, string WholeDay, string Waiting, string Queued, string Sessions,
    Func<NeedsYouKind, string> Kind, Func<int, string> More, Func<string, string> InTheBrain, Func<DayPlan, string> Summary, CultureInfo Culture)
{
    public static TomorrowWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" or "IT" ? Italian : English;

    /// <summary>"domenica 22 novembre", "Sunday 22 November".</summary>
    public string Day(DateOnly day) => day.ToString("dddd d MMMM", Culture);

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private static readonly TomorrowWords Italian = new(
        "Domani",
        "Oggi",
        "In calendario",
        "Niente in calendario.",
        "Da fare",
        "Nessuna attività in scadenza.",
        "scadeva",
        "Tutto il giorno",
        "Ti aspettano in Openspace",
        "In coda negli uffici",
        "Rimasti aperti nelle sessioni",
        kind => kind switch
        {
            NeedsYouKind.TaskToApprove => "Da approvare",
            NeedsYouKind.TaskToReview => "Da rivedere",
            NeedsYouKind.TaskFailed => "Non riuscito",
            NeedsYouKind.Proposal => "Proposta",
            NeedsYouKind.RoutineFailed => "Routine non riuscita",
            NeedsYouKind.DecisionToReview => "Decisione da riguardare",
            NeedsYouKind.DecisionToAccept => "Decisione da accettare",
            _ => "Decisione da rivedere",
        },
        n => $"e altre {n}",
        path => $"L'agenda completa è nel cervello: {path}",
        plan =>
        {
            var parts = new List<string>();
            if (plan.Events is { Count: > 0 } events)
            {
                parts.Add(Count(events.Count, "impegno", "impegni"));
            }

            if (plan.Tasks is { Count: > 0 } tasks)
            {
                parts.Add(Count(tasks.Count, "cosa da fare", "cose da fare"));
            }

            if (plan.Waiting.Count > 0)
            {
                parts.Add($"{Count(plan.Waiting.Count, "cosa ti aspetta", "cose ti aspettano")} in Openspace");
            }

            if (plan.Points.Count > 0)
            {
                parts.Add(Count(plan.Points.Count, "punto aperto", "punti aperti") + " dalle sessioni");
            }

            return parts.Count == 0
                ? plan.Queued.Count > 0 ? "Nessun impegno: lavorano solo gli uffici." : "Giornata libera: niente in calendario e niente che ti aspetti."
                : $"In programma: {Join(parts, "e")}.";
        },
        CultureInfo.GetCultureInfo("it-IT"));

    public static readonly TomorrowWords English = new(
        "Tomorrow",
        "Today",
        "In the calendar",
        "Nothing in the calendar.",
        "To do",
        "No tasks due.",
        "was due",
        "All day",
        "Waiting for you in Openspace",
        "Queued in the offices",
        "Left open in the sessions",
        kind => kind switch
        {
            NeedsYouKind.TaskToApprove => "To approve",
            NeedsYouKind.TaskToReview => "To review",
            NeedsYouKind.TaskFailed => "Failed",
            NeedsYouKind.Proposal => "Proposal",
            NeedsYouKind.RoutineFailed => "Routine failed",
            NeedsYouKind.DecisionToReview => "Decision to look at",
            NeedsYouKind.DecisionToAccept => "Decision to accept",
            _ => "Decision due for review",
        },
        n => $"and {n} more",
        path => $"The whole agenda is in the brain: {path}",
        plan =>
        {
            var parts = new List<string>();
            if (plan.Events is { Count: > 0 } events)
            {
                parts.Add(Count(events.Count, "event", "events"));
            }

            if (plan.Tasks is { Count: > 0 } tasks)
            {
                parts.Add(Count(tasks.Count, "task", "tasks") + " to do");
            }

            if (plan.Waiting.Count > 0)
            {
                parts.Add($"{Count(plan.Waiting.Count, "thing", "things")} waiting for you in Openspace");
            }

            if (plan.Points.Count > 0)
            {
                parts.Add(Count(plan.Points.Count, "point", "points") + " left open in the sessions");
            }

            return parts.Count == 0
                ? plan.Queued.Count > 0 ? "Nothing for you: only the offices are working." : "A free day: nothing in the calendar and nothing waiting for you."
                : $"On the list: {Join(parts, "and")}.";
        },
        CultureInfo.GetCultureInfo("en-GB"));

    private static string Join(List<string> parts, string and) =>
        parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts.Take(parts.Count - 1))} {and} {parts[^1]}";
}
