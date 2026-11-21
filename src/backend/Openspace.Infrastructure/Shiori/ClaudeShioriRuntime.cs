using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application;
using Openspace.Application.Brain;
using Openspace.Application.Functions;
using Openspace.Application.Shiori;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.Shiori;

/// <summary>
/// Shiori's conversations on Claude Code, signed in with the person's own account: one
/// <c>claude -p</c> per answer, resuming the session's conversation by the id Openspace chose for
/// it, so a restart loses nothing. Shiori has none of Claude Code's own tools (no files, no
/// commands, no web): only Openspace's tools for its session, those its mode allows, reached with
/// the session's secret. Nobody's settings apply to it.
/// </summary>
internal sealed partial class ClaudeShioriRuntime(
    IOptionsMonitor<ClaudeCodeOptions> options,
    IOptionsMonitor<OpenCodeOptions> openCode,
    ClaudeCodeRuntime claude,
    IVault vault,
    ILogger<ClaudeShioriRuntime> logger) : IShioriRuntime, IDisposable
{
    /// <summary>The prefix Claude Code gives the tools of the server named "openspace".</summary>
    public const string ToolPrefix = "mcp__openspace__";

    private readonly ConcurrentDictionary<string, Process> _running = new();

    public async Task<string> AnswerAsync(ShioriTurn turn, Func<string, Task> onText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(onText);
        if (turn.Engine != ModelEngine.ClaudeCode)
        {
            throw new RejectedException("shiori-engine", "Shiori talks through Claude Code for now: choose Claude Code for Shiori in Settings › Models.");
        }

        if (!await claude.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new RejectedException("shiori-engine", "Claude Code is not available on this machine, so Shiori cannot answer.");
        }

        using var process = Start(vault.Folder, Arguments(turn, openCode.CurrentValue.ToolsUrl), turn.Text);
        _running[turn.RuntimeSessionId] = process;
        var stream = new ShioriStream();
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
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                IReadOnlyList<string> pieces;
                try
                {
                    pieces = stream.Read(line);
                }
                catch (JsonException ex)
                {
                    LogUnreadable(logger, ex.Message);
                    continue;
                }

                foreach (var piece in pieces)
                {
                    await onText(piece).ConfigureAwait(false);
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        finally
        {
            _running.TryRemove(turn.RuntimeSessionId, out _);
        }

        if (stream.Error is { } error)
        {
            throw new InvalidOperationException(error);
        }

        if (!stream.Ended)
        {
            var detail = errors.ToString().Trim();
            throw new InvalidOperationException($"Claude Code stopped without answering{(detail.Length > 0 ? $": {detail[..Math.Min(300, detail.Length)]}" : ".")}");
        }

        return stream.Answer.Length > 0 ? stream.Answer : stream.Result ?? string.Empty;
    }

    public Task StopAsync(string runtimeSessionId)
    {
        if (_running.TryGetValue(runtimeSessionId, out var process))
        {
            Kill(process);
        }

        return Task.CompletedTask;
    }

    /// <summary>The command line of one answer: no tools of Claude Code's own, only the mode's tools of Openspace.</summary>
    internal static List<string> Arguments(ShioriTurn turn, Uri toolsUrl)
    {
        var mcp = JsonSerializer.Serialize(new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["openspace"] = new
                {
                    type = "http",
                    url = toolsUrl.ToString(),
                    headers = new Dictionary<string, string> { [ShioriTools.Header] = turn.ToolsToken },
                },
            },
        });
        var args = new List<string>
        {
            "-p", "--output-format", "stream-json", "--verbose", "--include-partial-messages", "--restricted", "--permission-prompts", "none",
            "--strict-mcp-config", "--mcp-config", mcp, "--tools", string.Empty,
        };
        if (turn.Tools.Count > 0)
        {
            args.Add("--allowedTools");
            args.AddRange(turn.Tools.Select(t => ToolPrefix + t));
        }

        if (ClaudeCodeOptions.ModelOrDefault(turn.Model) is { } model)
        {
            args.AddRange(["--model", model]);
        }

        args.AddRange(["--append-system-prompt", turn.Instructions]);
        args.AddRange([turn.Resume ? "--resume" : "--session-id", turn.RuntimeSessionId]);
        return args;
    }

    private Process Start(string folder, IEnumerable<string> args, string input)
    {
        var info = new ProcessStartInfo(options.CurrentValue.Command)
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

        var process = Process.Start(info) ?? throw new InvalidOperationException("Claude Code could not be started.");
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        return process;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // It ended on its own meanwhile.
        }
    }

    /// <summary>Openspace is closing: answers being written stop; the session resumes with the next message.</summary>
    public void Dispose()
    {
        foreach (var process in _running.Values)
        {
            Kill(process);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped an unreadable line of Shiori's answer: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);
}
