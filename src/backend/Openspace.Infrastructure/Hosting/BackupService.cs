using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure.Hosting;

/// <summary>Takes a copy of the database when the last one is older than the chosen interval.</summary>
internal sealed partial class BackupService(
    IDatabaseBackups backups, IOptions<BackupOptions> options, TimeProvider clock, ILogger<BackupService> logger) : BackgroundService
{
    private static readonly TimeSpan Look = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (backups.Folder is null)
        {
            return;
        }

        // Let the start-up migrations finish first.
        await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var copies = await backups.ListAsync(stoppingToken).ConfigureAwait(false);
                var latest = copies.Count > 0 ? copies[0] : null;
                if (latest is null || clock.GetUtcNow() - latest.CreatedAt >= TimeSpan.FromHours(Math.Max(1, options.Value.EveryHours)))
                {
                    var copy = await backups.BackUpAsync(stoppingToken).ConfigureAwait(false);
                    LogBackedUp(logger, copy.Path);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }

            await Task.Delay(Look, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database backed up to {Path}")]
    private static partial void LogBackedUp(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The database could not be backed up")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
