using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Queue;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Every few seconds reads who is changing which files and keeps <c>Now.md</c> current. Runs
/// with the queue: without running tasks there is nothing to read.
/// </summary>
internal sealed partial class NowService(
    IServiceScopeFactory scopes,
    IOptions<QueueOptions> options,
    ILogger<NowService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.NowInterval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NowKeeper>().RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogNowFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading who is changing what failed; it will try again")]
    private static partial void LogNowFailed(ILogger logger, Exception exception);
}
