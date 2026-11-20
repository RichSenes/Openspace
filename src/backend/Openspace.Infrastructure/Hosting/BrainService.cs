using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Brain;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// On start, creates what is missing of the vault, reads every note into the index, then keeps the
/// index current while notes change on disk. A vault that cannot be made (no git, no rights on the
/// folder) is told in the log and leaves the rest of Openspace running.
/// </summary>
internal sealed partial class BrainService(
    IVault vault, IBrainIndex index, IOptions<BrainOptions> options, ILogger<BrainService> logger) : BackgroundService
{
    private VaultWatcher? _watcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Not cut short by a stop: it takes a moment, and a vault left half made is worse than waiting.
            await vault.EnsureAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNoVault(logger, vault.Folder, ex);
        }

        if (!Directory.Exists(vault.Folder))
        {
            return;
        }

        // Watching starts first, so an edit made while the rebuild reads is not missed.
        _watcher = new VaultWatcher(vault.Folder, options.Value.WatchDelay, paths => paths is null
            ? index.RebuildAsync(stoppingToken)
            : index.RefreshAsync(paths, stoppingToken), logger);
        try
        {
            await index.RebuildAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNoIndex(logger, ex);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The brain's vault could not be prepared in {Folder}")]
    private static partial void LogNoVault(ILogger logger, string folder, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The brain's notes could not be indexed")]
    private static partial void LogNoIndex(ILogger logger, Exception exception);
}
