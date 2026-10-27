using System.Net;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Infrastructure.Telegram;

namespace Openspace.Infrastructure.Tests;

public sealed class TelegramOutboxTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-telegram-{Guid.NewGuid():N}");
    private readonly Recorder _telegram = new();

    public TelegramOutboxTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "telegram.env"), "# made-up tokens\nPAPERBOY=111:fake-token\nPOSTMAN = 222:other-fake\n");
        File.WriteAllText(Path.Combine(_folder, "telegram.json"), """{"chat": -100123, "topics": {"PAPERBOY": 2}}""");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private TelegramOutbox Outbox() =>
        new(new HttpClient(_telegram) { BaseAddress = new Uri("https://telegram.example/") }, new LiveOptions<TelegramOptions>(new TelegramOptions { Folder = _folder }));

    private static Routine Routine(string settings) =>
        Domain.Routines.Routine.Create(
            Office.Open("E Corp", DateTimeOffset.UnixEpoch), "Paper", "morning-paper", RoutineSchedule.Daily(new TimeOnly(7, 0)), settings,
            DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc);

    [Fact]
    public async Task A_routine_writes_with_its_bot_in_that_bot_s_topic()
    {
        await Outbox().SendAsync(Routine("""{"channel":"paperboy"}"""), "Good morning.", null, default);

        var (path, body) = Assert.Single(_telegram.Calls);
        Assert.Equal("/bot111:fake-token/sendMessage", path);
        Assert.Contains("chat_id=-100123", body, StringComparison.Ordinal);
        Assert.Contains("message_thread_id=2", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bot_without_a_topic_writes_in_the_group_and_long_text_is_split_on_lines()
    {
        var text = string.Join('\n', Enumerable.Repeat(new string('a', 99), 60));

        await Outbox().SendAsync(Routine("""{"channel":"POSTMAN"}"""), text, null, default);

        Assert.Equal(2, _telegram.Calls.Count);
        Assert.All(_telegram.Calls, c => Assert.DoesNotContain("message_thread_id", c.Body, StringComparison.Ordinal));
        Assert.All(TelegramOutbox.Chunks(text), c => Assert.True(c.Length <= 4000));
    }

    [Fact]
    public async Task A_file_goes_as_a_document_with_the_text_as_caption()
    {
        var pdf = Path.Combine(_folder, "paper.pdf");
        await File.WriteAllBytesAsync(pdf, [37, 80, 68, 70]);

        await Outbox().SendAsync(Routine("""{"channel":"PAPERBOY"}"""), "Issue 1", pdf, default);

        var (path, body) = Assert.Single(_telegram.Calls);
        Assert.Equal("/bot111:fake-token/sendDocument", path);
        Assert.Contains("filename=paper.pdf", body, StringComparison.Ordinal);
        Assert.Contains("Issue 1", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_routine_without_a_channel_delivers_nothing()
    {
        Assert.False(await Outbox().SendAsync(Routine("{}"), "Nobody reads this.", null, default));

        Assert.Empty(_telegram.Calls);
    }

    [Fact]
    public async Task Refusals_say_why_without_the_token()
    {
        _telegram.Answer = (HttpStatusCode.BadRequest, """{"ok":false,"description":"Bad Request: chat not found"}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Outbox().SendAsync(Routine("""{"channel":"PAPERBOY"}"""), "Hi", null, default));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => Outbox().SendAsync(Routine("""{"channel":"NOBODY"}"""), "Hi", null, default));

        Assert.Equal("Telegram refused the message: Bad Request: chat not found", error.Message);
        Assert.DoesNotContain("fake-token", error.Message + missing.Message, StringComparison.Ordinal);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];

        public (HttpStatusCode Status, string Body) Answer { get; set; } = (HttpStatusCode.OK, """{"ok":true}""");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : Uri.UnescapeDataString(await request.Content.ReadAsStringAsync(cancellationToken))));
            return new HttpResponseMessage(Answer.Status) { Content = new StringContent(Answer.Body) };
        }
    }
}
