using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.LocalModels;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>The newsletter digest on a made-up mailbox: no real account, no real model.</summary>
public sealed class NewsletterDigestTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-digest-{Guid.NewGuid():N}");
    private readonly FakeMailbox _mailbox = new();
    private readonly FakeChat _chat = new();
    private readonly FakePrinter _printer = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private NewsletterDigest Kind() => new(_mailbox, _chat, _printer);

    private Routine Create(int most = 20, string language = "English", string account = "someone@example.com")
    {
        var settings = $$"""
            {"title":"Letters","account":"{{account}}","language":"{{language}}","channel":"LETTERS","folder":{{JsonSerializer.Serialize(_folder)}},
             "query":"label:openspace-newsletters","most":{{most}},"reader":"Likes trains."}
            """;
        var routine = Routine.Create(_h.Office, "Letters", "newsletters", RoutineSchedule.Daily(new TimeOnly(8, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
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

    private string Html() => File.ReadAllText(Assert.Single(Directory.GetFiles(_folder, "*.html")));

    private static MailMessage Letter(string id, string from, string subject, string text) =>
        new(id, $"thread-{id}", from, from, subject, $"<{id}@letters.example>", Harness.Start.AddHours(-3), text, text, Unread: true, Bulk: true);

    private void Letters()
    {
        _mailbox.Messages.Add(Letter("n1", "Rail Weekly <hello@rail.example>", "New night trains", "Night trains are back on three lines."));
        _mailbox.Messages.Add(Letter("n2", "Chip Notes <news@chips.example>", "A faster chip", "A new chip runs local models twice as fast."));
        _mailbox.Messages.Add(Letter("n3", "Rail Weekly <hello@rail.example>", "Station news", "A station reopens downtown."));
        _mailbox.Messages.Add(Letter("n4", "Deals <deals@shop.example>", "50% off everything", "Buy now. Sale ends tonight."));
    }

    [Fact]
    public async Task It_prints_the_newsletters_summed_up_by_topic_numbered_and_delivers_them()
    {
        var routine = Create();
        Letters();

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("N. 1: 3 newsletters in 2 topics, 1 left out as advertising.", run.Summary);
        var pdf = Assert.Single(Directory.GetFiles(_folder, "*.pdf"));
        Assert.EndsWith("Letters-N001-2015-06-24.pdf", pdf, StringComparison.Ordinal);
        var html = Html();
        Assert.True(html.IndexOf("<h2>Trains</h2>", StringComparison.Ordinal) < html.IndexOf("<h2>Technology</h2>", StringComparison.Ordinal), "The topic with most newsletters comes first.");
        Assert.Contains("<h3>About New night trains</h3>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://mail.google.com/mail/?authuser=someone%40example.com#all/thread-n1\"", html, StringComparison.Ordinal);
        Assert.Contains(">Rail Weekly</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("50% off", html, StringComparison.Ordinal);
        Assert.Contains("Summed up by a local model", html, StringComparison.Ordinal);
        var (_, caption, file) = Assert.Single(_h.Outbox.Sent);
        Assert.Equal(pdf, file);
        Assert.Equal("Letters · N. 1 · Wednesday 24 June 2015\nTrains 2 · Technology 1", caption);
        Assert.All(_mailbox.Accounts, a => Assert.Equal("someone@example.com", a));
    }

    [Fact]
    public async Task A_newsletter_is_summed_up_once_and_issues_are_numbered_on()
    {
        var routine = Create();
        Letters();
        await RunAsync(routine);
        Assert.Equal("No new newsletters.", (await RunAsync(routine)).Summary);

        _mailbox.Messages.Add(Letter("n5", "Chip Notes <news@chips.example>", "Another chip", "Smaller and cooler."));
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var third = await RunAsync(routine);

        Assert.Equal("N. 2: 1 newsletters in 1 topics.", third.Summary);
        Assert.Equal(1, _chat.Asked.Count(p => p.StartsWith("A newsletter.", StringComparison.Ordinal) && p.Contains("Another chip", StringComparison.Ordinal)));
        Assert.Equal(2, _h.Outbox.Sent.Count);
    }

    [Fact]
    public async Task An_issue_takes_the_newest_up_to_its_limit_and_leaves_the_rest_for_the_next()
    {
        var routine = Create(most: 2);
        Letters();

        var first = await RunAsync(routine);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var second = await RunAsync(routine);

        Assert.StartsWith("N. 1: 2 newsletters", first.Summary, StringComparison.Ordinal);
        Assert.Equal("N. 2: 1 newsletters in 1 topics, 1 left out as advertising.", second.Summary);
    }

    [Fact]
    public async Task A_silent_model_fails_the_run_without_marking_the_newsletters_done()
    {
        var routine = Create();
        Letters();
        _chat.Fail = true;

        var run = await RunAsync(routine);

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("could not sum up any newsletter", run.Summary, StringComparison.Ordinal);
        Assert.Empty(_h.Outbox.Sent);
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder, "*.pdf").Length > 0);
    }

    [Fact]
    public async Task The_page_speaks_the_person_s_language()
    {
        var routine = Create(language: "Italian");
        Letters();

        await RunAsync(routine);

        var html = Html();
        Assert.Contains("<html lang=\"it\">", html, StringComparison.Ordinal);
        Assert.Contains("In questo numero", html, StringComparison.Ordinal);
        Assert.Contains("Mercoled&#236; 24 giugno 2015", html, StringComparison.Ordinal);
        Assert.Contains("Write in Italian", _chat.Systems[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_account_it_says_what_to_do()
    {
        var run = await RunAsync(Create(account: ""));

        Assert.Equal(RoutineRunStatus.Failed, run.Status);
        Assert.Contains("choose the Google account", run.Summary, StringComparison.Ordinal);
    }

    private sealed class FakeChat : ILocalChat
    {
        public List<string> Asked { get; } = [];

        public List<string> Systems { get; } = [];

        public bool Fail { get; set; }

        public Task<string> AskAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The model is busy.");
            }

            Asked.Add(ask.User);
            Systems.Add(ask.System);
            if (ask.User.StartsWith("This issue's newsletters", StringComparison.Ordinal))
            {
                return Task.FromResult("""{"title":"Rails and chips","text":"A busy week."}""");
            }

            var subject = ask.User.Split('\n').First(l => l.StartsWith("Subject: ", StringComparison.Ordinal))[9..];
            var topic = ask.User.Contains("train", StringComparison.OrdinalIgnoreCase) || ask.User.Contains("station", StringComparison.OrdinalIgnoreCase)
                ? (subject.StartsWith("Station", StringComparison.Ordinal) ? "trains." : "Trains")
                : "Technology";
            return Task.FromResult(subject.Contains("50%", StringComparison.Ordinal)
                ? """{"headline":"","summary":"","topic":"","worth":false}"""
                : JsonSerializer.Serialize(new { headline = $"About {subject}", summary = $"What {subject} says.", topic, worth = true }));
        }
    }

    private sealed class FakePrinter : IPdfPrinter
    {
        public Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken) => Task.FromResult("%PDF-1.7"u8.ToArray());
    }
}
