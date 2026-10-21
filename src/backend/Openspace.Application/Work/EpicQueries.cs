using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Work;

public sealed class EpicQueries(IEpicRepository epics)
{
    public async Task<IReadOnlyList<EpicView>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        [.. (await epics.ListAsync(officeId, cancellationToken).ConfigureAwait(false)).Select(EpicView.From)];
}
