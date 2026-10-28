using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Once a week, one message: the days ahead from the person's agenda (events, tasks due), the
/// tasks left behind, what the office's routines still wait for a yes on, and how they did in
/// the past week. A local model opens it with a couple of sentences that put it in order; when
/// it cannot, the message goes without them. It reads, never changes anything.
/// </summary>
public sealed class WeeklyRecap(IAgenda agenda, IRoutineRepository routines, ILocalChat chat) : IRoutineKind
{
    public string Key => "weekly";

    public string Name => "Weekly recap";

    public string Description =>
        "Once a week sends you the days ahead from your calendar and tasks, what is left to do, what waits for your yes and how your routines did.";

    public string DefaultSettings => """
        {
          "title": "Week",
          "account": "",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "days": 7
        }
        """;

    /// <summary>Before this hour the recap starts from today, after it from tomorrow: a Sunday evening recap is about the week to come.</summary>
    private const int Noon = 12;

    /// <summary>The past this far back is "last week".</summary>
    private static readonly TimeSpan LastWeek = TimeSpan.FromDays(7);

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = WeeklySettings.Read(context.Settings);
        var words = WeeklyWords.For(settings.Language);
        var zone = context.Clock.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(context.Now, zone);
        var from = DateOnly.FromDateTime(local.DateTime).AddDays(local.Hour < Noon ? 0 : 1);
        var until = from.AddDays(settings.Days);

        IReadOnlyList<AgendaEntry>? entries = null;
        if (settings.Account.Length == 0)
        {
            context.Write("No Google account in the settings: the recap leaves out the agenda.");
        }
        else
        {
            context.Progress("Reading the calendar and the tasks");
            try
            {
                entries = await agenda.ReadAsync(settings.Account, from, until, zone, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"The agenda could not be read ({ex.Message}).");
            }
        }

        context.Progress("Looking at the routines of the office");
        var waiting = await WaitingAsync(context, cancellationToken).ConfigureAwait(false);
        var week = await PastWeekAsync(context, cancellationToken).ConfigureAwait(false);

        // Each day: events by the clock, the whole-day ones first, then the tasks.
        var ahead = entries?.Where(e => e.Date >= from).OrderBy(e => e.Date).ThenByDescending(e => e.IsEvent).ThenBy(e => e.Time).ToList() ?? [];
        var behind = entries?.Where(e => !e.IsEvent && e.Date < from).OrderBy(e => e.Date).ToList() ?? [];
        var intro = ahead.Count + behind.Count > 0 ? await IntroAsync(context, settings, words, ahead, behind, cancellationToken).ConfigureAwait(false) : null;

        var text = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{settings.Title} · {from.ToString("d MMM", words.Culture)} – {until.AddDays(-1).ToString("d MMM", words.Culture)}");
        if (intro is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{intro}");
        }

        if (entries is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Ahead}");
            if (ahead.Count == 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n{words.Free}");
            }

            foreach (var day in ahead.GroupBy(e => e.Date))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n{Capital(day.Key.ToString("dddd d MMMM", words.Culture))}");
                foreach (var entry in day)
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n• {Line(entry, words)}");
                }
            }

            if (behind.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Behind} ({behind.Count})");
                foreach (var task in behind)
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n• {task.Title} — {words.Due} {task.Date.ToString("ddd d MMM", words.Culture)}");
                }
            }
        }

        if (waiting.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Waiting} ({waiting.Sum(w => w.Count)})");
            foreach (var (routine, count) in waiting)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {routine}: {count}");
            }
        }

        if (week.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.LastWeek}");
            foreach (var (routine, runs, failed, last) in week)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {routine}: {words.Runs(runs, failed)}");
                if (last is not null)
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n  {Short(last, 200)}");
                }
            }
        }

        await context.DeliverAsync(text.ToString(), null, cancellationToken).ConfigureAwait(false);
        var events = ahead.Count(e => e.IsEvent);
        return (entries is null ? "No agenda" : $"{events} events and {ahead.Count - events} tasks ahead, {behind.Count} left behind")
            + $"; {waiting.Sum(w => w.Count)} proposals waiting.";
    }

    /// <summary>Proposals of the office still waiting for a yes, by routine, the most first.</summary>
    private async Task<List<(string Routine, int Count)>> WaitingAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        var names = (await routines.ListAsync(context.Routine.OfficeId, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id, r => r.Name);
        var pending = await routines.ListProposalsAsync(context.Routine.OfficeId, ProposalStatus.Pending, cancellationToken).ConfigureAwait(false);
        return [.. pending.GroupBy(p => p.RoutineId)
            .Select(g => (names.GetValueOrDefault(g.Key, "?"), g.Count()))
            .OrderByDescending(w => w.Item2)
            .ThenBy(w => w.Item1, StringComparer.Ordinal)];
    }

    /// <summary>For each other routine of the office that ran in the past week: its runs, the failed ones and what the last good one said.</summary>
    private async Task<List<(string Routine, int Runs, int Failed, string? Last)>> PastWeekAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        var since = context.Now - LastWeek;
        var week = new List<(string, int, int, string?)>();
        foreach (var routine in await routines.ListAsync(context.Routine.OfficeId, cancellationToken).ConfigureAwait(false))
        {
            if (routine.Id == context.Routine.Id)
            {
                continue;
            }

            var runs = (await routines.ListRunsAsync(routine.Id, 50, cancellationToken).ConfigureAwait(false))
                .Where(r => r.StartedAt >= since && r.Status != RoutineRunStatus.Running)
                .ToList();
            if (runs.Count > 0)
            {
                week.Add((routine.Name, runs.Count, runs.Count(r => r.Status == RoutineRunStatus.Failed),
                    runs.Where(r => r.Status == RoutineRunStatus.Succeeded).MaxBy(r => r.StartedAt)?.Summary));
            }
        }

        return week;
    }

    private async Task<string?> IntroAsync(
        RoutineContext context, WeeklySettings settings, WeeklyWords words, List<AgendaEntry> ahead, List<AgendaEntry> behind, CancellationToken cancellationToken)
    {
        var list = string.Join('\n', ahead.Select(e => $"{e.Date.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}: {Line(e, WeeklyWords.For("English"))}")
            .Concat(behind.Select(e => $"Not done yet, due {e.Date.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}: {e.Title}")));
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"You help one person plan their week. Write in {settings.Language}. Use only what the agenda says: never invent appointments, dates or advice.",
                    $"""
                    The reader's agenda for the coming days:
                    {list}

                    In two short sentences, plain and friendly, tell the reader what the week looks like: the busiest days, what is due first, what not to forget.
                    """,
                    IntroSchema, MaxTokens: 300, Temperature: 0.4),
                cancellationToken).ConfigureAwait(false);
            var intro = JsonSerializer.Deserialize<IntroAnswer>(answer, JsonSerializerOptions.Web)?.Intro?.Trim();
            return string.IsNullOrWhiteSpace(intro) ? null : Short(intro, 600);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"No opening lines ({ex.Message}): the recap goes without them.");
            return null;
        }
    }

    /// <summary>"10:30 Dentist (Clinic)", "Whole day: Trip", "Task: Pay the bill".</summary>
    private static string Line(AgendaEntry entry, WeeklyWords words)
    {
        var when = !entry.IsEvent ? $"{words.Task}: " : entry.Time is { } time ? $"{time.ToString("HH:mm", CultureInfo.InvariantCulture)} " : $"{words.WholeDay}: ";
        return $"{when}{entry.Title}{(entry.Place.Length > 0 ? $" ({entry.Place})" : string.Empty)}";
    }

    private static string Capital(string text) => text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private const string IntroSchema = """
        {"type":"object","properties":{"intro":{"type":"string"}},"required":["intro"]}
        """;

    private sealed record IntroAnswer(string? Intro);
}

/// <summary>A weekly recap's settings, read from its JSON.</summary>
/// <param name="Account">The Google account whose agenda it reads; none leaves the agenda out.</param>
/// <param name="Days">How many days ahead it looks.</param>
public sealed record WeeklySettings(string Title, string Account, string Language, string Model, int Days)
{
    public static WeeklySettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        return new WeeklySettings(
            Text("title", "Week"), Text("account", string.Empty), Text("language", "English"), Text("model", "local/gemma"),
            settings.TryGetProperty("days", out var d) && d.TryGetInt32(out var days) ? Math.Clamp(days, 1, 31) : 7);
    }
}

/// <summary>The recap's few fixed words, in the person's language.</summary>
internal sealed record WeeklyWords(
    string Ahead, string Free, string Behind, string Due, string Waiting, string LastWeek, Func<int, int, string> Runs, string Task, string WholeDay, CultureInfo Culture)
{
    public static WeeklyWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    private static readonly WeeklyWords Italian = new(
        "I prossimi giorni",
        "Niente in agenda.",
        "Ancora da fare",
        "scadeva",
        "Aspettano il tuo sì in Openspace",
        "La settimana passata",
        (runs, failed) => (runs == 1 ? "1 giro" : $"{runs} giri") + (failed == 0 ? string.Empty : failed == 1 ? ", 1 non riuscito" : $", {failed} non riusciti"),
        "Da fare",
        "Tutto il giorno",
        CultureInfo.GetCultureInfo("it-IT"));

    private static readonly WeeklyWords English = new(
        "The days ahead",
        "Nothing in the agenda.",
        "Still to do",
        "was due",
        "Waiting for your yes in Openspace",
        "The past week",
        (runs, failed) => (runs == 1 ? "1 run" : $"{runs} runs") + (failed == 0 ? string.Empty : $", {failed} failed"),
        "To do",
        "All day",
        CultureInfo.GetCultureInfo("en-GB"));
}
