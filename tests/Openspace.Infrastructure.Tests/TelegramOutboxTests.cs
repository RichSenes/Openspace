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
    public void The_channels_are_the_bots_names_never_their_tokens()
    {
        var channels = Outbox().Channels();

        Assert.Equal(["PAPERBOY", "POSTMAN"], channels);
        Assert.DoesNotContain(channels, c => c.Contains("fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_tokens_file_there_are_no_channels() =>
        Assert.Empty(new TelegramOutbox(new HttpClient(), new LiveOptions<TelegramOptions>(new TelegramOptions { Folder = Path.Combine(_folder, "missing") })).Channels());

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
    public async Task A_message_of_its_own_goes_as_html_without_a_preview_and_gives_its_number()
    {
        _telegram.Answer = (HttpStatusCode.OK, """{"ok":true,"result":{"message_id":42}}""");

        var id = await Outbox().PostAsync(Routine("""{"channel":"PAPERBOY"}"""), new("<b>Hi</b>", "Hi"), default);

        Assert.Equal(42, id);
        var (path, body) = Assert.Single(_telegram.Calls);
        Assert.Equal("/bot111:fake-token/sendMessage", path);
        Assert.Contains("text=<b>Hi</b>", body, StringComparison.Ordinal);
        Assert.Contains("parse_mode=HTML", body, StringComparison.Ordinal);
        Assert.Contains("link_preview_options={\"is_disabled\":true}", body, StringComparison.Ordinal);
        Assert.Null(await Outbox().PostAsync(Routine("{}"), new("<b>Hi</b>", "Hi"), default));
    }

    [Fact]
    public async Task A_message_carries_its_buttons_and_answers_another()
    {
        await Outbox().PostAsync(Routine("""{"channel":"PAPERBOY"}"""), new("<b>Job</b>", "Job", [new("✅ Yes", "p:1:y"), new("❌ No", "p:1:n")], ReplyTo: 7), default);

        var body = Assert.Single(_telegram.Calls).Body;
        Assert.Contains("reply_markup={\"inline_keyboard\":[[{\"text\":\"✅ Yes\",\"callback_data\":\"p:1:y\"},{\"text\":\"❌ No\",\"callback_data\":\"p:1:n\"}]]}", body, StringComparison.Ordinal);
        Assert.Contains("reply_parameters={\"message_id\":7,\"allow_sending_without_reply\":true}", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_with_a_file_goes_as_a_document_with_its_formatted_caption()
    {
        var pdf = Path.Combine(_folder, "cv.pdf");
        await File.WriteAllBytesAsync(pdf, [37, 80, 68, 70]);

        await Outbox().PostAsync(Routine("""{"channel":"PAPERBOY"}"""), new("<b>CV</b>", "CV", ReplyTo: 9, FilePath: pdf), default);

        var (path, body) = Assert.Single(_telegram.Calls);
        Assert.Equal("/bot111:fake-token/sendDocument", path);
        Assert.Contains("filename=cv.pdf", body, StringComparison.Ordinal);
        Assert.Contains("<b>CV</b>", body, StringComparison.Ordinal);
        Assert.Contains("\"message_id\":9", body, StringComparison.Ordinal);
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
            Calls.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : Uri.UnescapeDataString((await request.Content.ReadAsStringAsync(cancellationToken)).Replace('+', ' '))));
            return new HttpResponseMessage(Answer.Status) { Content = new StringContent(Answer.Body) };
        }
    }
}
