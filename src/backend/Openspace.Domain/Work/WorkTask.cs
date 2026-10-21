using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

/// <summary>
/// A unit of work assigned to a desk. Named WorkTask in code to avoid clashing
/// with <see cref="System.Threading.Tasks.Task"/>; "task" everywhere else.
/// </summary>
public sealed class WorkTask : AggregateRoot<WorkTaskId>
{
    private const int MaxTitleLength = 200;
    private const int MaxNoteLength = 4000;

    private static readonly Dictionary<WorkTaskStatus, WorkTaskStatus[]> AllowedTransitions = new()
    {
        [WorkTaskStatus.Proposed] = [WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Queued] = [WorkTaskStatus.Running, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Running] = [WorkTaskStatus.Review, WorkTaskStatus.Failed, WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Review] = [WorkTaskStatus.Done, WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Failed] = [WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Done] = [],
        [WorkTaskStatus.Discarded] = [],
    };

    private readonly List<TaskNote> _notes = [];

    private WorkTask(
        WorkTaskId id,
        OfficeId officeId,
        string title,
        string description,
        string requestedBy,
        WorkTaskStatus status,
        DateTimeOffset createdAt)
        : base(id)
    {
        OfficeId = officeId;
        Title = title;
        Description = description;
        RequestedBy = requestedBy;
        Status = status;
        CreatedAt = createdAt;
    }

    public OfficeId OfficeId { get; }

    public DeskId? DeskId { get; private set; }

    public string Title { get; }

    public string Description { get; }

    /// <summary>Who asked for it. Today always the local user; kept for a multi-user future.</summary>
    public string RequestedBy { get; }

    public WorkTaskStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<TaskNote> Notes => _notes;

    /// <summary>The agent's session and workspace, once the task has started at least once.</summary>
    public AgentRun? Run { get; private set; }

    public bool IsClosed => Status is WorkTaskStatus.Done or WorkTaskStatus.Discarded;

    /// <summary>A task waiting for human approval, possibly without a desk yet.</summary>
    public static WorkTask Propose(
        Office office, string title, string? description, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(office);
        var task = new WorkTask(
            WorkTaskId.New(),
            office.Id,
            Guard.NotBlank(title, "Task title", MaxTitleLength),
            description?.Trim() ?? string.Empty,
            Guard.NotBlank(requestedBy, "Requester", 80),
            WorkTaskStatus.Proposed,
            now);
        task.Raise(new WorkTaskCreated(task.Id, office.Id, task.Title, now));
        return task;
    }

    /// <summary>A task created by a human for a given desk: it goes straight into the queue.</summary>
    public static WorkTask Assign(
        Office office, DeskId deskId, string title, string? description, string requestedBy, DateTimeOffset now)
    {
        var task = Propose(office, title, description, requestedBy, now);
        task.AssignTo(office, deskId);
        task.Approve(now);
        return task;
    }

    public void AssignTo(Office office, DeskId deskId)
    {
        ArgumentNullException.ThrowIfNull(office);
        if (office.Id != OfficeId)
        {
            throw new DomainException("A task can only be assigned to a desk of its own office.");
        }

        if (!office.HasDesk(deskId))
        {
            throw new DomainException($"Office '{office.Name}' has no desk {deskId.Value}.");
        }

        if (Status is not (WorkTaskStatus.Proposed or WorkTaskStatus.Queued or WorkTaskStatus.Failed))
        {
            throw new DomainException($"A {Status} task cannot change desk.");
        }

        if (Run is not null && DeskId != deskId)
        {
            throw new DomainException("A task that already has an agent session stays at its desk.");
        }

        DeskId = deskId;
    }

    public void Approve(DateTimeOffset now)
    {
        if (Status == WorkTaskStatus.Proposed && DeskId is null)
        {
            throw new DomainException("A task needs a desk before it can be queued.");
        }

        MoveTo(WorkTaskStatus.Queued, now);
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Queued)
        {
            throw InvalidTransition(WorkTaskStatus.Running);
        }

        MoveTo(WorkTaskStatus.Running, now);
    }

    /// <summary>Records where the agent works. Kept across retries so the agent can resume.</summary>
    public void AttachRun(AgentRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (Status != WorkTaskStatus.Running)
        {
            throw new DomainException($"Only a running task can get an agent session, not a {Status} one.");
        }

        Run = run;
    }

    public void SubmitForReview(DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Running)
        {
            throw InvalidTransition(WorkTaskStatus.Review);
        }

        MoveTo(WorkTaskStatus.Review, now);
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Running)
        {
            throw InvalidTransition(WorkTaskStatus.Failed);
        }

        AddNote(NoteAuthor.System, reason, now);
        MoveTo(WorkTaskStatus.Failed, now);
    }

    /// <summary>Puts a running or failed task back in the queue, e.g. after a restart or for a retry.</summary>
    public void Requeue(DateTimeOffset now)
    {
        if (Status is not (WorkTaskStatus.Running or WorkTaskStatus.Failed))
        {
            throw InvalidTransition(WorkTaskStatus.Queued);
        }

        MoveTo(WorkTaskStatus.Queued, now);
    }

    public void Accept(DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Review)
        {
            throw InvalidTransition(WorkTaskStatus.Done);
        }

        MoveTo(WorkTaskStatus.Done, now);
    }

    /// <summary>Rejects the result with feedback; the task goes back in the queue for the same desk.</summary>
    public void SendBack(NoteAuthor reviewer, string feedback, DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Review)
        {
            throw InvalidTransition(WorkTaskStatus.Queued);
        }

        AddNote(reviewer, feedback, now);
        MoveTo(WorkTaskStatus.Queued, now);
    }

    public void Discard(DateTimeOffset now) => MoveTo(WorkTaskStatus.Discarded, now);

    public void AddNote(NoteAuthor author, string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(author);
        var note = new TaskNote(author, Guard.NotBlank(text, "Note", MaxNoteLength), now);
        _notes.Add(note);
        Raise(new WorkTaskNoteAdded(Id, note, now));
    }

    private void MoveTo(WorkTaskStatus target, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(target))
        {
            throw InvalidTransition(target);
        }

        var from = Status;
        Status = target;
        Raise(new WorkTaskStatusChanged(Id, from, target, now));
    }

    private DomainException InvalidTransition(WorkTaskStatus target) =>
        new($"A task cannot go from {Status} to {target}.");
}
