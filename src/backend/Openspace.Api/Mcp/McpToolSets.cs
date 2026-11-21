using ModelContextProtocol.Server;
using Openspace.Application.Shiori;

namespace Openspace.Api.Mcp;

/// <summary>
/// One MCP server, two sets of tools: a request carrying a Shiori session's secret sees Shiori's
/// tools only, any other request sees the desks' tools only. Desks never learn Shiori's tools exist,
/// and Shiori never gets tools that act on a task.
/// </summary>
internal static class McpToolSets
{
    /// <summary>Called for every request, as the server is stateless: each request gets its own options.</summary>
    public static Task ConfigureAsync(HttpContext http, McpServerOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ToolCollection is not { } all)
        {
            return Task.CompletedTask;
        }

        var shiori = !string.IsNullOrWhiteSpace(http.Request.Headers[ShioriTools.Header].ToString());
        var kept = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (var tool in all.Where(t => ShioriTools.All.Contains(t.ProtocolTool.Name) == shiori))
        {
            kept.Add(tool);
        }

        options.ToolCollection = kept;
        return Task.CompletedTask;
    }
}
