using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.Brain;
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
    IWorkInProgress work,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<QueueOptions> options,
    TimeProvider clock,
    Characters.AgreementKeeper? agreements = null)
{
    /// <returns>The tasks that just started and now need an agent.</returns>
    /// <remarks>Every queued task that does not start gets the reason it waits, for the board.</remarks>
    public async Task<IReadOnlyList<WorkTaskId>> DispatchAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var queued = await tasks.ListAsync(null, WorkTaskStatus.Queued, cancellationToken).ConfigureAwait(false);
        var seats = options.CurrentValue.Seats;

        // Desks whose planned work clashes agree first on who goes first; the second waits.
        IReadOnlyDictionary<Guid, string> agreed = queued.Count > 0 && agreements is not null
            ? (await agreements.RoundAsync(cancellationToken).ConfigureAwait(false)).Hold
            : new Dictionary<Guid, string>();
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
        var runningIds = running.Select(t => t.Id.Value).ToHashSet();
        var inFlight = work.Current().Where(w => runningIds.Contains(w.TaskId)).ToList();
        var startedTasks = new List<WorkTask>();

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

            if (agreed.TryGetValue(task.Id.Value, out var agreement))
            {
                reasons[task.Id.Value] = agreement;
                continue;
            }

            // A desk on the day or night shift takes work only while it is in.
            if (await ShiftWaitAsync(task, desk, now, officeCache, cancellationToken).ConfigureAwait(false) is { } offShift)
            {
                reasons[task.Id.Value] = offShift;
                continue;
            }

            // Work that builds on other tasks waits until they are accepted and merged.
            if (await BlockerAsync(task, cancellationToken).ConfigureAwait(false) is { } blocker)
            {
                reasons[task.Id.Value] = $"Waits for \u201c{blocker.Title}\u201d to be accepted and merged.";
                continue;
            }

            // Two pieces of work on the same files do not run side by side (constitution rule 6).
            if (await OverlapAsync(task, inFlight, startedTasks, cancellationToken).ConfigureAwait(false) is { } overlap)
            {
                reasons[task.Id.Value] = overlap;
                continue;
            }

            // Desks on an account over its limits keep their tasks queued until it is back,
            // unless the office has a reserve model that can go on with them meanwhile.
            if (anyPaused || task.ReserveModel is not null)
            {
                var office = await OfficeOfAsync(task, officeCache, cancellationToken).ConfigureAwait(false);
                if (!Hand(task, office, desk, now))
                {
                    reasons[task.Id.Value] = Paused(office, desk, task.WaitsForItsDesk);
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
            startedTasks.Add(task);
        }

        if (started.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await waits.ReplaceAsync(reasons, cancellationToken).ConfigureAwait(false);
        return started;
    }

    /// <summary>
    /// Why the task waits for another one changing the files it names, or null when it may start:
    /// no running work in its office touches them, the office lets overlapping work run, or the
    /// task names no files at all.
    /// </summary>
    /// <param name="startedNow">Tasks started earlier in this round: git has seen nothing of them yet, their words count.</param>
    private async Task<string?> OverlapAsync(
        WorkTask task, IReadOnlyList<WorkInFlight> inFlight, IReadOnlyList<WorkTask> startedNow, CancellationToken cancellationToken)
    {
        // One office is one repository: work in another office never collides with this one.
        var office = task.OfficeId.Value;
        var sameOffice = inFlight.Where(w => w.OfficeId == office).ToList();
        var startedHere = startedNow.Where(t => t.OfficeId == task.OfficeId).ToList();
        if (sameOffice.Count == 0 && startedHere.Count == 0)
        {
            return null;
        }

        if (await offices.FindAsync(task.OfficeId, cancellationToken).ConfigureAwait(false) is not { HoldOverlappingWork: true } found)
        {
            return null;
        }

        // A task that names no files never waits on the room alone: holding every task of a busy
        // room would stall offices whose rooms are their only split, for conflicts that mostly
        // never come. Merges flag what gets through.
        var wanted = PathTokens.Extract($"{task.Title}\n{task.Description}", found.RepositoryPath);
        if (wanted.Count == 0)
        {
            return null;
        }

        foreach (var other in sameOffice.Concat(startedHere.Select(t => NowKeeper.Describe(t, found, []).Work)))
        {
            var shared = PathOverlap.Shared(wanted, other.Paths);
            if (shared.Count > 0)
            {
                var more = shared.Count == 1 ? string.Empty : $" and {shared.Count - 1} more {(shared.Count == 2 ? "file" : "files")}";
                return $"Waits for {other.Desk}, who is changing {shared[0]}{more}.";
            }
        }

        return null;
    }

    /// <summary>Why the desk's task waits for its shift, or null when the desk is in now.</summary>
    private async Task<string?> ShiftWaitAsync(WorkTask task, DeskId desk, DateTimeOffset now, Dictionary<OfficeId, Office> cache, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(task.OfficeId, out var office))
        {
            if (await offices.FindAsync(task.OfficeId, cancellationToken).ConfigureAwait(false) is not { } found)
            {
                return null;
            }

            office = cache[task.OfficeId] = found;
        }

        if (!office.HasDesk(desk))
        {
            return null;
        }

        var shift = office.Locate(desk).Desk.Shift;
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, clock.LocalTimeZone).DateTime);
        if (shift.IsIn(local))
        {
            return null;
        }

        var from = shift == Shift.Night ? Shifts.NightStarts : Shifts.DayStarts;
        return $"Waits for its shift, from {from.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}.";
    }

    /// <summary>Why a task whose desk is over its limits found no reserve to go on with.</summary>
    private string Paused(Office office, DeskId desk, bool ownDeskOnly)
    {
        var scope = EngineLimits.ScopeOf(office.Locate(desk).Desk.Agent);
        var pause = limits.Pauses().FirstOrDefault(p => p.Scope == scope);
        var account = pause?.Label ?? "Its account";
        var until = pause is null ? string.Empty : $" until {pause.Until.ToLocalTime():HH:mm}";
        if (ownDeskOnly)
        {
            return $"{account} is over its limits{until}, and this task waits for its own desk rather than a reserve model: it goes on then.";
        }

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

        if (task.WaitsForItsDesk)
        {
            return false;
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
