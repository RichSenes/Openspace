using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Routines;

namespace Openspace.Application.Routines;

/// <summary>A person says yes or no to what a routine proposed; a yes lets the routine act.</summary>
public sealed partial class ProposalCommands(
    IRoutineRepository routines,
    IEnumerable<IRoutineKind> kinds,
    IRoutineOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ProposalCommands> logger)
{
    public async Task ApproveAsync(ProposalId id, CancellationToken cancellationToken)
    {
        var (proposal, kind, context) = await OpenAsync(id, cancellationToken).ConfigureAwait(false);
        proposal.Approve(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var outcome = kind is null
                ? throw new InvalidOperationException("Openspace no longer knows how to carry out this proposal.")
                : await kind.CarryOutAsync(proposal, context, cancellationToken).ConfigureAwait(false);
            proposal.Carried(string.IsNullOrWhiteSpace(outcome) ? "Done." : outcome, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCarryFailed(logger, ex, proposal.Title);
            proposal.CouldNotCarry(ex.Message, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RejectAsync(ProposalId id, CancellationToken cancellationToken)
    {
        var (proposal, kind, context) = await OpenAsync(id, cancellationToken).ConfigureAwait(false);
        proposal.Reject(clock.GetUtcNow());
        if (kind is not null)
        {
            await kind.RejectedAsync(proposal, context, cancellationToken).ConfigureAwait(false);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Proposal Proposal, IRoutineKind? Kind, RoutineContext Context)> OpenAsync(ProposalId id, CancellationToken cancellationToken)
    {
        var proposal = await routines.GetProposalAsync(id, cancellationToken).ConfigureAwait(false);
        var routine = await routines.GetAsync(proposal.RoutineId, cancellationToken).ConfigureAwait(false);
        var run = await routines.FindRunAsync(proposal.RunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Routine run", proposal.RunId.Value);
        return (proposal, kinds.FirstOrDefault(k => k.Key == routine.Kind), new RoutineContext(routine, run, routines, outbox, clock));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not carry out the proposal {Title}")]
    private static partial void LogCarryFailed(ILogger logger, Exception exception, string title);
}
