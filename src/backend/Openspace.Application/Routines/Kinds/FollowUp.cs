using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.LocalModels;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Keeps conversations from going quiet. Mail the person sent that got no answer after some
/// days gets a gentle reminder drafted by a local model, which becomes a draft in the mailbox
/// only after a yes: the person sends it, never the routine. Mail from people that still waits
/// for the person's answer, as the decider judges it, is named in a short summary in the chat.
/// Each conversation comes up once for each last message: a new message starts it over.
/// </summary>
public sealed class FollowUp(IMailbox mailbox, IDecider decider, ILocalChat chat) : IRoutineKind
{
    public const string FollowUpAction = "draft-follow-up";

    /// <summary>Conversations met, by thread: the fingerprint is their last message, so a new one makes them new again.</summary>
    public const string KeyPrefix = "follow:";

    public string Key => "followup";

    public string Name => "Follow-up";

    public string Description =>
        "Finds the mail you sent that got no answer and drafts a reminder for your yes (never sends it), and tells you which mail still waits for your answer.";

    public string DefaultSettings => """
        {
          "title": "Follow-up",
          "account": "",
          "channel": "",
          "language": "English",
          "model": "local/gemma",
          "wait": 5,
          "window": 30,
          "most": 20,
          "me": "Who you are and how you write, so reminders sound like you."
        }
        """;

    /// <summary>How sure the decider must be that a message waits for an answer before the summary names it.</summary>
    public const double Sure = 0.6;

    /// <summary>Of each message, the start the decider and the writer read.</summary>
    private const int Reading = 1500;

    /// <summary>Pages of ids looked through at most per search.</summary>
    private const int MostPages = 10;

    private static readonly string[] Robots = ["noreply", "no-reply", "donotreply", "do-not-reply", "mailer-daemon", "notifications@"];

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = FollowUpSettings.Read(context.Settings);
        var words = FollowUpWords.For(settings.Language);
        var range = $"newer_than:{settings.Window}d older_than:{settings.Wait}d";

        var unanswered = await QuietAsync(context, settings, $"in:sent {range}", mine: true, settings.Most, cancellationToken).ConfigureAwait(false);
        var received = await QuietAsync(context, settings, $"in:inbox {range}", mine: false, settings.Most - unanswered.Count, cancellationToken).ConfigureAwait(false);
        context.Write($"{unanswered.Count} sent conversations without an answer, {received.Count} received ones to judge.");

        var yours = await WaitingAsync(context, received, cancellationToken).ConfigureAwait(false);

        var drafts = 0;
        foreach (var (message, i) in unanswered.Select((m, i) => (m, i)))
        {
            context.Progress($"Drafting reminder {i + 1} of {unanswered.Count}");
            if (await DraftAsync(context, settings, message, cancellationToken).ConfigureAwait(false) is { } reminder)
            {
                await context.ProposeAsync(
                    FollowUpAction,
                    Short($"Remind {MailSorting.Sender(message.To)}: {message.Subject}", 200),
                    Short($"You wrote to **{message.To}** {Ago(context, message, CultureInfo.InvariantCulture)} and got no answer:\n\n> {Quote(message.Snippet)}\n\nDraft of the reminder, saved in the mailbox as a draft after your yes, never sent:\n\n{reminder.Body}", 20_000),
                    reminder,
                    cancellationToken).ConfigureAwait(false);
                drafts++;
            }
        }

        foreach (var message in unanswered.Concat(received))
        {
            await context.MarkAsync(KeyPrefix + message.ThreadId, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        }

        if (unanswered.Count == 0 && yours.Count == 0)
        {
            return "Nothing to follow up.";
        }

        await context.DeliverAsync(Summary(context, settings, words, unanswered, yours, drafts), null, cancellationToken).ConfigureAwait(false);
        return $"{unanswered.Count} without an answer, {drafts} reminders proposed; {yours.Count} waiting for an answer.";
    }

    public async Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        if (proposal.Action != FollowUpAction)
        {
            throw new NotSupportedException($"Follow-up routines do not know how to {proposal.Action}.");
        }

        var reminder = JsonSerializer.Deserialize<MailReply>(proposal.Payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The proposal has no reminder.");
        await mailbox.DraftReplyAsync(FollowUpSettings.Read(context.Settings).Account, reminder, cancellationToken).ConfigureAwait(false);
        return "Saved as a draft in the mailbox: read it, change it if needed, and send it yourself.";
    }

    /// <summary>
    /// The last message of each conversation found by <paramref name="query"/> that went quiet on
    /// that message: sent by the person when <paramref name="mine"/>, by someone else otherwise.
    /// Only conversations not met yet with that same last message, up to <paramref name="want"/>.
    /// </summary>
    private async Task<List<MailMessage>> QuietAsync(
        RoutineContext context, FollowUpSettings settings, string query, bool mine, int want, CancellationToken cancellationToken)
    {
        var found = new List<MailMessage>();
        if (want <= 0)
        {
            return found;
        }

        var threads = new HashSet<string>(StringComparer.Ordinal);
        var quietSince = context.Now.AddDays(-settings.Wait);
        string? page = null;
        var pages = 0;
        do
        {
            var ids = await mailbox.FindAsync(settings.Account, query, page, cancellationToken).ConfigureAwait(false);
            foreach (var id in ids.Ids)
            {
                if (found.Count >= want)
                {
                    break;
                }

                var message = await mailbox.ReadAsync(settings.Account, id, cancellationToken).ConfigureAwait(false);
                if (!threads.Add(message.ThreadId) || (!mine && message.Bulk))
                {
                    continue;
                }

                context.Progress($"Looking at {(mine ? "sent" : "received")} conversation {threads.Count}");
                var thread = await mailbox.ReadThreadAsync(settings.Account, message.ThreadId, cancellationToken).ConfigureAwait(false);
                if (thread.Count == 0 || thread[^1] is not { } last || last.Sent != mine || last.Received > quietSince
                    || (mine ? IsRobot(last.To) || last.To.Length == 0 : last.Bulk || IsRobot(last.From)))
                {
                    continue;
                }

                var who = mine ? last.To : last.From;
                if (await context.MeetAsync(KeyPrefix + last.ThreadId, last.Id, Short($"{MailSorting.Sender(who)}: {last.Subject}", 200), cancellationToken).ConfigureAwait(false))
                {
                    found.Add(last);
                }
            }

            page = ids.Next;
            pages++;
        }
        while (page is not null && found.Count < want && pages < MostPages);

        return found;
    }

    /// <summary>Of the messages from people, those the decider is sure wait for the person's answer.</summary>
    private async Task<List<MailMessage>> WaitingAsync(RoutineContext context, List<MailMessage> messages, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        if (!decider.Available)
        {
            context.Write("No decider: mail waiting for your answer is not looked for.");
            return [];
        }

        var waiting = new List<MailMessage>();
        var question = new Dictionary<string, DecisionQuestion>
        {
            ["answer"] = new(
                "boolean",
                "Does this email wait for an answer from the reader: a question to them, a request, an invitation, something to confirm? Notices, receipts, thanks and messages that close a conversation do not."),
        };
        foreach (var (message, i) in messages.Select((m, i) => (m, i)))
        {
            context.Progress($"Judging {i + 1} of {messages.Count} messages");
            try
            {
                var answers = await decider.DecideAsync(
                    $"From: {message.From}\nSubject: {message.Subject}\n\n{Short(message.Text.Length > 0 ? message.Text : message.Snippet, Reading)}",
                    question, cancellationToken).ConfigureAwait(false);
                if (answers.TryGetValue("answer", out var answer) && !answer.Abstained && answer.Answer == "true" && answer.Confidence >= Sure)
                {
                    waiting.Add(message);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"The decider failed ({ex.Message}): the rest is left out.");
                break;
            }
        }

        return waiting;
    }

    private async Task<MailReply?> DraftAsync(RoutineContext context, FollowUpSettings settings, MailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var answer = await chat.AskAsync(context,
                new LocalAsk(
                    settings.Model,
                    $"""
                    You help one person with their mail.
                    The person: {settings.Me}
                    Use only what the emails say: never invent facts, dates, names or promises.
                    """,
                    $"""
                    The reader sent this email {Ago(context, message, CultureInfo.InvariantCulture)} and got no answer.
                    To: {message.To}
                    Subject: {message.Subject}

                    {Short(message.Text.Length > 0 ? message.Text : message.Snippet, Reading * 2)}

                    Write a short, friendly reminder from the reader, in the language the email is written in: two or three sentences that recall what the email asked, without blame. No subject line, no signature.
                    """,
                    ReminderSchema, MaxTokens: 600, Temperature: 0.5),
                cancellationToken).ConfigureAwait(false);
            var body = JsonSerializer.Deserialize<ReminderAnswer>(answer, JsonSerializerOptions.Web)?.Reminder?.Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var subject = message.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? message.Subject : $"Re: {message.Subject}";
            return new MailReply(message.ThreadId, message.To, subject, message.MessageId, body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"No reminder for {message.Subject} ({ex.Message}).");
            return null;
        }
    }

    private static string Summary(
        RoutineContext context, FollowUpSettings settings, FollowUpWords words, List<MailMessage> unanswered, List<MailMessage> yours, int drafts)
    {
        var text = new StringBuilder().Append(settings.Title);
        if (unanswered.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.NoAnswer} ({unanswered.Count})");
            foreach (var m in unanswered)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {MailSorting.Sender(m.To)} — {m.Subject} ({words.Sent(Days(context, m))})");
            }
        }

        if (yours.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Yours} ({yours.Count})");
            foreach (var m in yours)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {MailSorting.Sender(m.From)} — {m.Subject} ({words.Received(Days(context, m))})");
            }
        }

        if (drafts > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Drafts(drafts)}");
        }

        return text.ToString();
    }

    private static int Days(RoutineContext context, MailMessage message) => Math.Max(0, (int)(context.Now - message.Received).TotalDays);

    private static string Ago(RoutineContext context, MailMessage message, CultureInfo culture) =>
        string.Create(culture, $"{Days(context, message)} days ago");

    /// <summary>Addresses no person reads: reminding them, or waiting on them, makes no sense.</summary>
    private static bool IsRobot(string address) => Robots.Any(r => address.Contains(r, StringComparison.OrdinalIgnoreCase));

    private static string Quote(string text) => Short(text.ReplaceLineEndings(" "), 400);

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");

    private const string ReminderSchema = """
        {"type":"object","properties":{"reminder":{"type":"string"}},"required":["reminder"]}
        """;

    private sealed record ReminderAnswer(string? Reminder);
}

/// <summary>A follow-up routine's settings, read from its JSON.</summary>
/// <param name="Wait">Days of quiet before a conversation counts as waiting.</param>
/// <param name="Window">Days back the routine looks: older conversations are left alone.</param>
/// <param name="Most">Conversations brought up per run at most.</param>
public sealed record FollowUpSettings(string Title, string Account, string Language, string Model, int Wait, int Window, int Most, string Me)
{
    public static FollowUpSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;
        int Number(string name, int fallback, int least, int most) =>
            settings.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? Math.Clamp(n, least, most) : fallback;

        var account = Text("account", string.Empty);
        if (account.Length == 0)
        {
            throw new InvalidOperationException("This routine names no account: choose the Google account in Change.");
        }

        var wait = Number("wait", 5, 1, 60);
        return new FollowUpSettings(
            Text("title", "Follow-up"), account, Text("language", "English"), Text("model", "local/gemma"),
            wait, Math.Max(Number("window", 30, 2, 365), wait + 1), Number("most", 20, 1, 100), Text("me", "Someone who likes short, kind emails."));
    }
}

/// <summary>The summary's few fixed words, in the person's language.</summary>
internal sealed record FollowUpWords(string NoAnswer, string Yours, Func<int, string> Sent, Func<int, string> Received, Func<int, string> Drafts)
{
    public static FollowUpWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    private static readonly FollowUpWords Italian = new(
        "Nessuna risposta",
        "Aspettano una tua risposta",
        n => n == 1 ? "inviata 1 giorno fa" : $"inviata {n} giorni fa",
        n => n == 1 ? "da 1 giorno" : $"da {n} giorni",
        n => n == 1 ? "1 bozza di sollecito aspetta il tuo sì in Openspace: non la invio mai io." : $"{n} bozze di sollecito aspettano il tuo sì in Openspace: non le invio mai io.");

    private static readonly FollowUpWords English = new(
        "No answer yet",
        "Waiting for your answer",
        n => n == 1 ? "sent 1 day ago" : $"sent {n} days ago",
        n => n == 1 ? "for 1 day" : $"for {n} days",
        n => n == 1 ? "1 reminder draft waits for your yes in Openspace: I never send it." : $"{n} reminder drafts wait for your yes in Openspace: I never send them.");
}
