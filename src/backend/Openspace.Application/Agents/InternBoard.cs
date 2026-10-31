using System.Collections.Concurrent;
using Openspace.Application.Abstractions;

namespace Openspace.Application.Agents;

/// <summary>A helper agent working for a desk: an intern beside it, until it comes back.</summary>
/// <param name="Id">The hand-off it came for.</param>
/// <param name="What">What it was asked to do, in a few words.</param>
public sealed record Intern(string Id, string What, DateTimeOffset Since);

/// <summary>The interns at one desk, oldest first.</summary>
public sealed record DeskInterns(Guid OfficeId, Guid DeskId, Guid TaskId, IReadOnlyList<Intern> Interns);

/// <summary>
/// The interns at every desk, kept in memory like the bubbles. An intern arrives when its
/// agent hands off part of the work and leaves when the helper comes back, or when the
/// agent stops working.
/// </summary>
public sealed class InternBoard(IActivityPublisher publisher)
{
    private readonly ConcurrentDictionary<Guid, DeskInterns> _byDesk = new();
    private readonly Lock _gate = new();

    public IReadOnlyList<DeskInterns> Snapshot(Guid? officeId = null) =>
        [.. _byDesk.Values.Where(d => d.Interns.Count > 0 && (officeId is null || d.OfficeId == officeId))];

    public IReadOnlyList<Intern> AtDesk(Guid deskId) => _byDesk.TryGetValue(deskId, out var desk) ? desk.Interns : [];

    /// <summary>An intern arrives for a hand-off; told again about the same one, nothing changes.</summary>
    public Task ArriveAsync(Guid officeId, Guid deskId, Guid taskId, string id, string what, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DeskInterns next;
        lock (_gate)
        {
            var current = _byDesk.TryGetValue(deskId, out var desk) && desk.TaskId == taskId ? desk.Interns : [];
            if (current.Any(i => i.Id == id))
            {
                return Task.CompletedTask;
            }

            next = new DeskInterns(officeId, deskId, taskId, [.. current, new Intern(id, what, now)]);
            _byDesk[deskId] = next;
        }

        return publisher.InternsChangedAsync(next, cancellationToken);
    }

    /// <summary>The helper of a hand-off came back: its intern leaves.</summary>
    public Task LeaveAsync(Guid deskId, string id, CancellationToken cancellationToken) =>
        Change(deskId, interns => [.. interns.Where(i => i.Id != id)], cancellationToken);

    /// <summary>The agent stopped working: every intern of the desk leaves with it.</summary>
    public Task DismissAsync(Guid deskId, CancellationToken cancellationToken) =>
        Change(deskId, _ => [], cancellationToken);

    private Task Change(Guid deskId, Func<IReadOnlyList<Intern>, IReadOnlyList<Intern>> change, CancellationToken cancellationToken)
    {
        DeskInterns next;
        lock (_gate)
        {
            if (!_byDesk.TryGetValue(deskId, out var desk))
            {
                return Task.CompletedTask;
            }

            var interns = change(desk.Interns);
            if (interns.Count == desk.Interns.Count)
            {
                return Task.CompletedTask;
            }

            next = desk with { Interns = interns };
            _byDesk[deskId] = next;
        }

        return publisher.InternsChangedAsync(next, cancellationToken);
    }
}
