using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

public readonly record struct AgreementId(Guid Value)
{
    public static AgreementId New() => new(Guid.CreateVersion7());
}

/// <summary>Where an agreement stands.</summary>
public enum AgreementStatus
{
    /// <summary>The rules of the constitution settled it: one goes first.</summary>
    Settled,

    /// <summary>The rules did not settle it within the turns allowed: one question waits for the person.</summary>
    WaitingForPerson,

    /// <summary>The person chose who goes first.</summary>
    DecidedByPerson,

    /// <summary>One of the two tasks left the queue before the person answered: nothing to agree on any more.</summary>
    Dropped,
}

/// <summary>
/// Two desks whose planned work clashes (the same files, at the same time) agree on who goes
/// first. Their proposals and the turns are computed by code under the constitution; what the
/// rules do not settle goes to the person as one question, and the person's answer is final.
/// Disagreements stay between desks, never with the person.
/// </summary>
public sealed class Agreement : AggregateRoot<AgreementId>
{
    /// <summary>Turns the desks have before the question goes to the person.</summary>
    public const int MaxTurns = 4;

    private Agreement(
        AgreementId id, OfficeId officeId, DeskId firstDesk, WorkTaskId firstTask, DeskId secondDesk, WorkTaskId secondTask,
        string subject, DateTimeOffset openedAt)
        : base(id)
    {
        OfficeId = officeId;
        FirstDesk = firstDesk;
        FirstTask = firstTask;
        SecondDesk = secondDesk;
        SecondTask = secondTask;
        Subject = subject;
        OpenedAt = openedAt;
    }

    public OfficeId OfficeId { get; }

    /// <summary>One side: the desk whose task was queued first.</summary>
    public DeskId FirstDesk { get; }

    public WorkTaskId FirstTask { get; }

    public DeskId SecondDesk { get; }

    public WorkTaskId SecondTask { get; }

    /// <summary>What they clash on: "src/web/cart.ts and 1 more file".</summary>
    public string Subject { get; }

    public AgreementStatus Status { get; private set; }

    /// <summary>The task that goes first; null while the person has not answered.</summary>
    public WorkTaskId? GoesFirst { get; private set; }

    /// <summary>What each side proposed, in a sentence.</summary>
    public string FirstProposal { get; private set; } = string.Empty;

    public string SecondProposal { get; private set; } = string.Empty;

    /// <summary>The rule that settled it, or why it went to the person.</summary>
    public string Reason { get; private set; } = string.Empty;

    public int Turns { get; private set; }

    /// <summary>The minutes in the vault: <c>Agreements/&lt;date&gt;-&lt;slug&gt;.md</c>.</summary>
    public string MinutesPath { get; private set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; }

    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Who answered, when the person did.</summary>
    public string? DecidedBy { get; private set; }

    public static Agreement Open(
        OfficeId officeId, DeskId firstDesk, WorkTaskId firstTask, DeskId secondDesk, WorkTaskId secondTask, string subject, DateTimeOffset now)
    {
        if (firstDesk == secondDesk || firstTask == secondTask)
        {
            throw new DomainException("An agreement is between two desks on two tasks.");
        }

        return new Agreement(AgreementId.New(), officeId, firstDesk, firstTask, secondDesk, secondTask, Guard.NotBlank(subject, "Agreement subject", 300), now);
    }

    /// <param name="goesFirst">Null when the rules did not settle it: then the person is asked.</param>
    public void Record(string firstProposal, string secondProposal, int turns, WorkTaskId? goesFirst, string reason, string minutesPath, DateTimeOffset now)
    {
        if (turns is < 1 or > MaxTurns)
        {
            throw new DomainException($"An agreement takes from 1 to {MaxTurns} turns.");
        }

        if (goesFirst is { } task && task != FirstTask && task != SecondTask)
        {
            throw new DomainException("Only one of the two tasks can go first.");
        }

        FirstProposal = Guard.NotBlank(firstProposal, "Proposal", 500);
        SecondProposal = Guard.NotBlank(secondProposal, "Proposal", 500);
        Turns = turns;
        GoesFirst = goesFirst;
        Reason = Guard.NotBlank(reason, "Reason", 500);
        MinutesPath = Guard.NotBlank(minutesPath, "Minutes", 300);
        Status = goesFirst is null ? AgreementStatus.WaitingForPerson : AgreementStatus.Settled;
        ClosedAt = goesFirst is null ? null : now;
    }

    /// <summary>The person's answer: final, whatever the desks proposed.</summary>
    public void Decide(WorkTaskId goesFirst, string who, DateTimeOffset now)
    {
        if (Status != AgreementStatus.WaitingForPerson)
        {
            throw new DomainException("Only an agreement waiting for you can be decided.");
        }

        if (goesFirst != FirstTask && goesFirst != SecondTask)
        {
            throw new DomainException("Only one of the two tasks can go first.");
        }

        GoesFirst = goesFirst;
        DecidedBy = Guard.NotBlank(who, "Who", 80);
        Status = AgreementStatus.DecidedByPerson;
        ClosedAt = now;
    }

    /// <summary>One task left the queue before the person answered.</summary>
    public void Drop(DateTimeOffset now)
    {
        if (Status == AgreementStatus.WaitingForPerson)
        {
            Status = AgreementStatus.Dropped;
            ClosedAt = now;
        }
    }

    public bool Concerns(WorkTaskId task) => task == FirstTask || task == SecondTask;

    /// <summary>The task that waits for the other, once one goes first.</summary>
    public WorkTaskId? GoesSecond => GoesFirst is { } first ? (first == FirstTask ? SecondTask : FirstTask) : null;
}
