using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The follow-up routine on a made-up mailbox: no real account, no real model.</summary>
public sealed class FollowUpTests
{
    private const string Me = "Sam Example <sam@example.com>";

    private readonly Harness _h = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly FakeDecider _decider = new();
    private readonly FakeChat _chat = new();

    private FollowUp Kind() => new(_mailbox, _decider, _chat);

    private Routine Create(string account = "sam@example.com", string language = "English")
    {
        var settings = $$"""
            {"title":"Follow-up","account":"{{account}}","language":"{{language}}","channel":"POSTMAN","wait":5,"window":30,"me":"A kind person."}
            """;
        var routine = Routine.Create(_h.Office, "Follow-up", "followup", RoutineSchedule.Daily(new TimeOnly(9, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [Kind()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.OrderBy(r => r.StartedAt).Last();
    }

    private ProposalCommands Proposals() => new(_h.Routines, [Kind()], _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<ProposalCommands>.Instance);

    /// <summary>A message in thread <paramref name="thread"/>, <paramref name="daysAgo"/> days before the start.</summary>
    private void Add(string id, string thread, string from, string to, string subject, string text, int daysAgo, bool bulk = false)
    {
        var sent = from == Me;
        _mailbox.Messages.Add(new MailMessage(
            id, thread, from, from, subject, $"<{id}@mail.example>", Harness.Start.AddDays(-daysAgo), text, text, Unread: false, bulk, to, sent));
    }

    /// <summary>
    /// A quote that got no answer, a question that waits for Sam, thanks that close a talk, a recent
    /// mail, a mail to a robot and a newsletter: only the first two are worth a word.
    /// </summary>
    private void Mailbox()
    {
        Add("q1", "quote", Me, "Bea Builder <bea@builders.example>", "Quote for the kitchen", "Could you send me the quote for the kitchen by Friday?", 7);
        Add("d1", "dinner", Me, "Cy Friend <cy@example.org>", "Dinner?", "Dinner on Saturday?", 9);
        Add("d2", "dinner", "Cy Friend <cy@example.org>", Me, "Re: Dinner?", "Sure! Which restaurant do you want to try?", 8);
        Add("t1", "thanks", Me, "Di Neighbour <di@example.org>", "Your keys", "I left your keys at the desk.", 10);
        Add("t2", "thanks", "Di Neighbour <di@example.org>", Me, "Re: Your keys", "Thanks a lot, got them!", 9);
        Add("r1", "recent", Me, "Ed Colleague <ed@example.net>", "Slides", "Here are the slides, any comment?", 2);
        Add("n1", "robot", Me, "Shop <noreply@shop.example>", "Re: Order 12", "Where is my order?", 8);
        Add("l1", "letter", "Rail Weekly <hello@rail.example>", Me, "Night trains", "What do you think of night trains? Reply to vote!", 8, bulk: true);
    }

    [Fact]
    public async Task It_drafts_a_reminder_for_mail_without_answer_and_names_mail_waiting_for_the_person()
    {
        var routine = Create();
        Mailbox();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("1 without an answer, 1 reminders proposed; 1 waiting for an answer.", run.Summary);
        var proposal = Assert.Single(_h.Routines.Proposals);
        Assert.Equal(FollowUp.FollowUpAction, proposal.Action);
        Assert.Equal(ProposalStatus.Pending, proposal.Status);
        Assert.Equal("Remind Bea Builder: Quote for the kitchen", proposal.Title);
        Assert.Contains("Just checking in on the quote.", proposal.Body, StringComparison.Ordinal);
        Assert.Empty(_mailbox.Drafts);
        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(
            "Follow-up\n\nNo answer yet (1)\n• Bea Builder — Quote for the kitchen (sent 7 days ago)\n\nWaiting for your answer (1)\n• Cy Friend — Re: Dinner? (for 8 days)\n\n1 reminder draft waits for your yes in Openspace: I never send it.",
            text);
        Assert.DoesNotContain(_chat.Asked, p => p.Contains("slides", StringComparison.OrdinalIgnoreCase) || p.Contains("order", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_decider.Asked, s => s.Contains("night trains", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_yes_saves_the_reminder_as_a_draft_in_the_same_conversation_and_never_sends_it()
    {
        Create();
        Mailbox();
        await RunAsync(_h.Routines.Items[0]);
        var proposal = Assert.Single(_h.Routines.Proposals);

        await Proposals().ApproveAsync(proposal.Id, default);

        var draft = Assert.Single(_mailbox.Drafts);
        Assert.Equal("quote", draft.ThreadId);
        Assert.Equal("Bea Builder <bea@builders.example>", draft.To);
        Assert.Equal("Re: Quote for the kitchen", draft.Subject);
        Assert.Equal("<q1@mail.example>", draft.InReplyTo);
        Assert.Equal(ProposalStatus.Done, proposal.Status);
    }

    [Fact]
    public async Task A_conversation_comes_up_once_until_a_new_message_starts_it_over()
    {
        var routine = Create();
        Mailbox();
        await RunAsync(routine);
        _h.Clock.Advance(TimeSpan.FromDays(1));

        var second = await RunAsync(routine);
        Assert.Equal("Nothing to follow up.", second.Summary);
        Assert.Single(_h.Outbox.Sent);

        Add("q2", "quote", Me, "Bea Builder <bea@builders.example>", "Re: Quote for the kitchen", "Any news on the quote?", 6);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var third = await RunAsync(routine);

        Assert.Equal("1 without an answer, 1 reminders proposed; 0 waiting for an answer.", third.Summary);
        Assert.Equal(2, _h.Routines.Proposals.Count);
    }

    [Fact]
    public async Task Nothing_quiet_sends_nothing()
    {
        var routine = Create();
        Add("r1", "recent", Me, "Ed Colleague <ed@example.net>", "Slides", "Here are the slides, any comment?", 2);

        var run = await RunAsync(routine);

        Assert.Equal("Nothing to follow up.", run.Summary);
        Assert.Empty(_h.Outbox.Sent);
        Assert.Empty(_h.Routines.Proposals);
    }

    [Fact]
    public async Task Without_a_decider_it_still_reminds_and_says_what_it_skipped()
    {
        var routine = Create();
        Mailbox();
        _decider.Available = false;

        var run = await RunAsync(routine);

        Assert.Equal("1 without an answer, 1 reminders proposed; 0 waiting for an answer.", run.Summary);
        Assert.Contains("No decider", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_summary_speaks_the_person_s_language()
    {
        var routine = Create(language: "Italian");
        Mailbox();

        await RunAsync(routine);

        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.Contains("Nessuna risposta (1)\n• Bea Builder — Quote for the kitchen (inviata 7 giorni fa)", text, StringComparison.Ordinal);
        Assert.Contains("Aspettano una tua risposta (1)\n• Cy Friend — Re: Dinner? (da 8 giorni)", text, StringComparison.Ordinal);
        Assert.Contains("1 bozza di sollecito aspetta il tuo sì", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_account_it_says_what_to_do()
    {
        var run = await RunAsync(Create(account: ""));

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("choose the Google account", run.Summary, StringComparison.Ordinal);
    }

    private sealed class FakeDecider : IDecider
    {
        public bool Available { get; set; } = true;

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyDictionary<string, Decision>> DecideAsync(string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
        {
            Asked.Add(state);
            return Task.FromResult<IReadOnlyDictionary<string, Decision>>(new Dictionary<string, Decision>
            {
                ["answer"] = new(state.Contains('?', StringComparison.Ordinal) ? "true" : "false", 0.9, false),
            });
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Asked.Add(ask.User);
            return Task.FromResult("""{"reminder":"Hi! Just checking in on the quote."}""");
        }
    }
}
