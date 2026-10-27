using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.LocalModels;

/// <summary>
/// One look at the graphics card, every few seconds: gives it to the programs that come
/// first, loads the model desks wait for when it is free, and unloads it when nobody uses it.
/// </summary>
/// <remarks>Not thread-safe: call it from a single place (its background service).</remarks>
public sealed class GpuShift(
    GpuArbiter arbiter,
    IGpuProbe probe,
    ILocalModelServer server,
    IWorkTaskRepository tasks,
    IOfficeRepository offices,
    IAgentRuntime runtime,
    ActivityBoard activity,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<LocalModelOptions> options,
    TimeProvider clock)
{
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        if (!settings.Enabled)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = clock.GetUtcNow();
        var local = await LocalTasksAsync(cancellationToken).ConfigureAwait(false);
        var holders = probe.RunningHolders(settings.Holders)
            .Concat(arbiter.Claims())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var running = server.Running;

        if (holders.Count > 0)
        {
            await GiveWayAsync(holders, local, running, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        arbiter.YieldingSince = null;
        var wanted = arbiter.Wanted();
        if (running is null)
        {
            await LoadAsync(wanted, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await server.IsAnsweringAsync(cancellationToken).ConfigureAwait(false))
        {
            if (now - arbiter.LoadingSince > settings.StartTimeout)
            {
                await FailAsync(running, $"The model did not answer within {settings.StartTimeout.TotalMinutes:0} minutes", now, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                Report(GpuState.Loading, running, local.Count > 0);
            }

            return;
        }

        arbiter.LoadingSince = null;
        if (local.Count > 0 || wanted == running)
        {
            arbiter.LastUsed = now;
        }

        if (local.Count == 0 && wanted is not null && wanted != running)
        {
            // Another desk waits for a different model: make room for it.
            await StopAsync(cancellationToken).ConfigureAwait(false);
            Report(GpuState.Free, null, false, $"Unloaded {NameOf(running)} to make room for {NameOf(wanted)}.");
            return;
        }

        if (local.Count == 0 && now - arbiter.LastUsed >= settings.IdleUnload)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
            Report(GpuState.Free, null, false, $"Unloaded {NameOf(running)}: unused for {settings.IdleUnload.TotalMinutes:0} minutes.");
            return;
        }

        Report(GpuState.Serving, running, local.Count > 0);
    }

    /// <summary>Unloads the model, if Openspace started one: when Openspace stops, say.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (server.Running is not null)
        {
            await server.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        arbiter.LoadingSince = null;
    }

    private async Task GiveWayAsync(
        List<string> holders, List<WorkTask> local, string? running, DateTimeOffset now, CancellationToken cancellationToken)
    {
        arbiter.YieldingSince ??= now;
        var grace = options.CurrentValue.YieldGrace;
        if (local.Count > 0 && now - arbiter.YieldingSince < grace)
        {
            var left = arbiter.YieldingSince.Value + grace;
            Report(GpuState.Yielding, running, true, $"Local tasks may finish until {left.ToLocalTime():HH:mm}, then they go back to the queue.", holders);
            return;
        }

        foreach (var task in local)
        {
            await SendBackAsync(task, holders, now, cancellationToken).ConfigureAwait(false);
        }

        await StopAsync(cancellationToken).ConfigureAwait(false);
        Report(GpuState.Lent, null, false, "Local desks wait until the graphics card is free again.", holders);
    }

    private async Task SendBackAsync(WorkTask task, List<string> holders, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Back in the queue before the agent stops, so its last events find nothing to change.
        task.AddNote(
            NoteAuthor.System,
            $"The graphics card went to {string.Join(", ", holders)}: this task went back to the queue and goes on in its own session once the card is free again.",
            now);
        task.Requeue(now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activity.ClearTaskAsync(task.Id.Value, now, cancellationToken).ConfigureAwait(false);
        if (task.Run is { } run)
        {
            await runtime.AbortAsync(run.SessionId, run.WorkspacePath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(string? wanted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (arbiter.LoadingSince is not null)
        {
            // Started, and gone before it ever answered.
            await FailAsync(wanted ?? "", "The model server stopped while loading", now, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (wanted is null)
        {
            Report(GpuState.Free, null, false);
            return;
        }

        if (now < arbiter.RetryAt)
        {
            Report(GpuState.Failed, wanted, false, $"{NameOf(wanted)} did not load: trying again at {arbiter.RetryAt.Value.ToLocalTime():HH:mm}.");
            return;
        }

        if (options.CurrentValue.Find(wanted) is not { } model)
        {
            arbiter.Forget(wanted);
            Report(GpuState.Failed, wanted, false, $"No local model is called {wanted}: check the LocalModels settings.");
            return;
        }

        if (await server.IsAnsweringAsync(cancellationToken).ConfigureAwait(false))
        {
            Report(GpuState.Failed, wanted, false, $"Another program already answers at {options.CurrentValue.ServerUrl}: Openspace leaves it alone.");
            return;
        }

        if (await probe.MemoryAsync(cancellationToken).ConfigureAwait(false) is { } memory && memory.FreeMiB < model.MemoryMiB)
        {
            Report(GpuState.WaitingForMemory, wanted, false, $"{memory.FreeMiB} MiB free on the graphics card, {model.MemoryMiB} needed.");
            return;
        }

        await server.StartAsync(model, cancellationToken).ConfigureAwait(false);
        arbiter.LoadingSince = now;
        arbiter.LastUsed = now;
        Report(GpuState.Loading, wanted, false);
    }

    private async Task FailAsync(string id, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        arbiter.RetryAt = now + options.CurrentValue.RetryAfter;
        Report(GpuState.Failed, id, false, $"{reason}: trying again at {arbiter.RetryAt.Value.ToLocalTime():HH:mm}.");
    }

    /// <summary>The running tasks whose agent is on a local model.</summary>
    private async Task<List<WorkTask>> LocalTasksAsync(CancellationToken cancellationToken)
    {
        var running = await tasks.ListAsync(null, WorkTaskStatus.Running, cancellationToken).ConfigureAwait(false);
        var local = new List<WorkTask>();
        var cache = new Dictionary<OfficeId, Office?>();
        foreach (var task in running)
        {
            if (!cache.TryGetValue(task.OfficeId, out var office))
            {
                office = await offices.FindAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
                cache[task.OfficeId] = office;
            }

            if (office is not null && task.DeskId is { } desk && office.HasDesk(desk)
                && arbiter.IsLocal(AgentSetups.AgentFor(office.Locate(desk).Desk, task).Model))
            {
                local.Add(task);
            }
        }

        return local;
    }

    private void Report(GpuState state, string? id, bool busy, string? detail = null, IReadOnlyList<string>? holders = null)
    {
        var provider = options.CurrentValue.Provider;
        arbiter.Report(new GpuView(state, id is null ? null : $"{provider}/{id}", id is null ? null : NameOf(id), holders ?? [], detail), busy);
    }

    private string NameOf(string id) => options.CurrentValue.Find(id) is { Name.Length: > 0 } model ? model.Name : id;
}
