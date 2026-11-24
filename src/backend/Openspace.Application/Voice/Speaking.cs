namespace Openspace.Application.Voice;

/// <summary>Turns Shiori's words into her voice, on this machine (tools/voice/speak-server.py).</summary>
public interface ISpeaker
{
    /// <summary>The words as WAV audio, in the language given (it, en or ja).</summary>
    Task<byte[]> SpeakAsync(string text, string language, CancellationToken cancellationToken);

    /// <summary>The words as 16-bit mono PCM, a piece at a time while they are made.</summary>
    Task<SpokenAudio> StreamAsync(string text, string language, CancellationToken cancellationToken);

    /// <summary>Loads her voice now, so the first sentence of a conversation does not wait for it.</summary>
    Task<SpeakerStatus> WakeAsync(CancellationToken cancellationToken);

    /// <summary>Whether the voice answers, and with which engine and voice.</summary>
    Task<SpeakerStatus> StatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Whether her voice server answers; <paramref name="State"/> says whether the model is loaded
/// (asleep, waking, awake): it sleeps when quiet, to leave the graphics card to other work.
/// </summary>
public sealed record SpeakerStatus(bool Ready, string? Engine, string? Voice, string? Problem, string? State = null);

/// <summary>Her voice arriving as it is made: raw 16-bit mono PCM at <see cref="SampleRate"/>.</summary>
public sealed class SpokenAudio(int sampleRate, Stream audio, IDisposable? owner = null) : IAsyncDisposable
{
    public int SampleRate { get; } = sampleRate;

    public Stream Audio { get; } = audio;

    public async ValueTask DisposeAsync()
    {
        await Audio.DisposeAsync().ConfigureAwait(false);
        owner?.Dispose();
    }
}

/// <summary>
/// The voice could not speak: not running, or it failed on the words. <see cref="Busy"/> means
/// the graphics card has no room for it right now, so she answers in writing only for a while.
/// </summary>
public sealed class SpeakerException(string message, bool busy = false) : Exception(message)
{
    public bool Busy { get; } = busy;
}
