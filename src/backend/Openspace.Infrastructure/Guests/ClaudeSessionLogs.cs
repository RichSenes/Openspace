using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Guests;
using Openspace.Infrastructure.Git;

namespace Openspace.Infrastructure.Guests;

/// <summary>
/// Reads the logs Claude Code writes for each session, one file a session, from their end:
/// only the folder each line was written in, the branch, the time, how the session was
/// started and the names of the tools it used. What was said is never kept.
/// Sessions Openspace started itself (headless, in its own working copies) are left out.
/// </summary>
internal sealed class ClaudeSessionLogs(IOptions<GuestOptions> options, IOptions<WorkspaceOptions> workspaces, TimeProvider clock) : IClaudeSessions
{
    /// <summary>Tools that only look at things.</summary>
    private static readonly HashSet<string> Looking = new(StringComparer.Ordinal) { "Read", "Grep", "Glob", "LS", "WebFetch", "WebSearch", "NotebookRead" };

    /// <summary>A session that did nothing for this long waits for its person.</summary>
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(3);

    private const int FirstRead = 64 * 1024;
    private const int MostRead = 4 * 1024 * 1024;

    public async Task<IReadOnlyList<SessionTrace>> RecentAsync(TimeSpan within, CancellationToken cancellationToken)
    {
        var folder = string.IsNullOrWhiteSpace(options.Value.Folder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")
            : options.Value.Folder;
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var now = clock.GetUtcNow();
        var traces = new List<SessionTrace>();
        foreach (var file in Directory.EnumerateDirectories(folder).SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl")))
        {
            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
            if (now - written > within)
            {
                continue;
            }

            try
            {
                var tail = await TailAsync(file, cancellationToken).ConfigureAwait(false);
                if (Trace(Path.GetFileNameWithoutExtension(file), tail, written, now, workspaces.Value.Root) is { } trace && now - trace.LastAt <= within)
                {
                    traces.Add(trace);
                }
            }
            catch (IOException)
            {
                // Being written or gone: the next look will see it.
            }
        }

        return traces;
    }

    /// <summary>The last whole lines of a log: read from the end, more when a line is long.</summary>
    private static async Task<IReadOnlyList<string>> TailAsync(string file, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        for (var size = FirstRead; ; size *= 4)
        {
            var take = (int)Math.Min(stream.Length, size);
            stream.Seek(-take, SeekOrigin.End);
            var buffer = new byte[take];
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            var lines = Encoding.UTF8.GetString(buffer).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // The first line may be cut, unless the whole file was read.
            var whole = take == stream.Length ? lines : lines.Skip(1).ToArray();
            if (whole.Length >= 2 || take == stream.Length || size >= MostRead)
            {
                return whole;
            }
        }
    }

    /// <summary>What the end of a session's log says, or null when it is not a guest's.</summary>
    internal static SessionTrace? Trace(string sessionId, IReadOnlyList<string> lines, DateTimeOffset written, DateTimeOffset now, string workspaceRoot)
    {
        string? folder = null, branch = null, entrypoint = null;
        DateTimeOffset? last = null;
        GuestState? state = null;
        for (var i = lines.Count - 1; i >= 0 && (folder is null || state is null || last is null); i--)
        {
            JsonDocument line;
            try
            {
                line = JsonDocument.Parse(lines[i]);
            }
            catch (JsonException)
            {
                continue;
            }

            using (line)
            {
                var root = line.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                last ??= Time(root);
                if (folder is null && Text(root, "cwd") is { } cwd)
                {
                    (folder, branch, entrypoint) = (cwd, Text(root, "gitBranch"), Text(root, "entrypoint"));
                }

                state ??= Text(root, "type") switch
                {
                    "assistant" => StateOf(root),
                    "user" => GuestState.Working,
                    _ => null,
                };
            }
        }

        if (folder is null || (entrypoint is not null && entrypoint.StartsWith("sdk", StringComparison.Ordinal)))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(workspaceRoot) && folder.StartsWith(workspaceRoot.TrimEnd('/', '\\'), StringComparison.Ordinal))
        {
            return null;
        }

        var at = last ?? written;
        var doing = now - at > Quiet ? GuestState.Idle : state ?? GuestState.Idle;
        return new SessionTrace(sessionId, folder.TrimEnd('/', '\\'), string.IsNullOrWhiteSpace(branch) ? null : branch, at, doing);
    }

    /// <summary>A reply that ended its turn waits for the person; one using tools works, or reads.</summary>
    private static GuestState StateOf(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
        {
            return GuestState.Working;
        }

        if (Text(message, "stop_reason") == "end_turn")
        {
            return GuestState.Idle;
        }

        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var tool = content.EnumerateArray().Where(c => Text(c, "type") == "tool_use").Select(c => Text(c, "name")).LastOrDefault();
            if (tool is not null)
            {
                return Looking.Contains(tool) ? GuestState.Reading : GuestState.Working;
            }
        }

        return GuestState.Working;
    }

    private static DateTimeOffset? Time(JsonElement root) =>
        Text(root, "timestamp") is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
