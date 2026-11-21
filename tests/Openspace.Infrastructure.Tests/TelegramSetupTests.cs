using System.Net;
using System.Text.Json;
using Openspace.Application;
using Openspace.Infrastructure.Telegram;

namespace Openspace.Infrastructure.Tests;

/// <summary>The Telegram setup against a made-up Telegram, in a scratch folder: no real bot, no real token.</summary>
public sealed class TelegramSetupTests : IDisposable
{
    private const string Token = "123456:made-up-token-for-the-tests";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-telegram-setup-{Guid.NewGuid():N}");
    private readonly FakeTelegram _telegram = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private TelegramSetup Setup() =>
        new(new HttpClient(_telegram) { BaseAddress = new Uri("https://telegram.example/") }, new LiveOptions<TelegramOptions>(new TelegramOptions { Folder = _folder }));

    private string Env => Path.Combine(_folder, TelegramSetup.TokensFile);

    private string Json => Path.Combine(_folder, TelegramSetup.TargetsFile);

    [Fact]
    public async Task A_bot_is_checked_with_telegram_then_kept_in_a_file_only_the_person_can_read()
    {
        var username = await Setup().AddBotAsync(" paper ", Token, 7, default);

        Assert.Equal("made_up_paper_bot", username);
        Assert.Equal("/bot" + Token + "/getMe", Assert.Single(_telegram.Calls).Path);
        Assert.Contains($"PAPER={Token}", await File.ReadAllLinesAsync(Env));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Env));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Json));
        }

        var status = await Setup().StatusAsync(default);
        Assert.Equal([new("PAPER", 7)], status.Bots);
        Assert.DoesNotContain("made-up-token", JsonSerializer.Serialize(status), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bot_added_again_replaces_its_token_and_keeps_the_other_lines()
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Env, "# mine\nPOSTMAN=999:other-made-up-token-here\nPAPER=111:old-made-up-token-here\n");
        await File.WriteAllTextAsync(Json, """{"chat": -100123, "topics": {"POSTMAN": 3, "PAPER": 2}, "note": "kept"}""");

        await Setup().AddBotAsync("PAPER", Token, null, default);

        var lines = await File.ReadAllLinesAsync(Env);
        Assert.Equal(["# mine", "POSTMAN=999:other-made-up-token-here", $"PAPER={Token}"], lines);
        var status = await Setup().StatusAsync(default);
        Assert.Equal(-100123, status.Chat);
        Assert.Equal([new("POSTMAN", 3), new("PAPER", null)], status.Bots.OrderByDescending(b => b.Name));
        Assert.Contains("\"note\": \"kept\"", await File.ReadAllTextAsync(Json), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_telegram_refuses_or_that_is_not_one_is_never_kept()
    {
        _telegram.Answer = (HttpStatusCode.Unauthorized, """{"ok":false,"description":"Unauthorized"}""");

        var refused = await Assert.ThrowsAsync<RejectedException>(() => Setup().AddBotAsync("PAPER", Token, null, default));
        var malformed = await Assert.ThrowsAsync<RejectedException>(() => Setup().AddBotAsync("PAPER", "not a token", null, default));
        var badName = await Assert.ThrowsAsync<RejectedException>(() => Setup().AddBotAsync("my bot!", Token, null, default));

        Assert.Equal(("telegram-refused", "telegram-token", "telegram-name"), (refused.Code, malformed.Code, badName.Code));
        Assert.Contains("Unauthorized", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, refused.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Env));
    }

    [Fact]
    public async Task The_group_and_a_bot_s_topic_are_set_and_a_bot_can_be_removed()
    {
        await Setup().AddBotAsync("PAPER", Token, null, default);

        await Setup().SetChatAsync(-100777, default);
        await Setup().SetTopicAsync("paper", 12, default);
        Assert.Equal((-100777L, 12L), ((await Setup().StatusAsync(default)).Chat!.Value, (await Setup().StatusAsync(default)).Bots.Single().Topic!.Value));

        await Setup().RemoveBotAsync("PAPER", default);
        var status = await Setup().StatusAsync(default);
        Assert.Empty(status.Bots);
        Assert.Equal(-100777, status.Chat);
        Assert.DoesNotContain(Token, await File.ReadAllTextAsync(Env), StringComparison.Ordinal);
        await Assert.ThrowsAsync<RejectedException>(() => Setup().SetTopicAsync("PAPER", 1, default));
    }

    [Fact]
    public async Task The_test_message_goes_where_the_bot_s_routines_write_and_waits_for_a_group()
    {
        await Setup().AddBotAsync("PAPER", Token, 4, default);
        var noGroup = await Assert.ThrowsAsync<RejectedException>(() => Setup().TestAsync("PAPER", default));
        await Setup().SetChatAsync(-100777, default);

        await Setup().TestAsync("PAPER", default);

        Assert.Equal("telegram-no-chat", noGroup.Code);
        var (path, body) = _telegram.Calls[^1];
        Assert.Equal($"/bot{Token}/sendMessage", path);
        Assert.Contains("chat_id=-100777", body, StringComparison.Ordinal);
        Assert.Contains("message_thread_id=4", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chats_are_found_in_the_bot_s_messages_with_their_topics_without_marking_them_read()
    {
        await Setup().AddBotAsync("PAPER", Token, null, default);
        _telegram.Answer = (HttpStatusCode.OK, """
            {"ok":true,"result":[
              {"update_id":1,"my_chat_member":{"chat":{"id":-100777,"title":"Fsociety","type":"supergroup"}}},
              {"update_id":2,"message":{"chat":{"id":-100777,"title":"Fsociety","type":"supergroup"},"message_thread_id":5,"is_topic_message":true,
                "reply_to_message":{"forum_topic_created":{"name":"Morning paper"}}}},
              {"update_id":3,"message":{"chat":{"id":42,"first_name":"Darlene","type":"private"}}}
            ]}
            """);

        var chats = await Setup().FindChatsAsync("PAPER", default);

        Assert.Equal([-100777L, 42L], chats.Select(c => c.Id).Order());
        var group = chats.Single(c => c.Id == -100777);
        Assert.Equal(("Fsociety", "supergroup"), (group.Title, group.Kind));
        Assert.Equal([new(5, "Morning paper")], group.Topics);
        Assert.DoesNotContain("offset", _telegram.Calls[^1].Body, StringComparison.Ordinal);
    }

    private sealed class FakeTelegram : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];

        public (HttpStatusCode Status, string Body)? Answer { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((path, request.Content is null ? "" : Uri.UnescapeDataString((await request.Content.ReadAsStringAsync(cancellationToken)).Replace('+', ' '))));
            var (status, body) = Answer ?? (HttpStatusCode.OK, path.EndsWith("/getMe", StringComparison.Ordinal)
                ? """{"ok":true,"result":{"id":1,"is_bot":true,"username":"made_up_paper_bot"}}"""
                : """{"ok":true,"result":{"message_id":1}}""");
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }
}
