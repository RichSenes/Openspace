using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Application.Routines;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

internal sealed class InMemoryRoutines : IRoutineRepository
{
    public List<Routine> Items { get; } = [];

    public List<RoutineRun> Runs { get; } = [];

    public List<Proposal> Proposals { get; } = [];

    public List<SeenItem> Seen { get; } = [];

    public Task<Routine?> FindAsync(RoutineId id, CancellationToken cancellationToken) => Task.FromResult(Items.Find(r => r.Id == id));

    public Task<IReadOnlyList<Routine>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Routine>>([.. Items.Where(r => r.OfficeId == officeId).OrderBy(r => r.Name)]);

    public Task<IReadOnlyList<Routine>> ListDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Routine>>([.. Items.Where(r => r.IsDue(now)).OrderBy(r => r.NextRunAt)]);

    public void Add(Routine routine) => Items.Add(routine);

    public void Remove(Routine routine) => Items.Remove(routine);

    public Task<RoutineRun?> FindRunAsync(RoutineRunId id, CancellationToken cancellationToken) => Task.FromResult(Runs.Find(r => r.Id == id));

    public Task<IReadOnlyList<RoutineRun>> ListRunsAsync(RoutineId routineId, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutineRun>>([.. Runs.Where(r => r.RoutineId == routineId).OrderByDescending(r => r.StartedAt).Take(limit)]);

    public Task<IReadOnlyList<RoutineRun>> ListUnfinishedRunsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoutineRun>>([.. Runs.Where(r => r.Status == RoutineRunStatus.Running)]);

    public void AddRun(RoutineRun run) => Runs.Add(run);

    public Task<Proposal?> FindProposalAsync(ProposalId id, CancellationToken cancellationToken) => Task.FromResult(Proposals.Find(p => p.Id == id));

    public Task<IReadOnlyList<Proposal>> ListProposalsAsync(OfficeId officeId, ProposalStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Proposal>>([.. Proposals.Where(p => p.OfficeId == officeId && (status is null || p.Status == status)).OrderByDescending(p => p.CreatedAt)]);

    public void AddProposal(Proposal proposal) => Proposals.Add(proposal);

    public Task<SeenItem?> FindSeenAsync(RoutineId routineId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Seen.Find(s => s.RoutineId == routineId && s.Key == key));

    public void AddSeen(SeenItem item) => Seen.Add(item);
}

internal sealed class RecordingOutbox : IRoutineOutbox
{
    public List<(string Routine, string Text, string? File)> Sent { get; } = [];

    public Task SendAsync(Routine routine, string text, string? filePath, CancellationToken cancellationToken)
    {
        Sent.Add((routine.Name, text, filePath));
        return Task.CompletedTask;
    }
}

/// <summary>Job offers made up for the tests, proposed once each, as the real routine must.</summary>
internal sealed class FakeOffersKind : IRoutineKind
{
    public string Key => "fake-offers";

    public string Name => "Offers of the day";

    public string Description => "Made-up offers, each shown once.";

    public string DefaultSettings => """{"country":"JP"}""";

    public List<(string Url, string Title, string Detail)> Offers { get; } = [];

    public List<string> CarriedOut { get; } = [];

    public Exception? Fail { get; set; }

    public Exception? FailCarry { get; set; }

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        if (Fail is not null)
        {
            throw Fail;
        }

        var shown = 0;
        foreach (var (url, title, detail) in Offers)
        {
            if (!await context.MeetAsync(url, RoutineContext.Fingerprint(title, detail), title, cancellationToken))
            {
                continue;
            }

            context.Propose("tailor-cv", title, detail, new { url });
            await context.MarkAsync(url, SeenState.Shown, cancellationToken);
            shown++;
        }

        await context.DeliverAsync($"{shown} new offers", null, cancellationToken);
        return $"{shown} new offers";
    }

    public Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        if (FailCarry is not null)
        {
            throw FailCarry;
        }

        CarriedOut.Add(proposal.Title);
        return Task.FromResult("Tailored CV saved.");
    }

    public Task RejectedAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken) =>
        context.MarkAsync(JsonDocument.Parse(proposal.Payload).RootElement.GetProperty("url").GetString()!, SeenState.Dismissed, cancellationToken);
}
