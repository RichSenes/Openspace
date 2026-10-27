using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;

namespace Openspace.Infrastructure.LocalModels;

/// <summary>Starts the model server as a child process, and stops it with everything it started.</summary>
internal sealed partial class LocalModelServer(
    IHttpClientFactory httpClients, IOptionsMonitor<LocalModelOptions> options, ILogger<LocalModelServer> logger)
    : ILocalModelServer, IDisposable
{
    public const string HttpClientName = "local-model";

    /// <summary>Enough of the server's output to tell why it stopped.</summary>
    private const int KeptLines = 30;

    private readonly Lock _gate = new();
    private readonly Queue<string> _output = new();
    private Process? _process;
    private string? _model;

    public string? Running
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false } ? _model : null;
            }
        }
    }

    public Task StartAsync(LocalModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var start = new ProcessStartInfo(model.Command)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        foreach (var argument in model.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        lock (_gate)
        {
            Kill();
            var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {model.Command}.");
            process.StandardInput.Close();

            // The pipes must not fill up; the last lines say why the server stopped, if it does.
            _output.Clear();
            process.OutputDataReceived += (_, e) => Keep(e.Data);
            process.ErrorDataReceived += (_, e) => Keep(e.Data);
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Exited(process, model.Id);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            _model = model.Id;
        }

        LogStarted(logger, model.Id);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            Kill();
        }

        if (process is not null)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Killed already: the card frees up a moment later.
            }

            process.Dispose();
            LogStopped(logger);
        }
    }

    public async Task<bool> IsAnsweringAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            using var response = await http.GetAsync(new Uri(options.CurrentValue.ServerUrl, "/health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Kill();
        }
    }

    private void Keep(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Enqueue(line);
            if (_output.Count > KeptLines)
            {
                _output.Dequeue();
            }
        }
    }

    private void Exited(Process process, string model)
    {
        lock (_gate)
        {
            if (_process != process)
            {
                // Stopped by Openspace.
                return;
            }
        }

        // The last lines may still be on their way: wait for the pipes to empty.
        process.WaitForExit();
        string tail;
        lock (_output)
        {
            tail = string.Join('\n', _output);
        }

        LogExited(logger, model, process.ExitCode, tail);
    }

    private void Kill()
    {
        if (_process is { HasExited: false } process)
        {
            process.Kill(entireProcessTree: true);
        }

        _process = null;
        _model = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the local model {Model}")]
    private static partial void LogStarted(ILogger logger, string model);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The local model {Model} stopped by itself with code {ExitCode}; its last lines:\n{Output}")]
    private static partial void LogExited(ILogger logger, string model, int exitCode, string output);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped the local model server")]
    private static partial void LogStopped(ILogger logger);
}
