using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Application.Abstractions;

/// <summary>Routines, their runs, their proposals and what they have already met.</summary>
public interface IRoutineRepository
{
    Task<Routine?> FindAsync(RoutineId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Routine>> ListAsync(OfficeId officeId, CancellationToken cancellationToken);

    /// <summary>The offices that have at least one routine.</summary>
    Task<IReadOnlyList<OfficeId>> OfficesWithRoutinesAsync(CancellationToken cancellationToken);

    /// <summary>Routines turned on whose next run is at or before <paramref name="now"/>, earliest first.</summary>
    Task<IReadOnlyList<Routine>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken);

    void Add(Routine routine);

    void Remove(Routine routine);

    Task<RoutineRun?> FindRunAsync(RoutineRunId id, CancellationToken cancellationToken);

    /// <summary>The latest runs of a routine, newest first.</summary>
    Task<IReadOnlyList<RoutineRun>> ListRunsAsync(RoutineId routineId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<RoutineRun>> ListUnfinishedRunsAsync(CancellationToken cancellationToken);

    void AddRun(RoutineRun run);

    Task<Proposal?> FindProposalAsync(ProposalId id, CancellationToken cancellationToken);

    /// <summary>Proposals of an office, newest first; only those in <paramref name="status"/> when given.</summary>
    Task<IReadOnlyList<Proposal>> ListProposalsAsync(OfficeId officeId, ProposalStatus? status, CancellationToken cancellationToken);

    void AddProposal(Proposal proposal);

    Task<SeenItem?> FindSeenAsync(RoutineId routineId, string key, CancellationToken cancellationToken);

    void AddSeen(SeenItem item);
}
