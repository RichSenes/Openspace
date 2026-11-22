using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Brain;
using Openspace.Application.Functions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Offices;

namespace Openspace.Application.Characters;

/// <param name="Office">The office the lines are about.</param>
public sealed record ChronicleSection(string Office, IReadOnlyList<string> Lines);

/// <param name="ByModel">The lines are the model's; otherwise the plain facts.</param>
public sealed record ChronicleDay(DateOnly Day, string Path, IReadOnlyList<ChronicleSection> Sections, bool ByModel)
{
    public bool IsEmpty => Sections.All(s => s.Lines.Count == 0);
}

/// <summary>
/// The office gossip: a few lines a day about what happened between the inhabitants, written
/// by the model of "Journals and chronicle" (local by default) from facts computed by code, or
/// the plain facts when no model may or can write. The lines go to <c>Chronicle/&lt;day&gt;.md</c>
/// in the vault, and the morning paper prints them.
/// </summary>
public sealed class Chronicle(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IAuditLog audit,
    IVault vault,
    ILocalChat chat,
    IEngineText engines,
    IOptionsMonitor<FunctionModelOptions> models,
    IOptionsMonitor<LocalModelOptions> local,
    TimeProvider clock,
    IAgreementRepository? agreements = null)
{
    public const string Folder = "Chronicle";

    public const string Author = "Openspace";

    /// <summary>Lines per office at most: gossip, not minutes.</summary>
    public const int MostLines = 5;

    private const string Schema = """{"type":"object","properties":{"lines":{"type":"array","items":{"type":"string"}}},"required":["lines"]}""";

    /// <summary>"Chronicle/2026-11-21.md".</summary>
    public static string PathOf(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"{Folder}/{day:yyyy-MM-dd}.md");

    /// <summary>The day's lines: those already in the vault, else written now and kept there.</summary>
    public async Task<ChronicleDay> DayAsync(DateOnly day, string language, CancellationToken cancellationToken)
    {
        var note = await vault.ReadAsync(PathOf(day), cancellationToken).ConfigureAwait(false);
        return note is not null ? Read(day, note) : await WriteAsync(day, language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The daily write: yesterday's lines, once, as soon as the hour of <see cref="ChronicleOptions.At"/>
    /// has come. A day already in the vault (written by the morning paper, by hand) is left alone.
    /// </summary>
    /// <returns>The day written now, or null when there was nothing to do.</returns>
    public async Task<ChronicleDay?> KeepYesterdayAsync(ChronicleOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone);
        if (!options.Enabled || TimeOnly.FromDateTime(local.DateTime) < options.At)
        {
            return null;
        }

        var yesterday = DateOnly.FromDateTime(local.DateTime).AddDays(-1);
        if (await vault.ReadAsync(PathOf(yesterday), cancellationToken).ConfigureAwait(false) is not null)
        {
            return null;
        }

        return await WriteAsync(yesterday, options.Language, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The week's office life for the weekly digest: the lines kept in the vault for the
    /// <paramref name="days"/> days up to <paramref name="last"/>, by office, at most
    /// <see cref="MostLines"/> each, oldest first. Reads only: a day never written is left out.
    /// </summary>
    public static async Task<IReadOnlyList<ChronicleSection>> RoundupAsync(IVault vault, DateOnly last, int days, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(vault);
        var byOffice = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        for (var day = last.AddDays(1 - days); day <= last; day = day.AddDays(1))
        {
            if (await vault.ReadAsync(PathOf(day), cancellationToken).ConfigureAwait(false) is not { } note)
            {
                continue;
            }

            foreach (var section in Read(day, note).Sections.Where(s => s.Lines.Count > 0))
            {
                if (!byOffice.TryGetValue(section.Office, out var lines))
                {
                    byOffice[section.Office] = lines = [];
                    order.Add(section.Office);
                }

                // A line a day, so a busy day does not fill the whole week.
                lines.Add(section.Lines[0]);
            }
        }

        return [.. order.Select(o => new ChronicleSection(o, [.. byOffice[o].TakeLast(MostLines)]))];
    }

    /// <summary>Writes the day's lines into the vault, replacing what was there.</summary>
    public async Task<ChronicleDay> WriteAsync(DateOnly day, string language, CancellationToken cancellationToken)
    {
        var zone = clock.LocalTimeZone;
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        var end = start.AddDays(1);
        var sections = new List<ChronicleSection>();
        var byModel = false;
        foreach (var office in await offices.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var all = await tasks.ListAsync(office.Id, null, cancellationToken).ConfigureAwait(false);
            var log = await CharacterQueries.LogSinceAsync(audit, all, end - Relationships.Window, cancellationToken).ConfigureAwait(false);
            var agreed = agreements is null ? [] : await agreements.ListAsync(office.Id, cancellationToken).ConfigureAwait(false);
            var facts = Facts(office, all, [.. log.Where(e => e.At < end)], day, start, end, agreed);
            if (facts.Count == 0)
            {
                continue;
            }

            var written = await ComposeAsync(office.Name, facts, language, cancellationToken).ConfigureAwait(false);
            byModel |= written is not null;
            sections.Add(new ChronicleSection(office.Name, written ?? [.. facts.Take(MostLines)]));
        }

        var chronicle = new ChronicleDay(day, PathOf(day), sections, byModel);
        await vault.WriteAsync(Note.Create(chronicle.Path, Title(day), Body(chronicle)) with { Kind = "chronicle", Author = Author }, Author, cancellationToken, "chronicle of the day")
            .ConfigureAwait(false);
        return chronicle;
    }

    /// <summary>
    /// What happened in the office that day, as plain English sentences: work merged, sent back,
    /// failed or conflicting, what tied two desks, and birthdays. Computed by code only.
    /// </summary>
    public static IReadOnlyList<string> Facts(
        Office office, IReadOnlyList<Domain.Work.WorkTask> all, IReadOnlyList<Domain.Work.AuditEntry> log, DateOnly day, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyList<Domain.Work.Agreement>? agreements = null)
    {
        ArgumentNullException.ThrowIfNull(office);
        var names = office.Rooms.SelectMany(r => r.Desks).ToDictionary(d => d.Id, d => d.Name);
        var facts = new List<string>();
        facts.AddRange(office.Rooms.SelectMany(r => r.Desks).Where(d => d.Birthday.IsOn(day)).Select(d => $"It is {d.Name}'s birthday."));
        foreach (var e in LifeEvents.From(all, log).Where(e => e.At >= start && e.At < end && names.ContainsKey(e.Desk)))
        {
            facts.Add($"{names[e.Desk]}: {Mood.ReasonOf(e)}.");
        }

        foreach (var tie in Relationships.Of(all, log, names, end, agreements))
        {
            facts.AddRange(tie.History.Where(h => h.At >= start && h.At < end).Select(h => $"{h.Text}."));
        }

        return facts;
    }

    /// <summary>The model's lines, or null when no model may or can write them; the facts are used then.</summary>
    private async Task<IReadOnlyList<string>?> ComposeAsync(string office, IReadOnlyList<string> facts, string language, CancellationToken cancellationToken)
    {
        var function = models.CurrentValue.Journals;
        if (!PersonalDataReaders.MayRead(function, models.CurrentValue.FreeModels))
        {
            return null;
        }

        var system = $"You write the gossip column of a small office of AI inhabitants, in {language}. "
            + "Use only the facts given: never invent events, names or numbers. Warm and light, never mocking, never about the person who runs the office. "
            + $"At most {MostLines} short lines, one sentence each. Plain text: no Markdown.";
        var user = $"The office \"{office}\" today:\n{string.Join('\n', facts.Select(f => $"- {f}"))}";
        try
        {
            string? text;
            if (function.Engine == ModelEngine.Local)
            {
                var model = function.Model is { Length: > 0 } named ? named.Trim()
                    : local.CurrentValue.Models.FirstOrDefault() is { } first ? $"{local.CurrentValue.Provider}/{first.Id}"
                    : null;
                if (model is null)
                {
                    return null;
                }

                var answer = await chat.AskAsync(new LocalAsk(model, system, user + "\nAnswer as JSON: {\"lines\": [..]}.", Schema, MaxTokens: 400, Temperature: 0.6), cancellationToken)
                    .ConfigureAwait(false);
                return Clean(JsonSerializer.Deserialize<LinesAnswer>(answer, JsonSerializerOptions.Web)?.Lines);
            }

            text = await engines.AskAsync(function.Engine, function.Model, system, user + "\nOne line per sentence.", cancellationToken).ConfigureAwait(false);
            return Clean(text?.Split('\n'));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The model's lines without bullets or blanks, cut to size; null when nothing is left.</summary>
    public static IReadOnlyList<string>? Clean(IEnumerable<string?>? lines)
    {
        var kept = (lines ?? [])
            .Select(l => l?.Trim().TrimStart('-', '*', '•', ' ').Trim() ?? string.Empty)
            .Where(l => l.Length > 0)
            .Select(l => l.Length > 240 ? string.Concat(l.AsSpan(0, 240).TrimEnd(), "…") : l)
            .Take(MostLines)
            .ToList();
        return kept.Count == 0 ? null : kept;
    }

    private static string Title(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"Chronicle of {day:yyyy-MM-dd}");

    private static string Body(ChronicleDay chronicle)
    {
        var body = new StringBuilder().AppendLine(CultureInfo.InvariantCulture, $"# {Title(chronicle.Day)}");
        if (chronicle.IsEmpty)
        {
            body.AppendLine().AppendLine("A quiet day.");
        }

        foreach (var section in chronicle.Sections)
        {
            body.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## {section.Office}").AppendLine();
            section.Lines.ToList().ForEach(l => body.AppendLine(CultureInfo.InvariantCulture, $"- {l.ReplaceLineEndings(" ")}"));
        }

        return body.ToString();
    }

    /// <summary>A day read back from its note: a section per "## office", a line per "- ".</summary>
    public static ChronicleDay Read(DateOnly day, Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var sections = new List<ChronicleSection>();
        string? office = null;
        var lines = new List<string>();
        foreach (var line in note.Body.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (office is not null)
                {
                    sections.Add(new ChronicleSection(office, lines));
                }

                (office, lines) = (line[3..].Trim(), []);
            }
            else if (office is not null && line.StartsWith("- ", StringComparison.Ordinal))
            {
                lines.Add(line[2..].Trim());
            }
        }

        if (office is not null)
        {
            sections.Add(new ChronicleSection(office, lines));
        }

        return new ChronicleDay(day, note.Path, sections, ByModel: false);
    }

    private sealed record LinesAnswer(IReadOnlyList<string>? Lines);
}

/// <summary>When the chronicle of the day before is written on its own, and in which language.</summary>
public sealed class ChronicleOptions
{
    public const string SectionName = "Chronicle";

    /// <summary>Off, the chronicle is written only when the paper or the page asks for it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The local hour from which yesterday's lines are written: the day is over, the night is quiet.</summary>
    public TimeOnly At { get; set; } = new(1, 0);

    public string Language { get; set; } = "English";
}
