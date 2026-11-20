namespace Openspace.Application.Brain;

/// <summary>Where Shiori's vault lives and how quickly edits made outside Openspace are seen.</summary>
public sealed class BrainOptions
{
    public const string SectionName = "Brain";

    /// <summary>The vault's folder; <c>~</c> stands for the person's home.</summary>
    public string Folder { get; set; } = "~/.local/share/openspace/brain";

    /// <summary>How long the watcher waits for edits to settle before reading them again.</summary>
    public TimeSpan WatchDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
