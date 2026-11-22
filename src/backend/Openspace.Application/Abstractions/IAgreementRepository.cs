using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Abstractions;

public interface IAgreementRepository
{
    Task<Agreement?> FindAsync(AgreementId id, CancellationToken cancellationToken);

    /// <summary>Agreements of an office, or of every office when none is given, newest first.</summary>
    Task<IReadOnlyList<Agreement>> ListAsync(OfficeId? officeId, CancellationToken cancellationToken);

    void Add(Agreement agreement);
}
