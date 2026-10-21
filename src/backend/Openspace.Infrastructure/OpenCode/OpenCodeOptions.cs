namespace Openspace.Infrastructure.OpenCode;

public sealed class OpenCodeOptions
{
    public const string SectionName = "OpenCode";

    /// <summary>Where <c>opencode serve</c> listens.</summary>
    public Uri BaseUrl { get; set; } = new("http://127.0.0.1:4096");

    /// <summary>Where OpenCode reaches the Openspace tools (this API's MCP endpoint).</summary>
    public Uri ToolsUrl { get; set; } = new("http://127.0.0.1:5145/mcp");

    /// <summary>Model used for drafts and supervisor plans; null means OpenCode's default.</summary>
    public string? DraftModel { get; set; }

    /// <summary>Follow the server's event stream. Off in tests.</summary>
    public bool ListenToEvents { get; set; } = true;
}
