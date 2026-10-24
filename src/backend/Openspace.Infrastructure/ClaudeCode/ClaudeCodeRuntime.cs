using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Roles;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.ClaudeCode;

/// <summary>
/// Runs agents with Claude Code on this machine, signed in with the person's own account.
/// Each prompt starts <c>claude -p</c> in the task's working copy and follows its stream;
/// later prompts resume the same conversation. Claude Code is restricted to the role's
/// tools and asks Openspace, through its tools, before anything the role does not allow
/// outright. Personal and project settings never apply to it.
/// </summary>
internal sealed partial class ClaudeCodeRuntime(
    IOptions<ClaudeCodeOptions> options,
    IOptions<OpenCodeOptions> openCode,
    PermissionRequests permissions,
    ClaudeCodeUsage usage,
    IServiceScopeFactory scopes,
    ILogger<ClaudeCodeRuntime> logger,
    Func<AgentSignal, Task>? dispatch = null) : IDisposable
{
    /// <summary>Where signals go: the application, or a test.</summary>
    private readonly Func<AgentSignal, Task> _dispatch = dispatch ?? (signal => AgentSignalDispatch.DispatchAsync(scopes, signal, logger, CancellationToken.None));

    /// <summary>Session ids of Claude Code start with this, so they are told from OpenCode's.</summary>
    public const string SessionPrefix = "claude-";

    /// <summary>The Openspace tool Claude Code asks before using a tool the role does not allow outright.</summary>
    public const string PermissionTool = "mcp__openspace__permission";

    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _tasks = new();
    private readonly SemaphoreSlim _probe = new(1, 1);
    private (bool Available, DateTimeOffset At)? _available;

    private sealed class Session(string workspacePath, bool started)
    {
        public string WorkspacePath { get; } = workspacePath;

        public bool Started { get; set; } = started;

        public Process? Process { get; set; }

        public Task Following { get; set; } = Task.CompletedTask;

        public bool Aborted { get; set; }

        public string? LastReply { get; set; }
    }

    public static bool Owns(string id) => id.StartsWith(SessionPrefix, StringComparison.Ordinal);

    /// <summary>Whether Claude Code can be started here; asked once a minute at most.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return false;
        }

        await _probe.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_available is { } known && DateTimeOffset.UtcNow - known.At < TimeSpan.FromMinutes(1))
            {
                return known.Available;
            }

            bool available;
            try
            {
                using var process = Start(Path.GetTempPath(), ["--version"], null);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                available = process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                available = false;
            }

            _available = (available, DateTimeOffset.UtcNow);
            return available;
        }
        finally
        {
            _probe.Release();
        }
    }

    public string CreateSession(string workspacePath)
    {
        var id = SessionPrefix + Guid.NewGuid().ToString("D");
        _sessions[id] = new Session(workspacePath, started: false);
        return id;
    }

    /// <summary>The task the Openspace tools are about, for agents in this folder.</summary>
    public void ConnectTools(string workspacePath, Guid taskId) => _tasks[workspacePath] = taskId;

    public async Task PromptAsync(string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken)
    {
        // A session Openspace does not know was started before a restart: it is resumed.
        var session = _sessions.GetOrAdd(sessionId, _ => new Session(workspacePath, started: true));

        // The run that just finished may still be closing: one process per conversation.
        await session.Following.ConfigureAwait(false);

        var args = SessionArguments(sessionId, session.Started, setup, workspacePath);
        session.Started = true;
        session.Aborted = false;
        var process = Start(workspacePath, args, text);
        session.Process = process;
        var reading = Task.Run(() => ReadRunAsync(sessionId, session, process), CancellationToken.None);
        session.Following = reading;
        _ = AnnounceEndAsync(reading);
    }

    /// <summary>
    /// Says how a run ended once it is fully read. Apart from the reading, so whatever the end
    /// sets off (a reminder resuming this same session, say) does not wait for itself.
    /// </summary>
    private async Task AnnounceEndAsync(Task<IReadOnlyList<AgentSignal>> reading)
    {
        foreach (var signal in await reading.ConfigureAwait(false))
        {
            await _dispatch(signal).ConfigureAwait(false);
        }
    }

    /// <summary>The command line of an agent's run: the role's tools, Openspace's tools, and nothing of anyone's settings.</summary>
    internal List<string> SessionArguments(string sessionId, bool resume, AgentSetup setup, string workspacePath)
    {
        var (tools, allowed) = Tools(setup.Permissions);
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--restricted", "--permission-mode", "manual" };
        args.Add("--tools");
        args.AddRange(tools);
        args.Add("--allowedTools");
        args.AddRange(allowed);
        args.AddRange(["--permission-prompt-tool", PermissionTool, "--strict-mcp-config", "--mcp-config", McpConfig(workspacePath)]);
        if (setup.Model is { } model)
        {
            args.AddRange(["--model", model]);
        }

        if (setup.Instructions is { } instructions)
        {
            args.AddRange(["--append-system-prompt", instructions]);
        }

        args.AddRange([resume ? "--resume" : "--session-id", sessionId[SessionPrefix.Length..]]);
        return args;
    }

    /// <summary>
    /// The tools a role gets, and those it uses without asking: reading always; editing,
    /// commands and the web unless denied, without asking only when allowed.
    /// </summary>
    internal static (List<string> Tools, List<string> Allowed) Tools(RolePermissions? permissions)
    {
        var edit = permissions?.Edit ?? PermissionLevel.Allow;
        var bash = permissions?.Bash ?? PermissionLevel.Ask;
        var web = permissions?.Web ?? PermissionLevel.Ask;
        List<string> tools = ["Read", "Grep", "Glob", "TodoWrite"];
        List<string> allowed = ["Read", "Grep", "Glob", "TodoWrite", "mcp__openspace"];
        void Add(PermissionLevel level, params string[] names)
        {
            if (level == PermissionLevel.Deny)
            {
                return;
            }

            tools.AddRange(names);
            if (level == PermissionLevel.Allow)
            {
                allowed.AddRange(names);
            }
        }

        Add(edit, "Edit", "Write", "NotebookEdit");
        Add(bash, "Bash");
        Add(web, "WebFetch", "WebSearch");
        return (tools, allowed);
    }

    private string McpConfig(string workspacePath)
    {
        var headers = _tasks.TryGetValue(workspacePath, out var taskId)
            ? new Dictionary<string, string> { [OpenCodeRuntime.TaskHeader] = taskId.ToString() }
            : [];
        return JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["openspace"] = new { type = "http", url = openCode.Value.ToolsUrl.ToString(), headers },
            },
        });
    }

    /// <summary>Reads a run to its end, passing on its progress; returns how it ended, nothing when aborted.</summary>
    private async Task<IReadOnlyList<AgentSignal>> ReadRunAsync(string sessionId, Session session, Process process)
    {
        var events = new ClaudeCodeEvents(sessionId);
        var ending = new List<AgentSignal>();
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && errors.Length < 4000)
            {
                errors.AppendLine(e.Data);
            }
        };
        process.BeginErrorReadLine();

        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                IReadOnlyList<AgentSignal> signals;
                try
                {
                    signals = events.Read(line, usage);
                }
                catch (JsonException ex)
                {
                    LogUnreadable(logger, ex.Message);
                    continue;
                }

                foreach (var signal in signals)
                {
                    // The end waits for the process to close, so a next prompt resumes cleanly.
                    if (signal is AgentSignal.Finished or AgentSignal.Failed)
                    {
                        ending.Add(signal);
                    }
                    else
                    {
                        await _dispatch(signal).ConfigureAwait(false);
                    }
                }
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            LogStreamLost(logger, ex, sessionId);
        }
        finally
        {
            session.LastReply = events.LastReply ?? session.LastReply;
            session.Process = null;
            process.Dispose();
        }

        if (session.Aborted)
        {
            return [];
        }

        if (ending.Count == 0)
        {
            var detail = errors.ToString().Trim();
            ending.Add(ClaudeCodeEvents.Stopped(
                sessionId, $"Claude Code stopped without finishing{(detail.Length > 0 ? $": {detail[..Math.Min(300, detail.Length)]}" : ".")}", usage));
        }

        return ending;
    }

    public Task AbortAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Aborted = true;
            Kill(session.Process);
        }

        permissions.RefuseAll(sessionId);
        return Task.CompletedTask;
    }

    public string? LastReply(string sessionId) => _sessions.TryGetValue(sessionId, out var session) ? session.LastReply : null;

    /// <summary>
    /// One question, one answer, nothing kept: Claude Code with only the given tools, asking
    /// nobody for anything else. Returns the JSON that matches the schema, or the answer's text.
    /// </summary>
    public async Task<string> AskAsync(
        string folder, string instructions, string text, string? jsonSchema, IReadOnlyList<string> tools, IReadOnlyList<string> allowed, string? model, CancellationToken cancellationToken)
    {
        var args = new List<string> { "-p", "--output-format", "json", "--restricted", "--permission-prompts", "none", "--strict-mcp-config", "--no-session-persistence" };
        args.Add("--tools");
        args.AddRange(tools.Count > 0 ? tools : [string.Empty]);
        if (allowed.Count > 0)
        {
            args.Add("--allowedTools");
            args.AddRange(allowed);
        }

        if (jsonSchema is not null)
        {
            args.AddRange(["--json-schema", jsonSchema]);
        }

        if (model is not null)
        {
            args.AddRange(["--model", model]);
        }

        args.AddRange(["--append-system-prompt", instructions]);

        using var process = Start(folder, args, text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.AskTimeout);
        string output;
        try
        {
            var reading = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            output = await reading.ConfigureAwait(false);
            _ = await errors.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        return Answer(output, jsonSchema is not null);
    }

    /// <summary>
    /// Reads how much of the account's limits is used, with the smallest question there is
    /// to the smallest model: Claude Code reports the limits with every answer. Null when
    /// Claude Code is not there or said nothing about them.
    /// </summary>
    public async Task<ClaudeUsageView?> CheckUsageAsync(CancellationToken cancellationToken)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string[] args =
        [
            "-p", "--output-format", "stream-json", "--verbose", "--restricted", "--permission-prompts", "none",
            "--strict-mcp-config", "--no-session-persistence", "--tools", string.Empty, "--model", "haiku",
        ];
        using var process = Start(Path.GetTempPath(), args, "Reply with the single word OK.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var seen = usage.Latest;
        try
        {
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                try
                {
                    new ClaudeCodeEvents("usage").Read(line, usage);
                }
                catch (JsonException ex)
                {
                    LogUnreadable(logger, ex.Message);
                }
            }

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            _ = await errors.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }

        return ReferenceEquals(usage.Latest, seen) ? null : usage.Latest;
    }

    /// <summary>The answer in Claude Code's JSON output: the structured one when a schema was given.</summary>
    internal static string Answer(string output, bool structured)
    {
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'))
            ?? throw new InvalidOperationException("Claude Code gave no answer.");
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        var result = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        if (root.TryGetProperty("is_error", out var error) && error.ValueKind == JsonValueKind.True)
        {
            throw new InvalidOperationException($"Claude Code stopped: {result ?? "unknown error"}");
        }

        if (!structured)
        {
            return result?.Trim() ?? string.Empty;
        }

        return root.TryGetProperty("structured_output", out var answer) && answer.ValueKind == JsonValueKind.Object
            ? answer.GetRawText()
            : throw new InvalidOperationException("Claude Code did not answer in the expected form.");
    }

    private Process Start(string folder, IEnumerable<string> args, string? input)
    {
        var info = new ProcessStartInfo(options.Value.Command)
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        // A person may take their time to answer a permission: the tool call waits for them.
        info.Environment["MCP_TOOL_TIMEOUT"] = "86400000";
        var process = Process.Start(info) ?? throw new InvalidOperationException("Claude Code could not be started.");
        if (input is not null)
        {
            process.StandardInput.Write(input);
        }

        process.StandardInput.Close();
        return process;
    }

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // It ended on its own meanwhile.
        }
    }

    /// <summary>Openspace is closing: running agents stop, and resume with their task after a restart.</summary>
    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Aborted = true;
            Kill(session.Process);
        }

        _probe.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped an unreadable Claude Code line: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the output of Claude Code session {SessionId}")]
    private static partial void LogStreamLost(ILogger logger, Exception exception, string sessionId);
}
