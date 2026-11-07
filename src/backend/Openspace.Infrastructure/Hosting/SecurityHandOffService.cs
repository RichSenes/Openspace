using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Queue;
using Openspace.Application.Security;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Every couple of minutes, hands the findings of finished security audits to their office's
/// supervisor, for the audits started that way. Off with the queue, as in tests.
/// </summary>
internal sealed partial class SecurityHandOffService(
    IServiceScopeFactory scopes, IOptionsMonitor<QueueOptions> queue, TimeProvider clock, ILogger<SecurityHandOffService> logger) : BackgroundService
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
                    using var scope = scopes.CreateScope();
                    var handed = await scope.ServiceProvider.GetRequiredService<SecurityAudits>().HandFinishedAsync(stoppingToken).ConfigureAwait(false);
                    if (handed > 0)
                    {
                        LogHanded(logger, handed);
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
