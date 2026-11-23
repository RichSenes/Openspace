using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Openspace.Application.Voice;

namespace Openspace.Infrastructure.Voice;

/// <summary>Asks the local listening server (faster-whisper) to transcribe what the person said.</summary>
public sealed class WhisperListener(HttpClient http, IOptionsMonitor<VoiceOptions> options) : IListener
{
    private sealed record Reply(
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("seconds")] double Seconds,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("called")] bool Called);

    private sealed record Health(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("device")] string? Device);

    public async Task<Heard> HearAsync(Stream audio, string? language, bool wake, CancellationToken cancellationToken)
    {
        var query = string.Join('&', new[] { language is null ? null : $"language={language}", wake ? "wake=1" : null }.OfType<string>());
        var url = new Uri(options.CurrentValue.ListenUrl, query.Length == 0 ? "transcribe" : $"transcribe?{query}");
        // Read it whole first: the listening server wants to know the length before it reads.
        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        using var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new("application/octet-stream");
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new ListenerException(NotRunning());
        }
        using (response)
        {
            var reply = await response.Content.ReadFromJsonAsync<Reply>(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || reply is null)
                throw new ListenerException(reply?.Error ?? $"The listening server answered {(int)response.StatusCode}.");
            return new Heard(reply.Text?.Trim() ?? string.Empty, reply.Language ?? string.Empty, reply.Seconds, reply.Called);
        }
    }

    public async Task<ListenerStatus> StatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = await http.GetFromJsonAsync<Health>(new Uri(options.CurrentValue.ListenUrl, "health"), cancellationToken).ConfigureAwait(false);
            return new ListenerStatus(true, health?.Model, health?.Device, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new ListenerStatus(false, null, null, NotRunning());
        }
    }

    private string NotRunning() =>
        $"Shiori's ears are not running at {options.CurrentValue.ListenUrl}. Start tools/voice/listen-server.py (see its README).";
}
