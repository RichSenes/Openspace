using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Voice;

namespace Openspace.Api.Tests;

public sealed class VoiceApiTests : IDisposable
{
    private sealed class Ears : IListener
    {
        public string? Language { get; private set; }
        public int Bytes { get; private set; }
        public bool Wake { get; private set; }

        public async Task<Heard> HearAsync(Stream audio, string? language, bool wake, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await audio.CopyToAsync(copy, cancellationToken);
            Bytes = (int)copy.Length;
            Language = language;
            Wake = wake;
            return new Heard(wake ? "cosa c'è in coda oggi?" : "Shiori, cosa c'è in coda oggi?", "it", 0.9, wake);
        }

        public Task<ListenerStatus> StatusAsync(CancellationToken cancellationToken) => Task.FromResult(new ListenerStatus(true, "medium", "cpu", null));
    }

    private readonly OpenspaceApp _app = new();
    private readonly Ears _ears = new();

    private HttpClient Client() =>
        _app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IListener>(_ears))).CreateClient();

    [Fact]
    public async Task What_was_said_comes_back_as_text()
    {
        using var body = new ByteArrayContent(new byte[1234]);
        var response = await Client().PostAsync("/api/voice/hear?language=xx", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var heard = await response.Content.ReadFromJsonAsync<Heard>();
        Assert.Equal("Shiori, cosa c'è in coda oggi?", heard!.Text);
        Assert.Equal(1234, _ears.Bytes);
        Assert.Null(_ears.Language);
    }

    [Fact]
    public async Task Always_listening_only_keeps_what_follows_her_name()
    {
        using var body = new ByteArrayContent(new byte[64]);
        var heard = await (await Client().PostAsync("/api/voice/hear?wake=true", body)).Content.ReadFromJsonAsync<Heard>();

        Assert.True(_ears.Wake);
        Assert.True(heard!.Called);
        Assert.Equal("cosa c'è in coda oggi?", heard.Text);
    }

    private sealed class Voice : ISpeaker
    {
        public string? Language { get; private set; }

        public Task<byte[]> SpeakAsync(string text, string language, CancellationToken cancellationToken)
        {
            Language = language;
            return Task.FromResult<byte[]>([82, 73, 70, 70]);
        }

        public bool Busy { get; init; }

        public Task<SpokenAudio> StreamAsync(string text, string language, CancellationToken cancellationToken)
        {
            Language = language;
            if (Busy)
                throw new SpeakerException("the graphics card is busy", busy: true);
            return Task.FromResult(new SpokenAudio(48000, new MemoryStream([1, 0, 2, 0])));
        }

        public Task<SpeakerStatus> WakeAsync(CancellationToken cancellationToken) =>
            Busy ? throw new SpeakerException("the graphics card is busy", busy: true) : Task.FromResult(new SpeakerStatus(true, null, null, null, "waking"));

        public Task<SpeakerStatus> StatusAsync(CancellationToken cancellationToken) => Task.FromResult(new SpeakerStatus(true, "voxcpm", "shiori", null, "asleep"));
    }

    [Fact]
    public async Task Her_voice_can_arrive_while_it_is_made()
    {
        var voice = new Voice();
        var client = _app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ISpeaker>(voice))).CreateClient();

        var response = await client.PostAsJsonAsync("/api/voice/speak/stream", new { text = "Ciao!", language = "ja" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/pcm", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("48000", response.Headers.GetValues("X-Sample-Rate").Single());
        Assert.Equal([1, 0, 2, 0], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("ja", voice.Language);
    }

    [Fact]
    public async Task With_the_graphics_card_full_she_says_her_voice_is_resting()
    {
        var voice = new Voice { Busy = true };
        var client = _app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ISpeaker>(voice))).CreateClient();

        var spoken = await client.PostAsJsonAsync("/api/voice/speak/stream", new { text = "Ciao!", language = "it" });
        var woken = await client.PostAsync("/api/voice/speak/wake", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, spoken.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, woken.StatusCode);
        var problem = await spoken.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(problem.GetProperty("busy").GetBoolean());
        Assert.Equal("Her voice is resting", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Her_words_come_back_as_her_voice_in_one_of_her_languages()
    {
        var voice = new Voice();
        var client = _app.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ISpeaker>(voice))).CreateClient();

        var response = await client.PostAsJsonAsync("/api/voice/speak", new { text = "Ciao!", language = "xx" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/wav", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal([82, 73, 70, 70], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("it", voice.Language);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/voice/speak", new { text = " " })).StatusCode);
    }

    [Fact]
    public async Task Without_her_voice_running_the_page_is_told_how_to_start_it()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/api/voice/speak", new { text = "Ciao", language = "it" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_recording_is_refused()
    {
        using var body = new ByteArrayContent([]);
        var response = await Client().PostAsync("/api/voice/hear", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Without_its_ears_running_the_page_is_told_how_to_start_them()
    {
        using var body = new ByteArrayContent([1, 2, 3]);
        var response = await _app.CreateClient().PostAsync("/api/voice/hear", body);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("listen-server.py", await response.Content.ReadAsStringAsync());
    }

    public void Dispose() => _app.Dispose();
}
