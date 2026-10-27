using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>Routines a person asked to run now, waiting for the runner's next round.</summary>
public sealed class RoutineRequests
{
    private readonly ConcurrentQueue<RoutineId> _waiting = new();

    public void Ask(RoutineId id) => _waiting.Enqueue(id);

    public bool TryTake(out RoutineId id) => _waiting.TryDequeue(out id);
}

/// <summary>
/// Runs routines, one at a time: those asked for by hand first, then those whose time has
/// come. A run that throws fails with the reason; the routine runs again at its next time.
/// </summary>
/// <remarks>Not thread-safe: call it from a single place (its background service).</remarks>
public sealed partial class RoutineRunner(
    IRoutineRepository routines,
    IEnumerable<IRoutineKind> kinds,
    RoutineRequests requests,
    IRoutineOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RoutineRunner> logger)
{
    /// <summary>After a restart nothing follows the runs that were going on: they failed.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        var unfinished = await routines.ListUnfinishedRunsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in unfinished)
        {
            run.Fail("Openspace stopped while this run was going on.", clock.GetUtcNow());
        }

        if (unfinished.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RunWaitingAsync(CancellationToken cancellationToken)
    {
        while (requests.TryTake(out var id))
        {
            if (await routines.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } routine)
            {
                await RunAsync(routine, byHand: true, cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var routine in await routines.ListDueAsync(clock.GetUtcNow(), cancellationToken).ConfigureAwait(false))
        {
            await RunAsync(routine, byHand: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(Routine routine, bool byHand, CancellationToken cancellationToken)
    {
        var run = RoutineRun.Start(routine, byHand, clock.GetUtcNow(), clock.LocalTimeZone);
        routines.AddRun(run);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (kinds.FirstOrDefault(k => k.Key == routine.Kind) is not { } kind)
        {
            run.Fail($"Openspace does not know routines of kind {routine.Kind} (any more).", clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var context = new RoutineContext(routine, run, routines, outbox, clock);
        try
        {
            var summary = await kind.RunAsync(context, cancellationToken).ConfigureAwait(false);
            run.Succeed(string.IsNullOrWhiteSpace(summary) ? "Done." : summary, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, ex, routine.Name);
            run.Fail(ex.Message, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The routine {Routine} failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception, string routine);
}
