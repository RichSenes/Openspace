using System.Net;
using System.Text;
using Openspace.Application.Voice;
using Openspace.Infrastructure.Voice;

namespace Openspace.Infrastructure.Tests;

public sealed class LocalSpeakerTests
{
    private sealed class Server(HttpStatusCode status, byte[] body, string type, string? rate = null) : HttpMessageHandler
    {
        public string? Sent { get; private set; }
        public Uri? Asked { get; private set; }

        public long? Length { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            Length = request.Content?.Headers.ContentLength;
            Sent = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new(type);
            var response = new HttpResponseMessage(status) { Content = content };
            if (rate is not null)
                response.Headers.Add("X-Sample-Rate", rate);
            return response;
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
        Assert.NotNull(server.Length); // the Python server reads Content-Length, not a chunked body
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

    [Fact]
    public async Task Streaming_asks_for_pieces_and_says_their_sample_rate()
    {
        var server = new Server(HttpStatusCode.OK, [1, 0, 2, 0], "audio/pcm", "48000");
        await using var spoken = await Speaker(server).StreamAsync("Ciao.", "en", CancellationToken.None);
        using var copy = new MemoryStream();
        await spoken.Audio.CopyToAsync(copy);

        Assert.Equal(48000, spoken.SampleRate);
        Assert.Equal([1, 0, 2, 0], copy.ToArray());
        Assert.Contains("\"stream\":true", server.Sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_full_graphics_card_is_told_apart_from_a_failure()
    {
        var server = new Server(HttpStatusCode.ServiceUnavailable, Encoding.UTF8.GetBytes("""{"error":"the graphics card is busy","busy":true}"""), "application/json");
        var e = await Assert.ThrowsAsync<SpeakerException>(() => Speaker(server).StreamAsync("Ciao", "it", CancellationToken.None));
        Assert.True(e.Busy);
        var woken = await Assert.ThrowsAsync<SpeakerException>(() => Speaker(server).WakeAsync(CancellationToken.None));
        Assert.True(woken.Busy);
    }

    [Fact]
    public async Task Sleeping_says_whether_she_left_the_card()
    {
        var awake = new Server(HttpStatusCode.OK, Encoding.UTF8.GetBytes("""{"state":"awake"}"""), "application/json");
        Assert.False(await Speaker(awake).SleepAsync(TimeSpan.FromMinutes(1), CancellationToken.None));
        Assert.Contains("\"quietFor\":60", awake.Sent, StringComparison.Ordinal);

        var asleep = new Server(HttpStatusCode.OK, Encoding.UTF8.GetBytes("""{"state":"asleep"}"""), "application/json");
        Assert.True(await Speaker(asleep).SleepAsync(TimeSpan.Zero, CancellationToken.None));
        Assert.True(await Speaker(new Down()).SleepAsync(TimeSpan.Zero, CancellationToken.None));
    }
}
