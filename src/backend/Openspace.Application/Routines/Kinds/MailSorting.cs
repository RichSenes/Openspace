using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Looks after a mailbox. New mail is sorted by the decider into the labels the person
/// chose; what needs no reading leaves the inbox, labelled, so nothing is lost. For mail
/// that waits for the person's answer a local model drafts a reply, which becomes a draft in
/// the mailbox only after a yes: the person sends it, never the routine. A short summary of
/// what stays in the inbox reaches the person's chat. Dates in new mail (a bill to pay, an
/// appointment) go to the person's agenda: tasks due that day, or calendar events without
/// guests. Mail goes to local models only.
/// </summary>
public sealed class MailSorting(IMailbox mailbox, IDecider decider, ILocalChat chat, IAgenda agenda) : IRoutineKind
{
    public const string LabelAction = "label";

    public const string ReplyAction = "draft-reply";

    public const string AgendaAction = "add-to-agenda";

    public const string KeyPrefix = "mail:";

    /// <summary>What went to the agenda, by date and title, so a reminder mail does not add it twice.</summary>
    public const string AgendaPrefix = "due:";

    public string Key => "mail";

    public string Name => "Mail";

    public string Description =>
        "Sorts new mail into your labels and archives what needs no reading, puts dates in your agenda, drafts replies for your yes (never sends them) and sends you a short summary.";

    public string DefaultSettings => """
        {
          "title": "Mail",
          "account": "",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "query": "in:inbox newer_than:2d",
          "most": 40,
          "backlog": "in:inbox",
          "agenda": { "tasks": true, "calendar": true },
          "labelPrefix": "Openspace",
          "me": "Who you are and how you write, so reply drafts sound like you.",
          "labels": [
            { "name": "To answer", "about": "A person writes to me directly and waits for my answer.", "archive": false, "reply": true },
            { "name": "Newsletters", "about": "Newsletters, blogs and mailing lists I signed up for.", "archive": true },
            { "name": "Receipts", "about": "Orders, invoices, receipts, payments, shipping updates and bookings.", "archive": true },
            { "name": "Notifications", "about": "Automatic notices from apps and services: sign-ins, alerts, reminders, updates.", "archive": true },
            { "name": "Offers", "about": "Advertising, promotions and discounts.", "archive": true }
          ]
        }
        """;

    /// <summary>How sure the decider must be before a message leaves the inbox: below, it stays where it is.</summary>
    public const double Sure = 0.6;

    /// <summary>Messages summed up in one go at most, so the summary stays short.</summary>
    private const int MostInSummary = 15;

    /// <summary>Pages of ids looked through at most per search: a few thousand messages.</summary>
    private const int MostPages = 20;

    /// <summary>Of each message, the start the decider and the writer read.</summary>
    private const int Reading = 1500;

    public bool CanUndo(string action) => action is LabelAction or AgendaAction;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = MailSettings.Read(context.Settings);
        var words = MailWords.For(settings.Language);
        var done = await context.DealtWithAsync(KeyPrefix, cancellationToken).ConfigureAwait(false);
        var newer = await CollectAsync(settings.Account, settings.Query, settings.Most, done, [], countRest: false, cancellationToken).ConfigureAwait(false);
        var older = settings.Backlog is { } search && newer.Ids.Count < settings.Most
            ? await CollectAsync(settings.Account, search, settings.Most - newer.Ids.Count, done, newer.Ids, countRest: true, cancellationToken).ConfigureAwait(false)
            : null;

        var fresh = await MeetAsync(context, settings, newer.Ids, cancellationToken).ConfigureAwait(false);
        var old = older is null ? [] : await MeetAsync(context, settings, older.Ids, cancellationToken).ConfigureAwait(false);
        context.Write(older is null
            ? $"{fresh.Count} new messages."
            : $"{fresh.Count} new messages and {old.Count} older ones; {(older.More ? "more than " : string.Empty)}{older.Rest} older ones still to look at.");
        if (fresh.Count == 0 && old.Count == 0)
        {
            return "No new mail.";
        }

        var isOld = old.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var sorted = await SortAsync(context, settings, [.. fresh, .. old], isOld, cancellationToken).ConfigureAwait(false);

        var away = new Dictionary<string, int>(StringComparer.Ordinal);
        var archived = new HashSet<string>(StringComparer.Ordinal);
        var waiting = 0;
        foreach (var (message, label, _) in sorted.Where(s => s.Label is not null))
        {
            var proposal = await context.ProposeAsync(
                LabelAction,
                Short($"{label!.Name}: {message.Subject}", 200),
                $"From {message.From}. {(label.Archive ? $"It gets the label {settings.PathOf(label)} and leaves the inbox." : $"It gets the label {settings.PathOf(label)}.")}",
                new LabelPayload(message.Id, settings.PathOf(label), label.Archive),
                cancellationToken).ConfigureAwait(false);
            if (proposal.Status == ProposalStatus.Pending)
            {
                waiting++;
            }
            else if (proposal.Status == ProposalStatus.Done && label.Archive)
            {
                archived.Add(message.Id);
                if (!isOld.Contains(message.Id))
                {
                    away[label.Name] = away.GetValueOrDefault(label.Name) + 1;
                }
            }
        }

        // Older mail is sorted, never answered: a reply weeks late is the person's call, and the summary stays about today.
        var answer = sorted.Where(s => s.Label is { Reply: true } && !s.Message.Bulk && !isOld.Contains(s.Message.Id)).Select(s => s.Message).ToList();
        var kept = fresh.Where(m => !archived.Contains(m.Id)).Except(answer).ToList();
        var backlog = older is null ? null : new Backlog(old.Count, old.Count(m => archived.Contains(m.Id)), older.Rest, older.More);

        var dated = new List<AgendaItem>();
        foreach (var message in sorted.Where(s => s.Dated).Select(s => s.Message))
        {
            foreach (var item in await DatesAsync(context, settings, message, cancellationToken).ConfigureAwait(false))
            {
                var key = AgendaPrefix + AgendaKey(item);
                if (!await context.MeetAsync(key, key, item.Title, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var proposal = await context.ProposeAsync(
                    AgendaAction,
                    Short($"{(item.IsEvent ? "Calendar" : "Tasks")}: {item.Title}", 200),
                    $"From {message.From}, {message.Subject}. {(item.IsEvent ? "An event" : "A task")} on {When(item, CultureInfo.InvariantCulture)}{(item.Place.Length > 0 ? $", {item.Place}" : string.Empty)}.",
                    item,
                    cancellationToken).ConfigureAwait(false);
                await context.MarkAsync(key, SeenState.Shown, cancellationToken).ConfigureAwait(false);
                if (proposal.Status == ProposalStatus.Pending)
                {
                    waiting++;
                }
                else if (proposal.Status == ProposalStatus.Done)
                {
                    dated.Add(item);
                }
            }
        }
        var gists = await GistsAsync(context, settings, [.. answer.Concat(kept).Take(MostInSummary)], cancellationToken).ConfigureAwait(false);

        var drafts = 0;
        foreach (var message in answer)
        {
            if (await DraftAsync(context, settings, message, cancellationToken).ConfigureAwait(false) is not { } reply)
            {
                continue;
            }

            await context.ProposeAsync(
                ReplyAction,
                Short($"Reply to {Sender(message.From)}: {message.Subject}", 200),
                Short($"**{message.From}** wrote:\n\n> {Quote(message.Snippet)}\n\nDraft of the reply, saved in the mailbox as a draft after your yes, never sent:\n\n{reply.Body}", 20_000),
                reply,
                cancellationToken).ConfigureAwait(false);
            drafts++;
        }

        foreach (var message in fresh.Concat(old))
        {
            await context.MarkAsync(KeyPrefix + message.Id, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        var summary = Summary(settings, words, fresh.Count, answer, kept, away, gists, dated, drafts, waiting, backlog);
        await context.DeliverAsync(summary, null, cancellationToken).ConfigureAwait(false);
        return $"{fresh.Count} new: {away.Values.Sum()} sorted away, {answer.Count + kept.Count} kept, {drafts} reply drafts proposed{(dated.Count > 0 ? $", {dated.Count} in the agenda" : string.Empty)}"
            + (backlog is not { Looked: > 0 } ? "." : $"; {backlog.Looked} older looked at, {backlog.Archived} archived.");
    }

    public async Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var account = MailSettings.Read(context.Settings).Account;
        switch (proposal.Action)
        {
            case LabelAction:
                var label = JsonSerializer.Deserialize<LabelPayload>(proposal.Payload, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The proposal does not say which message to label.");
                await mailbox.LabelAsync(account, label.MessageId, label.Label, label.Archive, cancellationToken).ConfigureAwait(false);
                return label.Archive ? $"Labelled {label.Label} and archived." : $"Labelled {label.Label}.";
            case AgendaAction:
                var item = JsonSerializer.Deserialize<AgendaItem>(proposal.Payload, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The proposal has nothing for the agenda.");
                var link = await agenda.AddAsync(account, item, context.Clock.LocalTimeZone, cancellationToken).ConfigureAwait(false);
                return $"Added to {(item.IsEvent ? "the calendar" : "the tasks")}: {link}";
            case ReplyAction:
                var reply = JsonSerializer.Deserialize<MailReply>(proposal.Payload, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The proposal has no reply.");
                await mailbox.DraftReplyAsync(account, reply, cancellationToken).ConfigureAwait(false);
                return "Saved as a draft in the mailbox: read it, change it if needed, and send it yourself.";
            default:
                throw new NotSupportedException($"Mail routines do not know how to {proposal.Action}.");
        }
    }

    /// <summary>
    /// The ids matching <paramref name="query"/>, newest first, never dealt with nor already taken: up to
    /// <paramref name="want"/>. With <paramref name="countRest"/> it goes on counting the others, up to a limit.
    /// </summary>
    private async Task<Collected> CollectAsync(
        string account, string query, int want, IReadOnlySet<string> done, IReadOnlyCollection<string> taken, bool countRest, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var rest = 0;
        string? page = null;
        var pages = 0;
        do
        {
            var found = await mailbox.FindAsync(account, query, page, cancellationToken).ConfigureAwait(false);
            foreach (var id in found.Ids.Where(id => !done.Contains(KeyPrefix + id) && !taken.Contains(id) && !ids.Contains(id)))
            {
                if (ids.Count < want)
                {
                    ids.Add(id);
                }
                else
                {
                    rest++;
                }
            }

            page = found.Next;
            pages++;
        }
        while (page is not null && pages < MostPages && (ids.Count < want || countRest));

        return new Collected(ids, rest, page is not null);
    }

    /// <summary>The messages behind these ids that deserve a look, read in full.</summary>
    private async Task<List<MailMessage>> MeetAsync(RoutineContext context, MailSettings settings, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var messages = new List<MailMessage>();
        foreach (var id in ids)
        {
            var message = await mailbox.ReadAsync(settings.Account, id, cancellationToken).ConfigureAwait(false);
            if (await context.MeetAsync(KeyPrefix + message.Id, message.Id, $"{Sender(message.From)}: {message.Subject}", cancellationToken).ConfigureAwait(false))
            {
                messages.Add(message);
            }
        }

        return messages;
    }

    /// <summary>
    /// Each message with the label the decider is sure of, or none: then it stays in the inbox as it
    /// is; and, for new mail when the agenda is on, whether it gives a date to act on.
    /// </summary>
    private async Task<List<Sorted>> SortAsync(
        RoutineContext context, MailSettings settings, List<MailMessage> messages, HashSet<string> isOld, CancellationToken cancellationToken)
    {
        var agendaOn = settings.Agenda.Tasks || settings.Agenda.Calendar;
        if (!decider.Available || (settings.Labels.Count == 0 && !agendaOn))
        {
            context.Write(decider.Available ? "No labels in the settings: the mail stays as it is." : "No decider: the mail stays as it is, and no dates go to the agenda.");
            return [.. messages.Select(m => new Sorted(m, null, false))];
        }

        var options = settings.Labels.Select((l, i) => new DecisionOption($"label{i + 1}", $"{l.Name}: {l.About}"))
            .Append(new DecisionOption("none", "None of these, or not sure."))
            .ToList();
        var sorted = new List<Sorted>();
        var failed = false;
        foreach (var message in messages)
        {
            var questions = new Dictionary<string, DecisionQuestion>();
            if (settings.Labels.Count > 0)
            {
                questions["label"] = new("choice", "Which of these labels fits this email best?", options);
            }

            if (agendaOn && !isOld.Contains(message.Id))
            {
                questions["dated"] = new(
                    "boolean",
                    "Does this email give the reader a date to act on or to attend: a payment or deadline due, an appointment, a booking, a meeting, a trip, an event? Advertising and newsletters do not count.");
            }

            MailSettings.Label? label = null;
            var dated = false;
            if (!failed && questions.Count > 0)
            {
                try
                {
                    var answers = await decider.DecideAsync(Describe(message), questions, cancellationToken).ConfigureAwait(false);
                    if (answers.TryGetValue("label", out var answer) && !answer.Abstained && answer.Confidence >= Sure
                        && answer.Answer is { } id && id.StartsWith("label", StringComparison.Ordinal)
                        && int.TryParse(id.AsSpan(5), out var n) && n >= 1 && n <= settings.Labels.Count)
                    {
                        label = settings.Labels[n - 1];
                    }

                    dated = answers.TryGetValue("dated", out var date) && !date.Abstained && date.Answer == "true" && date.Confidence >= 0.5;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    context.Write($"The decider failed ({ex.Message}): the rest stays as it is.");
                    failed = true;
                }
            }

            sorted.Add(new Sorted(message, label, dated));
        }

        context.Write($"Sorted {sorted.Count(s => s.Label is not null)} of {messages.Count} messages; {sorted.Count(s => s.Dated)} give a date.");
        return sorted;
    }

    /// <summary>
    /// What the local model finds for the agenda in a message: only dates it states, from today on,
    /// within about a year; events become tasks when the calendar is off, and the other way round.
    /// </summary>
    private async Task<List<AgendaItem>> DatesAsync(RoutineContext context, MailSettings settings, MailMessage message, CancellationToken cancellationToken)
    {
        var zone = context.Clock.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.Now, zone).DateTime);
        var arrived = TimeZoneInfo.ConvertTime(message.Received, zone);
        try
        {
            var answer = await chat.AskAsync(
                new LocalAsk(
                    settings.Model, Assistant(settings),
                    $"""
                    Dates for the reader's agenda. Today is {today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}; the email arrived on {arrived.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)}.
                    From: {message.From}
                    Subject: {message.Subject}

                    {Short(message.Text.Length > 0 ? message.Text : message.Snippet, Reading * 2)}

                    List what the reader should put in their agenda: a "task" for something to do by a date (pay, send, renew, sign up), an "event" for something happening at a set time that the reader attends (an appointment, a meeting, a flight, a booking). For each: a short title in {settings.Language} saying what to do or what it is; the date as YYYY-MM-DD; the time as HH:MM on a 24 hour clock, or "" when none is given; for events, how many minutes it lasts (60 when not said); the place, or "". Only dates the email states clearly, never guessed: an empty list when nothing is certain.
                    """,
                    DatesSchema, MaxTokens: 800),
                cancellationToken).ConfigureAwait(false);
            var notes = $"{message.From}: {message.Subject}\nhttps://mail.google.com/mail/?authuser={Uri.EscapeDataString(settings.Account)}#all/{message.ThreadId}";
            var items = new List<AgendaItem>();
            foreach (var found in JsonSerializer.Deserialize<DatesAnswer>(answer, JsonSerializerOptions.Web)?.Items ?? [])
            {
                if (string.IsNullOrWhiteSpace(found.Title)
                    || !DateOnly.TryParseExact(found.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    || date < today || date > today.AddDays(400))
                {
                    continue;
                }

                TimeOnly? time = TimeOnly.TryParseExact(found.Time ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
                var isEvent = string.Equals(found.Kind, "event", StringComparison.OrdinalIgnoreCase);
                isEvent = isEvent ? settings.Agenda.Calendar || !settings.Agenda.Tasks : !settings.Agenda.Tasks;
                var title = Short(found.Title.Trim(), 120);
                if (!isEvent && time is { } at)
                {
                    title = $"{title} ({at.ToString("HH:mm", CultureInfo.InvariantCulture)})";
                }

                items.Add(new AgendaItem(isEvent, title, date, isEvent ? time : null, Math.Clamp(found.Minutes ?? 60, 15, 24 * 60), Short((found.Place ?? string.Empty).Trim(), 200), notes));
            }

            return items;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"No dates read from {message.Subject} ({ex.Message}).");
            return [];
        }
    }

    /// <summary>The same date and the same words, whatever the mail: "2026-11-12:pay-the-electricity-bill".</summary>
    public static string AgendaKey(AgendaItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var words = string.Join('-', item.Title.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        return $"{item.Date:yyyy-MM-dd}:{(words.Length <= 60 ? words : words[..60])}";
    }

    /// <summary>"Thu 12 Nov", with the time when it has one.</summary>
    private static string When(AgendaItem item, CultureInfo culture) =>
        item.Date.ToString("ddd d MMM", culture) + (item.Time is { } time ? $" {time.ToString("HH:mm", CultureInfo.InvariantCulture)}" : string.Empty);

    private static string Describe(MailMessage message) =>
        $"From: {message.From}\nSubject: {message.Subject}\n{(message.Bulk ? "Sent to a mailing list.\n" : string.Empty)}\n{Short(message.Text.Length > 0 ? message.Text : message.Snippet, Reading)}";

    /// <summary>One sentence per message, in the person's language: what it is about and what to do.</summary>
    private async Task<Dictionary<string, string>> GistsAsync(RoutineContext context, MailSettings settings, List<MailMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var list = string.Join("\n\n", messages.Select((m, i) => $"[{i + 1}] From {m.From}, subject {m.Subject}\n{Short(m.Text.Length > 0 ? m.Text : m.Snippet, 600)}"));
        try
        {
            var answer = await chat.AskAsync(
                new LocalAsk(
                    settings.Model, Assistant(settings),
                    $"These emails stay in the inbox:\n{list}\n\nFor each, write one short sentence in {settings.Language}: what it is about and what, if anything, the reader has to do. Give its number in \"n\".",
                    GistsSchema, MaxTokens: 1500),
                cancellationToken).ConfigureAwait(false);
            var gists = JsonSerializer.Deserialize<GistsAnswer>(answer, JsonSerializerOptions.Web)?.Gists ?? [];
            return gists.Where(g => g.N >= 1 && g.N <= messages.Count && !string.IsNullOrWhiteSpace(g.Text))
                .DistinctBy(g => g.N)
                .ToDictionary(g => messages[g.N - 1].Id, g => g.Text.Trim(), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"The summary could not be written ({ex.Message}): subjects only.");
            return [];
        }
    }

    private async Task<MailReply?> DraftAsync(RoutineContext context, MailSettings settings, MailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await chat.AskAsync(
                new LocalAsk(
                    settings.Model, Assistant(settings),
                    $"""
                    An email waiting for the reader's answer.
                    From: {message.From}
                    Subject: {message.Subject}

                    {Short(message.Text.Length > 0 ? message.Text : message.Snippet, Reading * 2)}

                    Write the reader's reply, in the language the email is written in: short, warm, as the reader writes. Answer only what the email asks; where the reader must decide something or give a fact you do not know, leave [brackets] for them to fill. No subject line, no signature.
                    """,
                    ReplySchema, MaxTokens: 1000, Temperature: 0.5),
                cancellationToken).ConfigureAwait(false);
            var body = JsonSerializer.Deserialize<ReplyAnswer>(answer, JsonSerializerOptions.Web)?.Reply?.Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var subject = message.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? message.Subject : $"Re: {message.Subject}";
            return new MailReply(message.ThreadId, message.ReplyTo, subject, message.MessageId, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"No reply draft for {message.Subject} ({ex.Message}).");
            return null;
        }
    }

    private static string Summary(
        MailSettings settings, MailWords words, int count, List<MailMessage> answer, List<MailMessage> kept, Dictionary<string, int> away,
        Dictionary<string, string> gists, List<AgendaItem> dated, int drafts, int waiting, Backlog? backlog)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{settings.Title}: {words.New(count)}");
        void List(string heading, List<MailMessage> messages)
        {
            if (messages.Count == 0)
            {
                return;
            }

            text.Append(CultureInfo.InvariantCulture, $"\n\n{heading} ({messages.Count})");
            foreach (var m in messages.Take(MostInSummary))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {Sender(m.From)} — {m.Subject}");
                if (gists.TryGetValue(m.Id, out var gist))
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n  {gist}");
                }
            }

            if (messages.Count > MostInSummary)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n  {words.More(messages.Count - MostInSummary)}");
            }
        }

        List(words.ToAnswer, answer);
        List(words.Kept, kept);
        if (dated.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Agenda} ({dated.Count})");
            foreach (var item in dated.OrderBy(i => i.Date).ThenBy(i => i.Time))
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {item.Title} — {When(item, words.Culture)} ({(item.IsEvent ? words.Calendar : words.Tasks)})");
            }
        }

        if (away.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.SortedAway}: {string.Join(", ", away.OrderByDescending(a => a.Value).Select(a => $"{a.Key} {a.Value}"))}");
        }

        if (backlog is { Looked: > 0 } || backlog is { Rest: > 0 })
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Older(backlog.Looked, backlog.Archived, backlog.Rest, backlog.More)}");
        }

        if (drafts > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Drafts(drafts)}");
        }

        if (waiting > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{words.Waiting(waiting)}");
        }

        return text.ToString();
    }

    private static string Assistant(MailSettings settings) => $"""
        You help one person with their mail. Write in {settings.Language} unless told otherwise.
        The person: {settings.Me}
        Use only what the emails say: never invent facts, dates, names or promises.
        """;

    /// <summary>"Ada Lovelace" from "Ada Lovelace &lt;ada@example.com&gt;"; the address when there is no name.</summary>
    public static string Sender(string from)
    {
        var at = from.IndexOf('<', StringComparison.Ordinal);
        var name = (at >= 0 ? from[..at] : from).Trim().Trim('"').Trim();
        return name.Length > 0 ? name : from.Trim('<', '>', ' ');
    }

    private static string Quote(string text) => Short(text.ReplaceLineEndings(" "), 400);

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private const string GistsSchema = """
        {"type":"object","properties":{"gists":{"type":"array","items":{"type":"object",
          "properties":{"n":{"type":"integer"},"text":{"type":"string"}},"required":["n","text"]}}},"required":["gists"]}
        """;

    private const string ReplySchema = """
        {"type":"object","properties":{"reply":{"type":"string"}},"required":["reply"]}
        """;

    private sealed record Sorted(MailMessage Message, MailSettings.Label? Label, bool Dated);

    private sealed record DatesAnswer(List<FoundDate>? Items);

    private sealed record FoundDate(string? Kind, string? Title, string? Date, string? Time, int? Minutes, string? Place);

    private static readonly char[] Separators = [.. Enumerable.Range(0, 128).Select(i => (char)i).Where(c => !char.IsLetterOrDigit(c))];

    private const string DatesSchema = """
        {"type":"object","properties":{"items":{"type":"array","items":{"type":"object",
          "properties":{"kind":{"type":"string","enum":["task","event"]},"title":{"type":"string"},"date":{"type":"string"},
            "time":{"type":"string"},"minutes":{"type":"integer"},"place":{"type":"string"}},
          "required":["kind","title","date","time","minutes","place"]}}},"required":["items"]}
        """;

    private sealed record Collected(List<string> Ids, int Rest, bool More);

    /// <param name="Rest">Older messages still to look at, as far as counted; <paramref name="More"/> when counting stopped early.</param>
    private sealed record Backlog(int Looked, int Archived, int Rest, bool More);

    private sealed record LabelPayload(string MessageId, string Label, bool Archive);

    private sealed record GistsAnswer(List<Gist>? Gists);

    private sealed record Gist(int N, string Text);

    private sealed record ReplyAnswer(string? Reply);
}

/// <summary>A mail routine's settings, read from its JSON.</summary>
public sealed record MailSettings(
    string Title, string Account, string Language, string Model, string Query, int Most, string LabelPrefix, string Me, IReadOnlyList<MailSettings.Label> Labels,
    string? Backlog = "in:inbox", MailSettings.AgendaChoice? AgendaSetting = null)
{
    /// <summary>Where dates go: tasks, calendar events, both, or neither.</summary>
    public sealed record AgendaChoice(bool Tasks, bool Calendar);

    public AgendaChoice Agenda => AgendaSetting ?? new AgendaChoice(true, true);

    /// <param name="Archive">Mail with this label leaves the inbox.</param>
    /// <param name="Reply">Mail with this label waits for an answer: a reply draft is proposed.</param>
    public sealed record Label(string Name, string About, bool Archive, bool Reply);

    /// <summary>The label as the mailbox names it, under the routine's own: "Openspace/Receipts".</summary>
    public string PathOf(Label label) => string.IsNullOrWhiteSpace(LabelPrefix) ? label.Name : $"{LabelPrefix}/{label.Name}";

    public static MailSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        var account = Text("account", string.Empty);
        if (account.Length == 0)
        {
            throw new InvalidOperationException("This routine names no account: choose the Google account in Change.");
        }

        var labels = settings.TryGetProperty("labels", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Where(l => l.ValueKind == JsonValueKind.Object && l.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString()))
                .Select(l => new Label(
                    l.GetProperty("name").GetString()!.Trim().Replace('/', '-'),
                    l.TryGetProperty("about", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()!.Trim() : string.Empty,
                    l.TryGetProperty("archive", out var ar) && ar.ValueKind == JsonValueKind.True,
                    l.TryGetProperty("reply", out var r) && r.ValueKind == JsonValueKind.True))
                .ToList()
            : [];
        var most = settings.TryGetProperty("most", out var m) && m.TryGetInt32(out var count) ? Math.Clamp(count, 1, 200) : 40;
        return new MailSettings(
            Text("title", "Mail"), account, Text("language", "English"), Text("model", "local/gemma"), Text("query", "in:inbox newer_than:2d"), most,
            settings.TryGetProperty("labelPrefix", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim().Trim('/') : "Openspace",
            Text("me", "Someone who likes short, kind emails."), labels,
            settings.TryGetProperty("backlog", out var b) && (b.ValueKind == JsonValueKind.False || (b.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(b.GetString())))
                ? null
                : Text("backlog", "in:inbox"),
            settings.TryGetProperty("agenda", out var g)
                ? g.ValueKind == JsonValueKind.Object
                    ? new AgendaChoice(!g.TryGetProperty("tasks", out var t) || t.ValueKind != JsonValueKind.False, !g.TryGetProperty("calendar", out var c) || c.ValueKind != JsonValueKind.False)
                    : new AgendaChoice(g.ValueKind != JsonValueKind.False, g.ValueKind != JsonValueKind.False)
                : null);
    }
}

/// <summary>The summary's few fixed words, in the person's language.</summary>
internal sealed record MailWords(
    Func<int, string> New, string ToAnswer, string Kept, string SortedAway, Func<int, string> Drafts, Func<int, string> Waiting, Func<int, string> More,
    Func<int, int, int, bool, string> Older, string Agenda, string Tasks, string Calendar, CultureInfo Culture)
{
    public static MailWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    private static readonly MailWords Italian = new(
        n => n switch { 0 => "nessuna mail nuova", 1 => "1 mail nuova", _ => $"{n} mail nuove" },
        "Da rispondere",
        "Restano nella posta in arrivo",
        "Smistate e archiviate",
        n => n == 1 ? "1 bozza di risposta aspetta il tuo sì in Openspace: non la invio mai io." : $"{n} bozze di risposta aspettano il tuo sì in Openspace: non le invio mai io.",
        n => n == 1 ? "1 modifica aspetta il tuo sì in Openspace." : $"{n} modifiche aspettano il tuo sì in Openspace.",
        n => $"e altre {n}",
        (looked, archived, rest, more) =>
            (looked > 0 ? $"Posta più vecchia: {looked} guardate, {archived} smistate e archiviate. " : string.Empty)
            + (rest == 0 && !more ? "Non ne restano altre da guardare." : $"Ne restano {(more ? "più di " : "circa ")}{rest} da guardare."),
        "Messe in agenda",
        "Tasks",
        "Calendar",
        CultureInfo.GetCultureInfo("it-IT"));

    private static readonly MailWords English = new(
        n => n switch { 0 => "no new email", 1 => "1 new email", _ => $"{n} new emails" },
        "To answer",
        "Staying in the inbox",
        "Sorted and archived",
        n => n == 1 ? "1 reply draft waits for your yes in Openspace: I never send it." : $"{n} reply drafts wait for your yes in Openspace: I never send them.",
        n => n == 1 ? "1 change waits for your yes in Openspace." : $"{n} changes wait for your yes in Openspace.",
        n => $"and {n} more",
        (looked, archived, rest, more) =>
            (looked > 0 ? $"Older mail: {looked} looked at, {archived} sorted and archived. " : string.Empty)
            + (rest == 0 && !more ? "None left to look at." : $"{(more ? "More than" : "About")} {rest} still to look at."),
        "Added to your agenda",
        "Tasks",
        "Calendar",
        CultureInfo.GetCultureInfo("en-GB"));
}
