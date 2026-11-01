using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.LocalModels;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Queue;

/// <summary>
/// Hands free seats to queued tasks, oldest first. A desk works on one task at a
/// time, so a task waits while its desk is busy even if other seats are free.
/// </summary>
/// <remarks>Not thread-safe: call it from a single place (the queue runner).</remarks>
public sealed class QueueDispatcher(
    IWorkTaskRepository tasks,
    IOfficeRepository offices,
    EngineLimits limits,
    GpuArbiter gpu,
    QueueWaits waits,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<QueueOptions> options,
    TimeProvider clock)
{
    /// <returns>The tasks that just started and now need an agent.</returns>
    /// <remarks>Every queued task that does not start gets the reason it waits, for the board.</remarks>
    public async Task<IReadOnlyList<WorkTaskId>> DispatchAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var queued = await tasks.ListAsync(null, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);
        var seats = options.CurrentValue.Seats;
        var freeSeats = seats - running.Count;
        var reasons = new Dictionary<Guid, string>();
        var seatsTaken = $"Waits for a free seat: {seats} {(seats == 1 ? "agent works" : "agents work")} at a time.";
        if (freeSeats <= 0)
        {
            foreach (var task in queued)
            {
                reasons[task.Id.Value] = seatsTaken;
            }

            await waits.ReplaceAsync(reasons, cancellationToken).ConfigureAwait(false);
            return [];
        }

        var busyDesks = running.Select(t => t.DeskId).OfType<DeskId>().ToHashSet();
        var now = clock.GetUtcNow();
        var started = new List<WorkTaskId>();
        var anyPaused = limits.Pauses().Count > 0;
        var officeCache = new Dictionary<OfficeId, Office>();

        foreach (var task in queued)
        {
            if (started.Count == freeSeats)
            {
                reasons[task.Id.Value] = seatsTaken;
                continue;
            }

            if (task.DeskId is not { } desk)
            {
                continue;
            }

            if (busyDesks.Contains(desk))
            {
                reasons[task.Id.Value] = "Its desk is busy with another task.";
                continue;
            }

            // Work that builds on other tasks waits until they are accepted and merged.
            if (await BlockerAsync(task, cancellationToken).ConfigureAwait(false) is { } blocker)
            {
                reasons[task.Id.Value] = $"Waits for \u201c{blocker.Title}\u201d to be accepted and merged.";
                continue;
            }

            // Desks on an account over its limits keep their tasks queued until it is back,
            // unless the office has a reserve model that can go on with them meanwhile.
            if (anyPaused || task.ReserveModel is not null)
            {
                var office = await OfficeOfAsync(task, officeCache, cancellationToken).ConfigureAwait(false);
                if (!Hand(task, office, desk, now))
                {
                    reasons[task.Id.Value] = Paused(office, desk);
                    continue;
                }
            }

            // Desks on a model of this machine wait for it to be loaded on the graphics card.
            if (gpu.Enabled && !Admitted(task, await OfficeOfAsync(task, officeCache, cancellationToken).ConfigureAwait(false), desk))
            {
                reasons[task.Id.Value] = CardTaken(task, await OfficeOfAsync(task, officeCache, cancellationToken).ConfigureAwait(false), desk);
                continue;
            }

            busyDesks.Add(desk);

            task.Start(now);
            started.Add(task.Id);
        }

        if (started.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await waits.ReplaceAsync(reasons, cancellationToken).ConfigureAwait(false);
        return started;
    }

    /// <summary>Why a task whose desk is over its limits found no reserve to go on with.</summary>
    private string Paused(Office office, DeskId desk)
    {
        var scope = EngineLimits.ScopeOf(office.Locate(desk).Desk.Agent);
        var pause = limits.Pauses().FirstOrDefault(p => p.Scope == scope);
        var account = pause?.Label ?? "Its account";
        var until = pause is null ? string.Empty : $" until {pause.Until.ToLocalTime():HH:mm}";
        return office.ReserveModels.Count == 0
            ? $"{account} is over its limits{until}, and the office has no reserve model: it goes on then."
            : $"{account} is over its limits{until}, and no reserve model is free: it goes on then.";
    }

    /// <summary>Why a task on a local model cannot start: the card is lent, the model loads, or another task uses it.</summary>
    private string CardTaken(WorkTask task, Office office, DeskId desk)
    {
        var view = gpu.View();
        var wanted = AgentSetups.AgentFor(office.Locate(desk).Desk, task).Model;
        var name = task.ReserveModel is not null ? $"the reserve {wanted}" : wanted ?? "its local model";
        return view.State switch
        {
            GpuState.Lent or GpuState.Yielding when view.Holders.Count > 0 => $"Waits for the graphics card, which {string.Join(", ", view.Holders)} {(view.Holders.Count == 1 ? "has" : "have")}.",
            GpuState.Loading when view.Model == wanted => $"Waits while {view.ModelName ?? wanted} loads on the graphics card.",
            GpuState.WaitingForMemory => $"Waits for enough free graphics memory to load {name}.",
            GpuState.Failed => $"Waits for {name}, which did not load: {view.Detail ?? "see the top bar"}",
            GpuState.Serving when view.Model == wanted && view.RoutineTurn => $"Waits for {view.ModelName ?? wanted}: routines are using it ({string.Join(", ", view.RoutinesWaiting ?? [])}).",
            GpuState.Serving when view.Model == wanted => $"Waits for {view.ModelName ?? wanted}: another task is using it.",
            GpuState.Serving or GpuState.Loading => $"Waits for {name}: {view.ModelName ?? view.Model} is on the graphics card now.",
            _ => $"Waits for {name} to be loaded on the graphics card.",
        };
    }

    /// <summary>The first task this one builds on that is not accepted and merged yet, if any.</summary>
    private async Task<WorkTask?> BlockerAsync(WorkTask task, CancellationToken cancellationToken)
    {
        foreach (var id in task.DependsOn)
        {
            // A task that no longer exists holds nothing up.
            if (await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { IsDoneForOthers: false } blocker)
            {
                return blocker;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks who works on the task now: its desk, when its account takes work, or else the
    /// first reserve model of the office whose own account does.
    /// </summary>
    /// <returns>False when nobody can, and the task waits.</returns>
    private bool Hand(WorkTask task, Office office, DeskId desk, DateTimeOffset now)
    {
        if (!office.HasDesk(desk))
        {
            return true;
        }

        var agent = office.Locate(desk).Desk.Agent;
        var scope = EngineLimits.ScopeOf(agent);
        if (limits.PausedUntil(scope) is not { } until)
        {
            if (task.ReserveModel is not null)
            {
                task.UseReserve(null);
                task.AddNote(NoteAuthor.System, "The desk's own account takes work again: it goes on from here.", now);
            }

            return true;
        }

        var reserve = office.ReserveModels.FirstOrDefault(m =>
        {
            var reserveScope = EngineLimits.ScopeOf(AgentProfile.ForReserve(m, agent.AgentName));
            return reserveScope != scope && limits.PausedUntil(reserveScope) is null;
        });
        if (reserve is null)
        {
            return false;
        }

        if (task.ReserveModel != reserve)
        {
            task.UseReserve(reserve);
            task.AddNote(
                NoteAuthor.System,
                $"The desk's account is over its limits until {until.ToLocalTime():HH:mm}: the reserve model {reserve} goes on with this task meanwhile.",
                now);
        }

        return true;
    }

    private bool Admitted(WorkTask task, Office office, DeskId desk)
    {
        if (!office.HasDesk(desk))
        {
            return true;
        }

        var model = AgentSetups.AgentFor(office.Locate(desk).Desk, task).Model;
        return !gpu.IsLocal(model) || gpu.Admit(model!);
    }

    private async Task<Office> OfficeOfAsync(WorkTask task, Dictionary<OfficeId, Office> cache, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(task.OfficeId, out var office))
        {
            office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
            cache[task.OfficeId] = office;
        }

        return office;
    }
}
