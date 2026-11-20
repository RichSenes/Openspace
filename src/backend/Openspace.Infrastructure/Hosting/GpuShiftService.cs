using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;
using Openspace.Infrastructure.LocalModels;

namespace Openspace.Infrastructure.Hosting;

/// <summary>
/// Looks at the graphics card every few seconds, and unloads the local model when Openspace
/// stops so the card is never left taken.
/// </summary>
internal sealed partial class GpuShiftService(
    IServiceScopeFactory scopes,
    DeciderClient decider,
    IOptionsMonitor<LocalModelOptions> options,
    ILogger<GpuShiftService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WriteProviderAsync(options.CurrentValue, stoppingToken).ConfigureAwait(false);

        // Models changed from Settings reach OpenCode's provider file too (OpenCode reads it when it starts).
        using var changes = options.OnChange(changed => _ = WriteProviderAsync(changed, stoppingToken));

        using var timer = new PeriodicTimer(Interval);
        do
        {
            await InScopeAsync((shift, ct) => shift.TickAsync(ct), stoppingToken).ConfigureAwait(false);
            await decider.StopIfIdleAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task WriteProviderAsync(LocalModelOptions current, CancellationToken cancellationToken)
    {
        try
        {
            if (await OpenCodeLocalProvider.WriteAsync(current, cancellationToken).ConfigureAwait(false))
            {
                LogProviderWritten(logger, current.OpenCodeConfigPath!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogProviderFailed(logger, ex);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await InScopeAsync((shift, ct) => shift.StopAsync(ct), cancellationToken).ConfigureAwait(false);
        await decider.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InScopeAsync(Func<GpuShift, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<GpuShift>(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogShiftFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wrote the OpenCode provider of the local models to {Path}: restart OpenCode to see them")]
    private static partial void LogProviderWritten(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not write the OpenCode provider of the local models")]
    private static partial void LogProviderFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The graphics card shift failed; it will try again")]
    private static partial void LogShiftFailed(ILogger logger, Exception exception);
}
