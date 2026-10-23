using Openspace.Application.Abstractions;

namespace Openspace.Application.Work;

/// <param name="Tasks">Tasks of the desk that spent anything.</param>
public sealed record DeskUsageView(
    Guid OfficeId, string OfficeName, Guid DeskId, string DeskName, int Tasks, long Input, long Output, long CacheRead, long CacheWrite);

/// <summary>What agents spent, desk by desk, over every task they worked on.</summary>
public sealed class UsageQueries(IOfficeRepository offices, IWorkTaskRepository tasks)
{
    public async Task<IReadOnlyList<DeskUsageView>> ByDeskAsync(CancellationToken cancellationToken)
    {
        var desks = (await offices.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(o => o.Rooms.SelectMany(r => r.Desks.Select(d => (Office: o, Desk: d))))
            .ToDictionary(x => x.Desk.Id);
        var spent = (await tasks.ListAsync(null, null, cancellationToken).ConfigureAwait(false))
            .Where(t => t.DeskId is { } id && desks.ContainsKey(id) && t.Usage.Count > 0)
            .GroupBy(t => t.DeskId!.Value);
        return
        [
            .. spent
                .Select(g =>
                {
                    var (office, desk) = desks[g.Key];
                    var total = g.Aggregate(Domain.Work.TokenCount.Zero, (sum, t) => sum + t.TotalTokens);
                    return new DeskUsageView(office.Id.Value, office.Name, desk.Id.Value, desk.Name, g.Count(), total.Input, total.Output, total.CacheRead, total.CacheWrite);
                })
                .OrderByDescending(d => d.Input + d.Output + d.CacheRead + d.CacheWrite),
        ];
    }
}
