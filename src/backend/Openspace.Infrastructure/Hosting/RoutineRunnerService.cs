using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Openspace.Application.Routines;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Runs routines: fails the runs a restart interrupted, then every few seconds runs those
/// asked for by hand and those whose time has come.
/// </summary>
internal sealed partial class RoutineRunnerService(IServiceScopeFactory scopes, ILogger<RoutineRunnerService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InScopeAsync((runner, ct) => runner.RecoverAsync(ct), stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await InScopeAsync((runner, ct) => runner.RunWaitingAsync(ct), stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task InScopeAsync(Func<RoutineRunner, CancellationToken, Task> work, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<RoutineRunner>(), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRunnerFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The routine runner failed; it will try again")]
    private static partial void LogRunnerFailed(ILogger logger, Exception exception);
}
