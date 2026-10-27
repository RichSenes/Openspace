using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.LocalModels;
using Openspace.Infrastructure.LocalModels;

namespace Openspace.Infrastructure.Tests;

public sealed class LocalModelApiTests : IDisposable
{
    private readonly string _key = Path.Combine(Path.GetTempPath(), $"openspace-key-{Guid.NewGuid():N}");
    private readonly Recorder _server = new();

    public LocalModelApiTests() => File.WriteAllText(_key, "made-up-key\n");

    public void Dispose() => File.Delete(_key);

    private LlamaServerApi Api() => new(
        new HttpClient(_server),
        new LiveOptions<LocalModelOptions>(new LocalModelOptions { ServerUrl = new Uri("http://127.0.0.1:9"), ApiKeyFile = _key }));

    [Fact]
    public async Task It_asks_with_the_key_and_the_schema_and_returns_the_answer()
    {
        _server.Answer = """{"choices":[{"message":{"content":"  {\"title\":\"Riku Shinbun\"} "},"finish_reason":"stop"}]}""";

        var answer = await Api().CompleteAsync(new LocalAsk("local/gemma", "You edit a paper.", "Name it.", """{"type":"object"}""", 512), default);

        Assert.Equal("""{"title":"Riku Shinbun"}""", answer);
        Assert.Equal(("http://127.0.0.1:9/v1/chat/completions", "Bearer made-up-key"), (_server.Url, _server.Authorization));
        Assert.Contains("\"json_schema\"", _server.Body, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"gemma\"", _server.Body, StringComparison.Ordinal);
        Assert.Contains("\"max_tokens\":512", _server.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_json_answer_cut_short_is_an_error_and_so_is_a_refusal()
    {
        _server.Answer = """{"choices":[{"message":{"content":"{\"title\":"},"finish_reason":"length"}]}""";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Api().CompleteAsync(new LocalAsk("local/gemma", "s", "u", "{}"), default));

        _server.Status = HttpStatusCode.ServiceUnavailable;
        _server.Answer = """{"error":"loading model"}""";
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Api().CompleteAsync(new LocalAsk("local/gemma", "s", "u"), default));
        Assert.Contains("503", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decisions_keep_the_likeliest_real_option_and_say_when_it_abstained()
    {
        var decisions = DeciderClient.Read("""
            {"answers":{
              "section":{"status":"ok","probabilities":{"japan":0.7,"ai":0.2,"__insufficient__":0.1}},
              "relevant":{"status":"ok","probabilities":{"true":0.35,"false":0.65}},
              "mood":{"status":"insufficient","probabilities":{"__insufficient__":0.9,"calm":0.1}}}}
            """);

        Assert.Equal(new Decision("japan", 0.7, false), decisions["section"]);
        Assert.Equal("false", decisions["relevant"].Answer);
        Assert.True(decisions["mood"].Abstained);
    }

    [Fact]
    public async Task The_decider_is_not_stopped_as_idle_while_it_starts()
    {
        using var provider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var options = new LocalModelOptions
        {
            Decider = new DeciderOptions { Command = "/bin/sh", Arguments = ["-c", "sleep 30"], Url = new Uri("http://127.0.0.1:9"), StartTimeout = TimeSpan.FromSeconds(3) },
        };
        using var decider = new DeciderClient(
            provider.GetRequiredService<IHttpClientFactory>(),
            new LiveOptions<LocalModelOptions>(options), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalModelServer>.Instance);

        var asking = decider.DecideAsync("A text.", new Dictionary<string, DecisionQuestion> { ["ok"] = new("boolean", "Is it fine?") }, default);
        await Task.Delay(500);
        await decider.StopIfIdleAsync(default);

        Assert.Equal("decider", decider.Running);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => asking);
        Assert.Contains("did not start", error.Message, StringComparison.Ordinal);
        Assert.Null(decider.Running);
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public string Answer { get; set; } = "{}";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string? Url { get; private set; }

        public string? Authorization { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Answer) };
        }
    }
}
