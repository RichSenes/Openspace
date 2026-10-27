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
/// what stays in the inbox reaches the person's chat. Mail goes to local models only.
/// </summary>
public sealed class MailSorting(IMailbox mailbox, IDecider decider, ILocalChat chat) : IRoutineKind
{
    public const string LabelAction = "label";

    public const string ReplyAction = "draft-reply";

    public const string KeyPrefix = "mail:";

    public string Key => "mail";

    public string Name => "Mail";

    public string Description =>
        "Sorts new mail into your labels and archives what needs no reading, drafts replies for your yes (never sends them) and sends you a short summary.";

    public string DefaultSettings => """
        {
          "title": "Mail",
          "account": "",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "query": "in:inbox newer_than:2d",
          "most": 40,
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

    /// <summary>Of each message, the start the decider and the writer read.</summary>
    private const int Reading = 1500;

    public bool CanUndo(string action) => action == LabelAction;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = MailSettings.Read(context.Settings);
        var words = MailWords.For(settings.Language);
        var messages = await mailbox.ListAsync(settings.Account, settings.Query, settings.Most, cancellationToken).ConfigureAwait(false);

        var fresh = new List<MailMessage>();
        foreach (var message in messages)
        {
            if (await context.MeetAsync(KeyPrefix + message.Id, message.Id, $"{Sender(message.From)}: {message.Subject}", cancellationToken).ConfigureAwait(false))
            {
                fresh.Add(message);
            }
        }

        context.Write($"{messages.Count} messages found, {fresh.Count} new.");
        if (fresh.Count == 0)
        {
            return "No new mail.";
        }

        var sorted = await SortAsync(context, settings, fresh, cancellationToken).ConfigureAwait(false);

        var away = new Dictionary<string, int>(StringComparer.Ordinal);
        var archived = new HashSet<string>(StringComparer.Ordinal);
        var waiting = 0;
        foreach (var (message, label) in sorted.Where(s => s.Label is not null))
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
                away[label.Name] = away.GetValueOrDefault(label.Name) + 1;
                archived.Add(message.Id);
            }
        }

        var answer = sorted.Where(s => s.Label is { Reply: true } && !s.Message.Bulk).Select(s => s.Message).ToList();
        var kept = sorted.Select(s => s.Message).Where(m => !archived.Contains(m.Id)).Except(answer).ToList();
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

        foreach (var message in fresh)
        {
            await context.MarkAsync(KeyPrefix + message.Id, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        var summary = Summary(settings, words, fresh.Count, answer, kept, away, gists, drafts, waiting);
        await context.DeliverAsync(summary, null, cancellationToken).ConfigureAwait(false);
        return $"{fresh.Count} new: {away.Values.Sum()} sorted away, {answer.Count + kept.Count} kept, {drafts} reply drafts proposed.";
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
            case ReplyAction:
                var reply = JsonSerializer.Deserialize<MailReply>(proposal.Payload, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The proposal has no reply.");
                await mailbox.DraftReplyAsync(account, reply, cancellationToken).ConfigureAwait(false);
                return "Saved as a draft in the mailbox: read it, change it if needed, and send it yourself.";
            default:
                throw new NotSupportedException($"Mail routines do not know how to {proposal.Action}.");
        }
    }

    /// <summary>Each message with the label the decider is sure of, or none: then it stays in the inbox as it is.</summary>
    private async Task<List<(MailMessage Message, MailSettings.Label? Label)>> SortAsync(
        RoutineContext context, MailSettings settings, List<MailMessage> fresh, CancellationToken cancellationToken)
    {
        if (!decider.Available || settings.Labels.Count == 0)
        {
            context.Write(settings.Labels.Count == 0 ? "No labels in the settings: the mail stays as it is." : "No decider: the mail stays as it is.");
            return [.. fresh.Select(m => (m, (MailSettings.Label?)null))];
        }

        var options = settings.Labels.Select((l, i) => new DecisionOption($"label{i + 1}", $"{l.Name}: {l.About}"))
            .Append(new DecisionOption("none", "None of these, or not sure."))
            .ToList();
        var questions = new Dictionary<string, DecisionQuestion>
        {
            ["label"] = new("choice", "Which of these labels fits this email best?", options),
        };

        var sorted = new List<(MailMessage, MailSettings.Label?)>();
        var failed = false;
        foreach (var message in fresh)
        {
            MailSettings.Label? label = null;
            if (!failed)
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
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    context.Write($"The decider failed ({ex.Message}): the rest stays as it is.");
                    failed = true;
                }
            }

            sorted.Add((message, label));
        }

        context.Write($"Sorted {sorted.Count(s => s.Item2 is not null)} of {fresh.Count} messages.");
        return sorted;
    }

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
        Dictionary<string, string> gists, int drafts, int waiting)
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
        if (away.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.SortedAway}: {string.Join(", ", away.OrderByDescending(a => a.Value).Select(a => $"{a.Key} {a.Value}"))}");
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

    private sealed record LabelPayload(string MessageId, string Label, bool Archive);

    private sealed record GistsAnswer(List<Gist>? Gists);

    private sealed record Gist(int N, string Text);

    private sealed record ReplyAnswer(string? Reply);
}

/// <summary>A mail routine's settings, read from its JSON.</summary>
public sealed record MailSettings(
    string Title, string Account, string Language, string Model, string Query, int Most, string LabelPrefix, string Me, IReadOnlyList<MailSettings.Label> Labels)
{
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
            Text("me", "Someone who likes short, kind emails."), labels);
    }
}

/// <summary>The summary's few fixed words, in the person's language.</summary>
internal sealed record MailWords(
    Func<int, string> New, string ToAnswer, string Kept, string SortedAway, Func<int, string> Drafts, Func<int, string> Waiting, Func<int, string> More)
{
    public static MailWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    private static readonly MailWords Italian = new(
        n => n == 1 ? "1 mail nuova" : $"{n} mail nuove",
        "Da rispondere",
        "Restano nella posta in arrivo",
        "Smistate e archiviate",
        n => n == 1 ? "1 bozza di risposta aspetta il tuo sì in Openspace: non la invio mai io." : $"{n} bozze di risposta aspettano il tuo sì in Openspace: non le invio mai io.",
        n => n == 1 ? "1 etichetta aspetta il tuo sì in Openspace." : $"{n} etichette aspettano il tuo sì in Openspace.",
        n => $"e altre {n}");

    private static readonly MailWords English = new(
        n => n == 1 ? "1 new email" : $"{n} new emails",
        "To answer",
        "Staying in the inbox",
        "Sorted and archived",
        n => n == 1 ? "1 reply draft waits for your yes in Openspace: I never send it." : $"{n} reply drafts wait for your yes in Openspace: I never send them.",
        n => n == 1 ? "1 label waits for your yes in Openspace." : $"{n} labels wait for your yes in Openspace.",
        n => $"and {n} more");
}
