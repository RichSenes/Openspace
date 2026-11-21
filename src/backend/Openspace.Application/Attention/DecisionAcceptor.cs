using Openspace.Application.Brain;

namespace Openspace.Application.Attention;

/// <summary>
/// Shiori's hand on proposals, before Shiori can talk: every proposal that the rules allow is
/// accepted at once and goes to the person as "accepted, to review"; the rest stay proposed and
/// wait in the inbox with the reason. It runs when a note changes in the vault and now and then.
/// </summary>
public sealed class DecisionAcceptor(IVault vault) : IDisposable
{
    // One pass at a time: the watcher and the timer may both call while a pass writes.
    private readonly SemaphoreSlim _one = new(1, 1);

    public void Dispose() => _one.Dispose();

    /// <returns>The notes accepted in this pass.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken)
    {
        await _one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notes = await vault.ListAsync(cancellationToken).ConfigureAwait(false);
            var accepted = notes.Where(DecisionRules.IsAcceptedDecision).ToList();
            var done = new List<string>();

            // The oldest proposal first: when two say the same thing, the first one written wins and
            // the second waits for the person, as it now clashes with an accepted decision.
            foreach (var proposal in notes.Where(DecisionRules.IsProposed).OrderBy(n => n.Modified).ThenBy(n => n.Path, StringComparer.Ordinal))
            {
                if (DecisionRules.WhyWaiting(proposal, accepted) is not null)
                {
                    continue;
                }

                var note = DecisionRules.With(DecisionRules.With(proposal with { Status = NoteStatus.Accepted }, DecisionRules.AcceptedBy, DecisionRules.Shiori), DecisionRules.Reviewed, "false");
                await vault.WriteAsync(note, DecisionRules.Shiori, cancellationToken, $"accepted {proposal.Path}").ConfigureAwait(false);
                if (DecisionRules.IsAcceptedDecision(note))
                {
                    accepted.Add(note);
                }

                done.Add(proposal.Path);
            }

            return done;
        }
        finally
        {
            _one.Release();
        }
    }
}
