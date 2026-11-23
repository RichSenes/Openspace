using System.Net;
using System.Text;
using Openspace.Application.Voice;
using Openspace.Infrastructure.Voice;

namespace Openspace.Infrastructure.Tests;

public sealed class LocalSpeakerTests
{
    private sealed class Server(HttpStatusCode status, byte[] body, string type) : HttpMessageHandler
    {
        public string? Sent { get; private set; }
        public Uri? Asked { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            Sent = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new(type);
            return new HttpResponseMessage(status) { Content = content };
        }
    }

    private sealed class Down : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private static LocalSpeaker Speaker(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new LiveOptions<VoiceOptions>(new VoiceOptions { SpeakUrl = new Uri("http://127.0.0.1:9") }));

    [Fact]
    public async Task It_sends_the_words_and_their_language_and_returns_her_voice()
    {
        var server = new Server(HttpStatusCode.OK, [82, 73, 70, 70], "audio/wav");
        var wav = await Speaker(server).SpeakAsync("Ciao, sono Shiori.", "it", CancellationToken.None);

        Assert.Equal([82, 73, 70, 70], wav);
        Assert.Equal("/speak", server.Asked!.AbsolutePath);
        Assert.Contains("\"text\":\"Ciao, sono Shiori.\"", server.Sent, StringComparison.Ordinal);
        Assert.Contains("\"language\":\"it\"", server.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Its_errors_are_passed_on()
    {
        var server = new Server(HttpStatusCode.InternalServerError, Encoding.UTF8.GetBytes("""{"error":"out of memory"}"""), "application/json");
        var e = await Assert.ThrowsAsync<SpeakerException>(() => Speaker(server).SpeakAsync("Ciao", "it", CancellationToken.None));
        Assert.Equal("out of memory", e.Message);
    }

    [Fact]
    public async Task A_voice_that_is_not_running_says_how_to_start_it()
    {
        var e = await Assert.ThrowsAsync<SpeakerException>(() => Speaker(new Down()).SpeakAsync("Ciao", "it", CancellationToken.None));
        Assert.Contains("speak-server.py", e.Message);
        Assert.False((await Speaker(new Down()).StatusAsync(CancellationToken.None)).Ready);
    }
}
