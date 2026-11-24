using System.Globalization;
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
        [property: JsonPropertyName("voice")] string? Voice,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("problem")] string? Problem);

    private sealed record Problem(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("busy")] bool Busy);

    private sealed record Woken([property: JsonPropertyName("state")] string? State);

    public async Task<byte[]> SpeakAsync(string text, string language, CancellationToken cancellationToken)
    {
        using var response = await AskAsync(new { text, language }, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<SpokenAudio> StreamAsync(string text, string language, CancellationToken cancellationToken)
    {
        var response = await AskAsync(new { text, language, stream = true }, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        try
        {
            var rate = response.Headers.TryGetValues("X-Sample-Rate", out var values)
                && int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : 24000;
            var audio = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new SpokenAudio(rate, audio, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<SpeakerStatus> WakeAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            response = await http.PostAsync(new Uri(options.CurrentValue.SpeakUrl, "wake"), content, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new SpeakerException(NotRunning());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await FailureAsync(response, cancellationToken).ConfigureAwait(false);
            var woken = await response.Content.ReadFromJsonAsync<Woken>(cancellationToken).ConfigureAwait(false);
            return new SpeakerStatus(true, null, null, null, woken?.State);
        }
    }

    private sealed record Slept([property: JsonPropertyName("state")] string? State);

    public async Task<bool> SleepAsync(TimeSpan quietFor, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent($"{{\"quietFor\":{(int)quietFor.TotalSeconds}}}", System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(options.CurrentValue.SpeakUrl, "sleep"), content, cancellationToken).ConfigureAwait(false);
            var slept = await response.Content.ReadFromJsonAsync<Slept>(cancellationToken).ConfigureAwait(false);
            return slept?.State is not ("awake" or "waking");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return true; // not running: nothing of hers on the card
        }
    }

    public async Task<SpeakerStatus> StatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = await http.GetFromJsonAsync<Health>(new Uri(options.CurrentValue.SpeakUrl, "health"), cancellationToken).ConfigureAwait(false);
            return new SpeakerStatus(true, health?.Engine, health?.Voice, health?.Problem, health?.State);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new SpeakerStatus(false, null, null, NotRunning());
        }
    }

    private async Task<HttpResponseMessage> AskAsync(object body, HttpCompletionOption completion, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            // With its length: the Python server reads Content-Length, not chunked bodies (JsonContent's).
            using var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.CurrentValue.SpeakUrl, "speak")) { Content = content };
            response = await http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new SpeakerException(NotRunning());
        }

        if (response.IsSuccessStatusCode)
            return response;
        using (response)
            throw await FailureAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SpeakerException> FailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
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

        return new SpeakerException(problem?.Error ?? $"Her voice answered {(int)response.StatusCode}.", problem?.Busy ?? false);
    }

    private string NotRunning() =>
        $"Shiori's voice is not running at {options.CurrentValue.SpeakUrl}. Start tools/voice/speak-server.py (see its README).";
}
