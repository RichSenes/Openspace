using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Infrastructure.Brain;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Lets Shiori read proposals as soon as they land in the vault (an agent's tool, a note saved in
/// Obsidian) and, in case the watcher missed one, every few minutes. It keeps its own watcher, so
/// the index's stays as it is; the notes Shiori writes wake it once more and find nothing to do.
/// </summary>
internal sealed partial class AcceptorService(
    IVault vault, DecisionAcceptor acceptor, IOptions<BrainOptions> options, TimeProvider clock, ILogger<AcceptorService> logger) : BackgroundService
{
    /// <summary>How often it looks even when nothing was seen changing.</summary>
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(5);

    private VaultWatcher? _watcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The brain's own service makes the vault on start: wait for it rather than make it twice.
        while (!Directory.Exists(vault.Folder))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken).ConfigureAwait(false);
        }

        _watcher = new VaultWatcher(vault.Folder, options.Value.WatchDelay, _ => PassAsync(stoppingToken), logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            await PassAsync(stoppingToken).ConfigureAwait(false);
            await Task.Delay(Every, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        try
        {
            var accepted = await acceptor.RunAsync(cancellationToken).ConfigureAwait(false);
            if (accepted.Count > 0)
            {
                LogAccepted(logger, accepted.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shiori accepted {Count} proposals")]
    private static partial void LogAccepted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shiori could not read the proposals")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
