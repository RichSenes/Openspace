using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Offices;

public sealed class OfficeQueries(IOfficeRepository offices)
{
    public async Task<IReadOnlyList<OfficeView>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Select(OfficeView.From)];
    }

    public async Task<OfficeView> GetAsync(OfficeId id, CancellationToken cancellationToken) =>
        OfficeView.From(await offices.GetAsync(id, cancellationToken).ConfigureAwait(false));
}
