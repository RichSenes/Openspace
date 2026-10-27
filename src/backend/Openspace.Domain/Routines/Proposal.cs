using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Routines;

public readonly record struct ProposalId(Guid Value)
{
    public static ProposalId New() => new(Guid.CreateVersion7());
}

public enum ProposalStatus
{
    Pending,
    Approved,
    Rejected,

    /// <summary>Approved and carried out.</summary>
    Done,

    /// <summary>Approved, but carrying it out did not work.</summary>
    Failed,
}

/// <summary>
/// Something a routine wants to do with the person's things (file a mail, add a reminder,
/// save a tailored CV): it waits for a yes or a no, and only a yes lets the routine act.
/// </summary>
public sealed class Proposal : AggregateRoot<ProposalId>
{
    public const int MaxPayloadLength = 100_000;

    private Proposal(
        ProposalId id, RoutineId routineId, RoutineRunId runId, OfficeId officeId, string action, string title, string body, string payload, DateTimeOffset createdAt)
        : base(id)
    {
        RoutineId = routineId;
        RunId = runId;
        OfficeId = officeId;
        Action = action;
        Title = title;
        Body = body;
        Payload = payload;
        CreatedAt = createdAt;
    }

    public RoutineId RoutineId { get; }

    public RoutineRunId RunId { get; }

    public OfficeId OfficeId { get; }

    /// <summary>What a yes does, as the routine's kind names it: "add-reminder", "tailor-cv"…</summary>
    public string Action { get; }

    public string Title { get; }

    /// <summary>The proposal for people, in Markdown.</summary>
    public string Body { get; }

    /// <summary>What the routine needs to carry it out, as JSON.</summary>
    public string Payload { get; }

    public ProposalStatus Status { get; private set; } = ProposalStatus.Pending;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>What came of it once carried out, or why it could not be.</summary>
    public string? Outcome { get; private set; }

    public static Proposal Make(RoutineRun run, string action, string title, string body, string? payload, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        var json = string.IsNullOrWhiteSpace(payload) ? "{}" : payload.Trim();
        if (json.Length > MaxPayloadLength)
        {
            throw new DomainException($"A proposal's payload cannot be longer than {MaxPayloadLength} characters.");
        }

        var proposal = new Proposal(
            ProposalId.New(), run.RoutineId, run.Id, run.OfficeId, Guard.NotBlank(action, "Proposal action", 40),
            Guard.NotBlank(title, "Proposal title", 200), Guard.NotBlank(body, "Proposal text", 20_000), json, now);
        proposal.Raise(new ProposalChanged(proposal.Id, run.OfficeId, now));
        return proposal;
    }

    public void Approve(DateTimeOffset now) => Decide(ProposalStatus.Approved, now);

    public void Reject(DateTimeOffset now) => Decide(ProposalStatus.Rejected, now);

    public void Carried(string outcome, DateTimeOffset now) => Close(ProposalStatus.Done, outcome, now);

    public void CouldNotCarry(string reason, DateTimeOffset now) => Close(ProposalStatus.Failed, reason, now);

    private void Decide(ProposalStatus status, DateTimeOffset now)
    {
        if (Status != ProposalStatus.Pending)
        {
            throw new DomainException($"This proposal was already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = status;
        DecidedAt = now;
        Raise(new ProposalChanged(Id, OfficeId, now));
    }

    private void Close(ProposalStatus status, string outcome, DateTimeOffset now)
    {
        if (Status != ProposalStatus.Approved)
        {
            throw new DomainException("Only an approved proposal is carried out.");
        }

        Status = status;
        Outcome = Guard.NotBlank(outcome, "Outcome", 2000);
        Raise(new ProposalChanged(Id, OfficeId, now));
    }
}

public sealed record ProposalChanged(ProposalId ProposalId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;
