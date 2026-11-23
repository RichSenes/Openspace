namespace Openspace.Application.Voice;

/// <summary>Where Shiori's ears are: a local speech-to-text server (tools/voice/listen-server.py).</summary>
public sealed class VoiceOptions
{
    public const string SectionName = "Voice";

    /// <summary>The local listening server; nothing said ever leaves the machine.</summary>
    public Uri ListenUrl { get; set; } = new("http://127.0.0.1:8178");

    /// <summary>The longest recording accepted, in bytes.</summary>
    public long MaxBytes { get; set; } = 25 * 1024 * 1024;
}

/// <summary>What was heard: the words and the language they were said in.</summary>
public sealed record Heard(string Text, string Language, double Seconds);

/// <summary>Turns what the person said into text, on this machine.</summary>
public interface IListener
{
    /// <summary>Transcribes the audio; null language lets the listener detect it.</summary>
    Task<Heard> HearAsync(Stream audio, string? language, CancellationToken cancellationToken);

    /// <summary>Whether the listener answers, and what it runs on.</summary>
    Task<ListenerStatus> StatusAsync(CancellationToken cancellationToken);
}

public sealed record ListenerStatus(bool Ready, string? Model, string? Device, string? Problem);

/// <summary>The listener could not hear: not running, or the audio could not be read.</summary>
public sealed class ListenerException(string message) : Exception(message);

/// <summary>Shiori speaks three languages; anything else is left to detection.</summary>
public static class Languages
{
    public static readonly IReadOnlySet<string> Spoken = new HashSet<string>(StringComparer.Ordinal) { "it", "en", "ja" };

    public static string? Choose(string? requested) =>
        requested is not null && Spoken.Contains(requested.Trim().ToLowerInvariant()) ? requested.Trim().ToLowerInvariant() : null;
}
