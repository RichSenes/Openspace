namespace Openspace.Infrastructure.OpenCode;

public sealed class OpenCodeOptions
{
    public const string SectionName = "OpenCode";

    /// <summary>Where <c>opencode serve</c> listens.</summary>
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:4096");

    /// <summary>Follow the server's event stream. Off in tests.</summary>
    public bool ListenToEvents { get; set; } = true;
}
