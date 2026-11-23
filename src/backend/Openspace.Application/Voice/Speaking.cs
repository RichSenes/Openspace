namespace Openspace.Application.Voice;

/// <summary>Turns Shiori's words into her voice, on this machine (tools/voice/speak-server.py).</summary>
public interface ISpeaker
{
    /// <summary>The words as WAV audio, in the language given (it, en or ja).</summary>
    Task<byte[]> SpeakAsync(string text, string language, CancellationToken cancellationToken);

    /// <summary>Whether the voice answers, and with which engine and voice.</summary>
    Task<SpeakerStatus> StatusAsync(CancellationToken cancellationToken);
}

public sealed record SpeakerStatus(bool Ready, string? Engine, string? Voice, string? Problem);

/// <summary>The voice could not speak: not running, or it failed on the words.</summary>
public sealed class SpeakerException(string message) : Exception(message);
