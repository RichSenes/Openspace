using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Guests;

namespace Openspace.Infrastructure.Hosting;

/// <summary>Looks for guests every few seconds.</summary>
internal sealed partial class GuestService(IServiceScopeFactory scopes, IOptions<GuestOptions> options, TimeProvider clock, ILogger<GuestService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        // Let the start-up migrations finish first.
        await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GuestWatcher>().ScanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            await Task.Delay(options.Value.Every, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not look for guests")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
