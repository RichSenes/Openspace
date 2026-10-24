using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;
using Openspace.Infrastructure.ClaudeCode;

namespace Openspace.Infrastructure.OpenCode;

/// <summary>
/// Asks OpenCode, once a minute, how the sessions of running tasks are doing. Events tell
/// about changes only: a session that was already retrying when Openspace started (a
/// provider over its limits, say) sends none, and would look busy for good.
/// </summary>
internal sealed partial class OpenCodeStatusSweep(
    IHttpClientFactory httpClients,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<OpenCodeStatusSweep> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Every, clock, stoppingToken).ConfigureAwait(false);
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogSweepFailed(logger, ex.Message);
            }
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        IReadOnlyList<WorkTask> running;
        using (var scope = scopes.CreateScope())
        {
            running = await scope.ServiceProvider.GetRequiredService<IWorkTaskRepository>()
                .ListAsync(null, WorkTaskStatus.Running, stoppingToken).ConfigureAwait(false);
        }

        using var http = httpClients.CreateClient(OpenCodeEventListener.HttpClientName);
        foreach (var run in running.Select(t => t.Run).OfType<AgentRun>().Where(r => !ClaudeCodeRuntime.Owns(r.SessionId)))
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            limit.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await http
                .GetAsync($"session/status?directory={Uri.EscapeDataString(run.WorkspacePath)}", limit.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false));
            if (OpenCodeEventTranslator.FromStatusOf(json.RootElement, run.SessionId) is { } signal)
            {
                await AgentSignalDispatch.DispatchAsync(scopes, signal, logger, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read OpenCode session statuses: {Reason}")]
    private static partial void LogSweepFailed(ILogger logger, string reason);
}
