using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Routines;
using Openspace.Infrastructure.Telegram;

namespace Openspace.Infrastructure.Tests;

/// <summary>The buttons pressed under routines' messages, through made-up answers of the bot: nothing is sent.</summary>
public sealed class TelegramListenerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-listener-{Guid.NewGuid():N}");
    private readonly Bot _bot = new();
    private readonly Answers _answers = new();

    public TelegramListenerTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "telegram.env"), "CAREER=333:fake-token\nPAPERBOY=111:other-fake\n");
        File.WriteAllText(Path.Combine(_folder, "telegram.json"), """{"chat": -100123}""");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private TelegramListener Listener()
    {
        var services = new ServiceCollection().AddSingleton<IChatAnswers>(_answers).BuildServiceProvider();
        return new TelegramListener(
            new Clients(new HttpClient(_bot) { BaseAddress = new Uri("https://telegram.example/") }),
            new LiveOptions<TelegramOptions>(new TelegramOptions { Folder = _folder }),
            services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<TelegramListener>.Instance);
    }

    [Fact]
    public async Task A_press_in_the_person_s_chat_answers_its_proposal_and_its_buttons_give_way()
    {
        _bot.Updates = """
            {"ok":true,"result":[
              {"update_id":10,"callback_query":{"id":"q1","data":"p:abc:y","message":{"message_id":55,"chat":{"id":-100123}}}},
              {"update_id":11,"callback_query":{"id":"q2","data":"p:abc:n","message":{"message_id":56,"chat":{"id":999}}}}]}
            """;
        var listener = Listener();

        await listener.ListenOnceAsync(default);
        await listener.ListenOnceAsync(default);

        Assert.Equal(["p:abc:y"], _answers.Asked);
        Assert.All(_bot.Calls, c => Assert.StartsWith("/bot333:fake-token/", c.Path, StringComparison.Ordinal));
        var calls = _bot.Calls.Select(c => c.Path[(c.Path.LastIndexOf('/') + 1)..]).ToList();
        Assert.Equal(["getUpdates", "answerCallbackQuery", "editMessageReplyMarkup", "answerCallbackQuery", "getUpdates"], calls);
        Assert.Contains("allowed_updates=[\"callback_query\"]", _bot.Calls[0].Body, StringComparison.Ordinal);
        Assert.Contains("text=Noted.", _bot.Calls[1].Body, StringComparison.Ordinal);
        Assert.Contains("message_id=55", _bot.Calls[2].Body, StringComparison.Ordinal);
        Assert.Contains("{\"text\":\"✅ Interested\",\"callback_data\":\"done\"}", _bot.Calls[2].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("text=", _bot.Calls[3].Body, StringComparison.Ordinal);
        Assert.Contains("offset=12", _bot.Calls[4].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_telegram_set_up_nothing_is_read()
    {
        File.Delete(Path.Combine(_folder, "telegram.json"));

        await Listener().ListenOnceAsync(default);

        Assert.Empty(_bot.Calls);
    }

    private sealed class Answers : IChatAnswers
    {
        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<string>> ChannelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(["CAREER"]);

        public Task<ChatAnswer?> AnswerAsync(string data, CancellationToken cancellationToken)
        {
            Asked.Add(data);
            return Task.FromResult<ChatAnswer?>(new ChatAnswer("Noted.", "✅ Interested"));
        }
    }

    private sealed class Clients(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }

    /// <summary>Gives its updates once, then none; answers everything else with ok.</summary>
    private sealed class Bot : HttpMessageHandler
    {
        public List<(string Path, string Body)> Calls { get; } = [];

        public string Updates { get; set; } = """{"ok":true,"result":[]}""";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((path, Uri.UnescapeDataString((await request.Content!.ReadAsStringAsync(cancellationToken)).Replace('+', ' '))));
            var answer = """{"ok":true,"result":true}""";
            if (path.EndsWith("/getUpdates", StringComparison.Ordinal))
            {
                answer = Updates;
                Updates = """{"ok":true,"result":[]}""";
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer) };
        }
    }
}
