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
