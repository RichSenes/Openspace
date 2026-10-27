using Microsoft.EntityFrameworkCore;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Infrastructure.Persistence;

internal sealed class RoutineRepository(OpenspaceDbContext db) : IRoutineRepository
{
    public Task<Routine?> FindAsync(RoutineId id, CancellationToken cancellationToken) =>
        db.Routines.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Routine>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        await db.Routines.Where(r => r.OfficeId == officeId).OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Routine>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Routines.Where(r => r.Enabled && r.NextRunAt != null && r.NextRunAt <= now).OrderBy(r => r.NextRunAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Routine routine) => db.Routines.Add(routine);

    public void Remove(Routine routine) => db.Routines.Remove(routine);

    public Task<RoutineRun?> FindRunAsync(RoutineRunId id, CancellationToken cancellationToken) =>
        db.RoutineRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<RoutineRun>> ListRunsAsync(RoutineId routineId, int limit, CancellationToken cancellationToken) =>
        await db.RoutineRuns.Where(r => r.RoutineId == routineId).OrderByDescending(r => r.StartedAt).Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<RoutineRun>> ListUnfinishedRunsAsync(CancellationToken cancellationToken) =>
        await db.RoutineRuns.Where(r => r.Status == RoutineRunStatus.Running).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void AddRun(RoutineRun run) => db.RoutineRuns.Add(run);

    public Task<Proposal?> FindProposalAsync(ProposalId id, CancellationToken cancellationToken) =>
        db.Proposals.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Proposal>> ListProposalsAsync(OfficeId officeId, ProposalStatus? status, CancellationToken cancellationToken) =>
        await db.Proposals.Where(p => p.OfficeId == officeId && (status == null || p.Status == status))
            .OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void AddProposal(Proposal proposal) => db.Proposals.Add(proposal);

    public Task<SeenItem?> FindSeenAsync(RoutineId routineId, string key, CancellationToken cancellationToken) =>
        db.SeenItems.FirstOrDefaultAsync(s => s.RoutineId == routineId && s.Key == key, cancellationToken);

    public void AddSeen(SeenItem item) => db.SeenItems.Add(item);
}
