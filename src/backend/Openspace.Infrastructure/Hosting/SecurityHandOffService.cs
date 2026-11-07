using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Queue;
using Openspace.Application.Security;
using Openspace.Application.Work;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Every couple of minutes, hands the findings of finished security audits to their office's
/// supervisor, for the audits started that way. Off with the queue, as in tests.
/// </summary>
internal sealed partial class SecurityHandOffService(
    IServiceScopeFactory scopes, SupervisorJobs jobs, IOptionsMonitor<QueueOptions> queue, TimeProvider clock, ILogger<SecurityHandOffService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (queue.CurrentValue.Enabled)
            {
                try
                {
                    // Each run's hand-off is a supervisor's plan of its own, shown like any other.
                    using var scope = scopes.CreateScope();
                    var due = await scope.ServiceProvider.GetRequiredService<SecurityAudits>().DueHandOffsAsync(stoppingToken).ConfigureAwait(false);
                    foreach (var run in due)
                    {
                        jobs.Start(run.OfficeId, "security", $"Findings of security audit run {run.Run}", SecurityAudits.JobKey(run.OfficeId, run.Run), async (services, token) =>
                        {
                            var handed = await services.GetRequiredService<SecurityAudits>()
                                .HandToSupervisorAsync(new Domain.Offices.OfficeId(run.OfficeId), run.Run, run.RequestedBy, token, run.Language).ConfigureAwait(false);
                            return new SupervisorOutcome(handed.Tasks, handed.EpicId);
                        });
                    }

                    if (due.Count > 0)
                    {
                        LogHanded(logger, due.Count);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(logger, ex.Message);
                }
            }

            await Task.Delay(TimeSpan.FromMinutes(2), clock, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handed the findings of {Runs} finished security audits to their supervisors")]
    private static partial void LogHanded(ILogger logger, int runs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not hand security findings to a supervisor: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);
}
