using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Releases;

namespace Openspace.Infrastructure.Hosting;

/// <summary>Looks for a new release of Openspace now and then. It never installs one.</summary>
internal sealed partial class ReleaseService(ReleaseWatch watch, IOptionsMonitor<ReleaseOptions> options, TimeProvider clock, ILogger<ReleaseService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not while everything else is starting.
        await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                try
                {
                    await watch.CheckAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogFailed(logger, ex.Message);
                }
            }

            await Task.Delay(options.CurrentValue.Every > TimeSpan.FromMinutes(10) ? options.CurrentValue.Every : TimeSpan.FromMinutes(10), clock, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Could not look for a new release: {Reason}")]
    private static partial void LogFailed(ILogger logger, string reason);
}
