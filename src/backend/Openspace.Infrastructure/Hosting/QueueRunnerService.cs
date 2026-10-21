using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Agents;
using Openspace.Application.Queue;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// The only caller of the queue: recovers interrupted tasks at startup, then keeps
/// filling free seats every few seconds.
/// </summary>
internal sealed partial class QueueRunnerService(
    IServiceScopeFactory scopes,
    IOptions<QueueOptions> options,
    ILogger<QueueRunnerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InScopeAsync((runner, ct) => runner.RecoverAsync(ct), stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(options.Value.PollInterval);
        do
        {
            await InScopeAsync((runner, ct) => runner.RunQueueAsync(ct), stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task InScopeAsync(Func<TaskRunner, CancellationToken, Task> work, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<TaskRunner>(), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogQueueFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The queue runner failed; it will try again")]
    private static partial void LogQueueFailed(ILogger logger, Exception exception);
}
