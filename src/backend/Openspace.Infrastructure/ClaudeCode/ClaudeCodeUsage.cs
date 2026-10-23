using System.Text.Json;
using Openspace.Application.Abstractions;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>
/// How much of the Claude account's limits is used, as Claude Code last reported it: the
/// five-hour window and the weekly one, shared with everything else done with that account.
/// </summary>
internal sealed class ClaudeCodeUsage(TimeProvider clock)
{
    private ClaudeUsageView? _latest;

    public ClaudeUsageView? Latest => Volatile.Read(ref _latest);

    public void Update(JsonElement rateLimitEvent)
    {
        if (!rateLimitEvent.TryGetProperty("rate_limit_info", out var info) || !info.TryGetProperty("unifiedWindows", out var windows))
        {
            return;
        }

        static UsageWindow? Window(JsonElement windows, string name) =>
            windows.TryGetProperty(name, out var w) && w.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
                ? new UsageWindow(
                    u.GetDouble(),
                    w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null)
                : null;

        var status = info.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "allowed";
        Volatile.Write(ref _latest, new ClaudeUsageView(status, Window(windows, "five_hour"), Window(windows, "seven_day"), clock.GetUtcNow()));
    }
}
