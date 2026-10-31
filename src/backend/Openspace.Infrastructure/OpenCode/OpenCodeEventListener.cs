using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Openspace.Application.Agents;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>
/// Follows the OpenCode server's global event stream (server-sent events) and hands
/// each meaningful event to the application. Reconnects on its own.
/// </summary>
internal sealed partial class OpenCodeEventListener(
    IHttpClientFactory httpClients,
    IServiceScopeFactory scopes,
    ILogger<OpenCodeEventListener> logger) : BackgroundService
{
    public const string HttpClientName = "opencode-events";

    /// <summary>Answers whose tokens were counted: OpenCode may report a finished answer more than once.</summary>
    private readonly HashSet<string> _counted = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FollowAsync(stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogDisconnected(logger, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
            }
        }
    }

    private async Task FollowAsync(CancellationToken stoppingToken)
    {
        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http
            .GetAsync("global/event", HttpCompletionOption.ResponseHeadersRead, stoppingToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        LogConnected(logger);

        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(stoppingToken).ConfigureAwait(false));
        while (await reader.ReadLineAsync(stoppingToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            AgentSignal? signal;
            AgentSignal? delegation;
            try
            {
                using var json = JsonDocument.Parse(line.AsMemory(5));
                var payload = json.RootElement.TryGetProperty("payload", out var p) ? p : json.RootElement;
                signal = OpenCodeEventTranslator.Translate(payload);
                delegation = OpenCodeEventTranslator.Delegation(payload);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                LogUnreadable(logger, ex.Message);
                continue;
            }

            if (signal is AgentSignal.Used { MessageId: { } answer } && !Count(answer))
            {
                continue;
            }

            if (signal is not null)
            {
                await DispatchAsync(signal, stoppingToken).ConfigureAwait(false);
            }

            // A hand-off to a helper is told apart from the progress of the same event.
            if (delegation is not null)
            {
                await DispatchAsync(delegation, stoppingToken).ConfigureAwait(false);
            }

            // A finished answer also says how full the conversation is: all it read, plus what it wrote.
            if (signal is AgentSignal.Used { MessageId: not null } used)
            {
                await DispatchAsync(new AgentSignal.Context(used.SessionId, used.Tokens.Total, null, used.Model), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private bool Count(string answer)
    {
        if (_counted.Count > 10_000)
        {
            _counted.Clear();
        }

        return _counted.Add(answer);
    }

    private Task DispatchAsync(AgentSignal signal, CancellationToken cancellationToken) =>
        AgentSignalDispatch.DispatchAsync(scopes, signal, logger, cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Following OpenCode events")]
    private static partial void LogConnected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the OpenCode event stream ({Reason}); retrying in {Seconds}s")]
    private static partial void LogDisconnected(ILogger logger, string reason, double seconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipped an unreadable OpenCode event: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);
}
