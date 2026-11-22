using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Characters;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Writes yesterday's chronicle once a day, after the hour set in <see cref="ChronicleOptions"/>,
/// with the model of "Journals and chronicle" (never a free online one: the chronicle checks).
/// It looks every hour, so a computer that was off at night catches up when it wakes.
/// </summary>
internal sealed partial class ChronicleService(
    IServiceScopeFactory scopes, IOptionsMonitor<ChronicleOptions> options, TimeProvider clock, ILogger<ChronicleService> logger) : BackgroundService
{
    private static readonly TimeSpan Look = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the start-up migrations and the vault come first.
        await Task.Delay(TimeSpan.FromMinutes(2), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var written = await scope.ServiceProvider.GetRequiredService<Chronicle>().KeepYesterdayAsync(options.CurrentValue, stoppingToken).ConfigureAwait(false);
                if (written is not null)
                {
                    LogWritten(logger, written.Path);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            await Task.Delay(Look, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the chronicle {Path}")]
    private static partial void LogWritten(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The chronicle could not be written")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
