using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The mail routine on a made-up mailbox: no real account, no real model.</summary>
public sealed class MailSortingTests
{
    private readonly Harness _h = new();
    private readonly FakeMailbox _mailbox = new();
    private readonly FakeDecider _decider = new();
    private readonly FakeChat _chat = new();

    private MailSorting Kind() => new(_mailbox, _decider, _chat);

    private Routine Create(bool askFirst = false, string account = "someone@example.com", string language = "English")
    {
        var settings = $$"""
            {"title":"Post","account":"{{account}}","language":"{{language}}","channel":"POSTMAN","labelPrefix":"Openspace","me":"A kind person.",
             "labels":[
               {"name":"To answer","about":"A person waits for my answer.","archive":false,"reply":true},
               {"name":"Newsletters","about":"Letters I signed up for.","archive":true},
               {"name":"Receipts","about":"Orders and invoices.","archive":true}]}
            """;
        var routine = Routine.Create(_h.Office, "Post", "mail", RoutineSchedule.Daily(new TimeOnly(7, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        routine.AskBeforeActing(askFirst, _h.Clock.GetUtcNow());
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

    private static MailMessage Mail(string id, string from, string subject, string text = "Hello.", bool bulk = false) =>
        new(id, $"thread-{id}", from, from, subject, $"<{id}@mail.example>", Harness.Start, text, text, Unread: true, bulk);

    private void Inbox()
    {
        _mailbox.Messages.Add(Mail("m1", "Ada Example <ada@example.com>", "Dinner on Saturday?", "Shall we meet at eight?"));
        _mailbox.Messages.Add(Mail("m2", "News <news@letters.example>", "This week in trains", bulk: true));
        _mailbox.Messages.Add(Mail("m3", "Shop <orders@shop.example>", "Your receipt", bulk: true));
        _mailbox.Messages.Add(Mail("m4", "Bob Example <bob@example.com>", "Photos from the trip"));
    }

    [Fact]
    public async Task It_labels_and_archives_on_its_own_what_needs_no_reading_and_sums_up_the_rest()
    {
        var routine = Create();
        Inbox();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("4 new: 2 sorted away, 2 kept, 1 reply drafts proposed.", run.Summary);
        Assert.Equal(
            [("m1", "Openspace/To answer", false), ("m2", "Openspace/Newsletters", true), ("m3", "Openspace/Receipts", true)],
            _mailbox.Labelled);
        Assert.Equal("someone@example.com", _mailbox.Accounts.Distinct().Single());

        var (_, text, file) = Assert.Single(_h.Outbox.Sent);
        Assert.Null(file);
        Assert.StartsWith("Post: 4 new emails", text, StringComparison.Ordinal);
        Assert.Contains("To answer (1)\n• Ada Example — Dinner on Saturday?\n  About: Dinner on Saturday?", text, StringComparison.Ordinal);
        Assert.Contains("Staying in the inbox (1)\n• Bob Example — Photos from the trip", text, StringComparison.Ordinal);
        Assert.Contains("Sorted and archived: Newsletters 1, Receipts 1", text, StringComparison.Ordinal);
        Assert.Contains("1 reply draft waits for your yes", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mail_already_met_is_left_alone_the_next_time()
    {
        var routine = Create();
        Inbox();
        await RunAsync(routine);
        _mailbox.Labelled.Clear();
        _h.Outbox.Sent.Clear();

        _h.Clock.Advance(TimeSpan.FromDays(1));
        var run = await RunAsync(routine);

        Assert.Equal("No new mail.", run.Summary);
        Assert.Empty(_mailbox.Labelled);
        Assert.Empty(_h.Outbox.Sent);
    }

    [Fact]
    public async Task A_reply_is_only_a_draft_after_a_yes_and_never_sent()
    {
        var routine = Create();
        Inbox();
        await RunAsync(routine);

        var draft = Assert.Single(_h.Routines.Proposals, p => p.Action == MailSorting.ReplyAction);
        Assert.Equal(ProposalStatus.Pending, draft.Status);
        Assert.Equal("Reply to Ada Example: Dinner on Saturday?", draft.Title);
        Assert.Contains("Eight is perfect.", draft.Body, StringComparison.Ordinal);
        Assert.Empty(_mailbox.Drafts);

        await Proposals().ApproveAsync(draft.Id, default);

        var reply = Assert.Single(_mailbox.Drafts);
        Assert.Equal(new MailReply("thread-m1", "Ada Example <ada@example.com>", "Re: Dinner on Saturday?", "<m1@mail.example>", "Eight is perfect."), reply);
        Assert.Equal(ProposalStatus.Done, draft.Status);
        Assert.Contains("send it yourself", draft.Outcome, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asked_first_it_touches_nothing_until_each_label_gets_a_yes()
    {
        var routine = Create(askFirst: true);
        Inbox();

        await RunAsync(routine);

        Assert.Empty(_mailbox.Labelled);
        var labels = _h.Routines.Proposals.Where(p => p.Action == MailSorting.LabelAction).ToList();
        Assert.Equal(3, labels.Count);
        Assert.All(labels, p => Assert.Equal(ProposalStatus.Pending, p.Status));
        var text = Assert.Single(_h.Outbox.Sent).Text;
        Assert.Contains("Staying in the inbox (3)", text, StringComparison.Ordinal);
        Assert.Contains("3 labels wait for your yes", text, StringComparison.Ordinal);

        await Proposals().ApproveAsync(labels.Single(p => p.Title == "Receipts: Your receipt").Id, default);
        Assert.Equal([("m3", "Openspace/Receipts", true)], _mailbox.Labelled);
    }

    [Fact]
    public async Task What_the_decider_is_not_sure_of_stays_in_the_inbox_and_lists_get_no_replies()
    {
        var routine = Create();
        _mailbox.Messages.Add(Mail("m5", "Club <club@lists.example>", "Unsure: meeting minutes", bulk: true));
        _mailbox.Messages.Add(Mail("m6", "Club <club@lists.example>", "Reply wanted from all members", bulk: true));

        var run = await RunAsync(routine);

        Assert.Equal([("m6", "Openspace/To answer", false)], _mailbox.Labelled);
        Assert.DoesNotContain(_h.Routines.Proposals, p => p.Action == MailSorting.ReplyAction);
        Assert.Equal("2 new: 0 sorted away, 2 kept, 0 reply drafts proposed.", run.Summary);
    }

    [Fact]
    public async Task Without_a_decider_nothing_is_moved_but_the_summary_still_comes()
    {
        var routine = Create();
        Inbox();
        _decider.Available = false;

        var run = await RunAsync(routine);

        Assert.Empty(_mailbox.Labelled);
        Assert.Contains("No decider: the mail stays as it is.", run.Log, StringComparison.Ordinal);
        Assert.Contains("Staying in the inbox (4)", Assert.Single(_h.Outbox.Sent).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_summary_speaks_the_person_s_language_and_survives_a_silent_model()
    {
        var routine = Create(language: "Italian");
        Inbox();
        _chat.Fail = true;

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        var text = Assert.Single(_h.Outbox.Sent).Text;
        Assert.StartsWith("Post: 4 mail nuove", text, StringComparison.Ordinal);
        Assert.Contains("Restano nella posta in arrivo (1)\n• Bob Example — Photos from the trip", text, StringComparison.Ordinal);
        Assert.DoesNotContain("About:", text, StringComparison.Ordinal);
        Assert.Contains("subjects only", run.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_routine_without_an_account_says_what_to_do()
    {
        var routine = Create(account: "");

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("choose the Google account", run.Summary, StringComparison.Ordinal);
        Assert.Empty(_mailbox.Accounts);
    }

    [Theory]
    [InlineData("\"Ada Example\" <ada@example.com>", "Ada Example")]
    [InlineData("<ada@example.com>", "ada@example.com")]
    [InlineData("Shop <orders@shop.example>", "Shop")]
    public void The_sender_is_named_as_people_say_it(string from, string name) => Assert.Equal(name, MailSorting.Sender(from));

    private sealed class FakeMailbox : IMailbox
    {
        public List<MailMessage> Messages { get; } = [];

        public List<(string Id, string Label, bool Archive)> Labelled { get; } = [];

        public List<MailReply> Drafts { get; } = [];

        public List<string> Accounts { get; } = [];

        public Task<IReadOnlyList<MailMessage>> ListAsync(string account, string query, int most, CancellationToken cancellationToken)
        {
            Accounts.Add(account);
            return Task.FromResult<IReadOnlyList<MailMessage>>([.. Messages.Take(most)]);
        }

        public Task LabelAsync(string account, string messageId, string label, bool archive, CancellationToken cancellationToken)
        {
            Accounts.Add(account);
            Labelled.Add((messageId, label, archive));
            return Task.CompletedTask;
        }

        public Task<string> DraftReplyAsync(string account, MailReply reply, CancellationToken cancellationToken)
        {
            Accounts.Add(account);
            Drafts.Add(reply);
            return Task.FromResult($"draft-{Drafts.Count}");
        }
    }

    /// <summary>Sorts by words in the subject; "Unsure" makes it unsure.</summary>
    private sealed class FakeDecider : IDecider
    {
        public bool Available { get; set; } = true;

        public Task<IReadOnlyDictionary<string, Decision>> DecideAsync(string state, IReadOnlyDictionary<string, DecisionQuestion> questions, CancellationToken cancellationToken)
        {
            bool Has(string word) => state.Contains(word, StringComparison.OrdinalIgnoreCase);
            var options = questions["label"].Options!;
            Assert.Equal("none", options[^1].Id);
            var answer = Has("Unsure") ? new Decision("label2", 0.4, false)
                : Has("Dinner") || Has("Reply wanted") ? new Decision("label1", 0.9, false)
                : Has("trains") ? new Decision("label2", 0.8, false)
                : Has("receipt") ? new Decision("label3", 0.95, false)
                : new Decision("none", 0.7, false);
            return Task.FromResult<IReadOnlyDictionary<string, Decision>>(new Dictionary<string, Decision> { ["label"] = answer });
        }
    }

    private sealed class FakeChat : ILocalChat
    {
        public bool Fail { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The model is busy.");
            }

            if (ask.User.StartsWith("These emails stay in the inbox", StringComparison.Ordinal))
            {
                var subjects = ask.User.Split('\n').Where(l => l.StartsWith('[')).Select(l => l[(l.IndexOf("subject ", StringComparison.Ordinal) + 8)..]).ToList();
                return Task.FromResult(JsonSerializer.Serialize(new { gists = subjects.Select((s, i) => new { n = i + 1, text = $"About: {s}" }) }));
            }

            return Task.FromResult("""{"reply":"Eight is perfect."}""");
        }
    }
}
