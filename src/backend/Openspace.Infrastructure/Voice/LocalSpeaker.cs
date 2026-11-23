using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Openspace.Application.Voice;

namespace Openspace.Infrastructure.Voice;

/// <summary>Asks the local speech server for Shiori's voice saying some words.</summary>
public sealed class LocalSpeaker(HttpClient http, IOptionsMonitor<VoiceOptions> options) : ISpeaker
{
    private sealed record Health(
        [property: JsonPropertyName("engine")] string? Engine,
        [property: JsonPropertyName("voice")] string? Voice);

    private sealed record Problem([property: JsonPropertyName("error")] string? Error);

    public async Task<byte[]> SpeakAsync(string text, string language, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(new Uri(options.CurrentValue.SpeakUrl, "speak"), new { text, language }, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new SpeakerException(NotRunning());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Problem? problem = null;
                try
                {
                    problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken).ConfigureAwait(false);
                }
                catch (System.Text.Json.JsonException)
                {
                    // Not JSON: the status says enough.
                }

                throw new SpeakerException(problem?.Error ?? $"Her voice answered {(int)response.StatusCode}.");
            }

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<SpeakerStatus> StatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = await http.GetFromJsonAsync<Health>(new Uri(options.CurrentValue.SpeakUrl, "health"), cancellationToken).ConfigureAwait(false);
            return new SpeakerStatus(true, health?.Engine, health?.Voice, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new SpeakerStatus(false, null, null, NotRunning());
        }
    }

    private string NotRunning() =>
        $"Shiori's voice is not running at {options.CurrentValue.SpeakUrl}. Start tools/voice/speak-server.py (see its README).";
}
