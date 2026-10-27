using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

public sealed class RoutineQueries(IRoutineRepository routines, IEnumerable<IRoutineKind> kinds)
{
    public IReadOnlyList<RoutineKindView> Kinds() =>
        [.. kinds.OrderBy(k => k.Name, StringComparer.CurrentCulture).Select(k => new RoutineKindView(k.Key, k.Name, k.Description, k.DefaultSettings))];

    public async Task<IReadOnlyList<RoutineView>> ListAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var list = await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false);
        var views = new List<RoutineView>(list.Count);
        foreach (var routine in list)
        {
            var runs = await routines.ListRunsAsync(routine.Id, 1, cancellationToken).ConfigureAwait(false);
            var last = runs.Count > 0 ? runs[0] : null;
            views.Add(new RoutineView(
                routine.Id.Value, routine.OfficeId.Value, routine.Name, routine.Kind,
                kinds.FirstOrDefault(k => k.Key == routine.Kind)?.Name ?? routine.Kind,
                ScheduleView.Of(routine.Schedule), routine.Settings, routine.Enabled, routine.AskFirst, routine.LastRunAt, routine.NextRunAt,
                last is null ? null : RoutineRunView.Of(last) with { Log = string.Empty }));
        }

        return views;
    }

    public async Task<IReadOnlyList<RoutineRunView>> RunsAsync(RoutineId id, CancellationToken cancellationToken)
    {
        await routines.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return [.. (await routines.ListRunsAsync(id, 20, cancellationToken).ConfigureAwait(false)).Select(RoutineRunView.Of)];
    }

    public async Task<IReadOnlyList<ProposalView>> ProposalsAsync(OfficeId officeId, ProposalStatus? status, CancellationToken cancellationToken)
    {
        var names = (await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id, r => r.Name);
        return
        [
            .. (await routines.ListProposalsAsync(officeId, status, cancellationToken).ConfigureAwait(false))
                .Select(p => new ProposalView(
                    p.Id.Value, p.RoutineId.Value, names.GetValueOrDefault(p.RoutineId, "A routine"), p.Action, p.Title, p.Body, p.Status,
                    p.CreatedAt, p.DecidedAt, p.Outcome)),
        ];
    }
}
