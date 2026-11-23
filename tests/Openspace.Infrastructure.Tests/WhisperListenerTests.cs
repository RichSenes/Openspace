using System.Net;
using System.Text;
using Openspace.Application.Voice;
using Openspace.Infrastructure.Voice;

namespace Openspace.Infrastructure.Tests;

public sealed class WhisperListenerTests
{
    private sealed class Server(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }
        public long? Length { get; private set; }
        public byte[]? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            Length = request.Content?.Headers.ContentLength;
            Sent = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Down : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    private static WhisperListener Listener(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new LiveOptions<VoiceOptions>(new VoiceOptions { ListenUrl = new Uri("http://127.0.0.1:9") }));

    [Fact]
    public async Task It_sends_the_recording_and_returns_the_words_and_their_language()
    {
        var server = new Server(HttpStatusCode.OK, """{"text":"  Ciao Shiori, com'è andata la notte?  ","language":"it","seconds":1.2}""");
        var heard = await Listener(server).HearAsync(new MemoryStream([1, 2, 3]), null, false, CancellationToken.None);

        Assert.Equal("Ciao Shiori, com'è andata la notte?", heard.Text);
        Assert.Equal("it", heard.Language);
        Assert.Equal([1, 2, 3], server.Sent);
        Assert.Equal(3, server.Length);
        Assert.Equal("/transcribe", server.Asked!.AbsolutePath);
        Assert.Empty(server.Asked.Query);
    }

    [Fact]
    public async Task A_chosen_language_is_passed_on()
    {
        var server = new Server(HttpStatusCode.OK, """{"text":"こんにちは","language":"ja","seconds":0.8}""");
        await Listener(server).HearAsync(new MemoryStream([1]), "ja", false, CancellationToken.None);
        Assert.Equal("?language=ja", server.Asked!.Query);
    }

    [Fact]
    public async Task Listening_for_her_name_asks_for_it_and_says_whether_she_was_called()
    {
        var server = new Server(HttpStatusCode.OK, """{"text":"che ore sono?","language":"it","seconds":0.6,"called":true}""");
        var heard = await Listener(server).HearAsync(new MemoryStream([1]), "it", true, CancellationToken.None);

        Assert.Equal("?language=it&wake=1", server.Asked!.Query);
        Assert.True(heard.Called);
        Assert.Equal("che ore sono?", heard.Text);
    }

    [Fact]
    public async Task A_server_that_is_not_running_says_how_to_start_it()
    {
        var e = await Assert.ThrowsAsync<ListenerException>(() => Listener(new Down()).HearAsync(new MemoryStream([1]), null, false, CancellationToken.None));
        Assert.Contains("listen-server.py", e.Message);
        var status = await Listener(new Down()).StatusAsync(CancellationToken.None);
        Assert.False(status.Ready);
        Assert.Contains("listen-server.py", status.Problem);
    }

    [Fact]
    public async Task Audio_it_cannot_read_comes_back_as_its_reason()
    {
        var server = new Server(HttpStatusCode.BadRequest, """{"error":"the audio could not be read"}""");
        var e = await Assert.ThrowsAsync<ListenerException>(() => Listener(server).HearAsync(new MemoryStream([1]), null, false, CancellationToken.None));
        Assert.Equal("the audio could not be read", e.Message);
    }

    [Fact]
    public async Task Its_status_names_the_model_and_the_device()
    {
        var status = await Listener(new Server(HttpStatusCode.OK, """{"ok":true,"model":"medium","device":"cuda"}""")).StatusAsync(CancellationToken.None);
        Assert.True(status.Ready);
        Assert.Equal("medium", status.Model);
        Assert.Equal("cuda", status.Device);
    }
}
