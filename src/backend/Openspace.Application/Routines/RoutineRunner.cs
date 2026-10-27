using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>
/// Routines a person asked to run now, waiting for the runner's next round; and the run
/// going on, which a person may stop.
/// </summary>
public sealed class RoutineRequests
{
    private readonly ConcurrentQueue<RoutineId> _waiting = new();
    private readonly Lock _gate = new();
    private (RoutineId Id, CancellationTokenSource Stop)? _running;

    public void Ask(RoutineId id) => _waiting.Enqueue(id);

    public bool TryTake(out RoutineId id) => _waiting.TryDequeue(out id);

    /// <summary>A run of this routine starts: it stops when <see cref="Stop"/> is asked or the app stops.</summary>
    internal CancellationTokenSource Starting(RoutineId id, CancellationToken stopping)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        lock (_gate)
        {
            _running = (id, source);
        }

        return source;
    }

    internal void Ended(RoutineId id)
    {
        lock (_gate)
        {
            if (_running?.Id == id)
            {
                _running = null;
            }
        }
    }

    /// <summary>Stops the routine's run if it is the one going on. True when it was.</summary>
    public bool Stop(RoutineId id)
    {
        lock (_gate)
        {
            if (_running is not { } running || running.Id != id)
            {
                return false;
            }

            running.Stop.Cancel();
            return true;
        }
    }
}

/// <summary>
/// Runs routines, one at a time: those asked for by hand first, then those whose time has
/// come. A run that throws fails with the reason; the routine runs again at its next time.
/// </summary>
/// <remarks>Not thread-safe: call it from a single place (its background service).</remarks>
public sealed partial class RoutineRunner(
    IRoutineRepository routines,
    IOfficeRepository offices,
    IRoutineProgress progress,
    IEnumerable<IRoutineKind> kinds,
    RoutineRequests requests,
    IRoutineOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RoutineRunner> logger)
{
    /// <summary>
    /// After a restart nothing follows the runs that were going on: they failed. Offices
    /// with routines get their routines room if they had none yet.
    /// </summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        foreach (var officeId in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await offices.FindAsync(officeId, cancellationToken).ConfigureAwait(false) is { } office)
            {
                office.RoutinesRoom(RoutineCommands.RoomName, clock.GetUtcNow());
            }
        }

        var unfinished = await routines.ListUnfinishedRunsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var run in unfinished)
        {
            run.Fail("Openspace stopped while this run was going on.", clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

        var context = new RoutineContext(routine, run, routines, outbox, clock, kind, progress);
        progress.Report(routine, "Starting");
        using var stop = requests.Starting(routine.Id, cancellationToken);
        try
        {
            var summary = await kind.RunAsync(context, stop.Token).ConfigureAwait(false);
            run.Succeed(string.IsNullOrWhiteSpace(summary) ? "Done." : summary, clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            run.Fail("Stopped by a person.", clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogRunFailed(logger, ex, routine.Name);
            run.Fail(ex.Message, clock.GetUtcNow());
        }
        finally
        {
            requests.Ended(routine.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(routine, string.Empty);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The routine {Routine} failed")]
    private static partial void LogRunFailed(ILogger logger, Exception exception, string routine);
}
