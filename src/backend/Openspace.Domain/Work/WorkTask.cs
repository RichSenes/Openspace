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

    /// <summary>Most things a person can be asked to try by hand on one task.</summary>
    public const int MaxManualChecks = 20;

    public const int MaxDependencies = 10;

    private static readonly Dictionary<WorkTaskStatus, WorkTaskStatus[]> AllowedTransitions = new()
    {
        [WorkTaskStatus.Proposed] = [WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Queued] = [WorkTaskStatus.Running, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Running] = [WorkTaskStatus.Review, WorkTaskStatus.Failed, WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Review] = [WorkTaskStatus.Done, WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        [WorkTaskStatus.Failed] = [WorkTaskStatus.Queued, WorkTaskStatus.Discarded],
        // Accepted work that no longer merges goes back to its desk to catch up.
        [WorkTaskStatus.Done] = [WorkTaskStatus.Queued],
        [WorkTaskStatus.Discarded] = [],
    };

    private readonly List<TaskNote> _notes = [];
    private readonly List<ProcedureStep> _procedure = [];
    private readonly List<ManualCheck> _manualChecks = [];
    private readonly List<RoundUsage> _usage = [];

    private List<Guid> _dependsOn = [];

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
        StatusChangedAt = createdAt;
    }

    public OfficeId OfficeId { get; }

    public DeskId? DeskId { get; private set; }

    public string Title { get; }

    /// <summary>
    /// A short key for names (working copy, branch, report file): the random end of the id,
    /// as its start is the time of creation, the same for tasks proposed together.
    /// </summary>
    public string Key => Id.Value.ToString("N")[^10..];

    public string Description { get; }

    /// <summary>Who asked for it. Today always the local user; kept for a multi-user future.</summary>
    public string RequestedBy { get; }

    public WorkTaskStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the task last changed status, e.g. when its work last reached review.</summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    /// <summary>The round of work: 1, then one more each time the work is sent back.</summary>
    public int Round { get; private set; } = 1;

    /// <summary>What a person must try by hand before merging, as the agent and the reviewer listed it.</summary>
    public IReadOnlyList<ManualCheck> ManualChecks => _manualChecks;

    /// <summary>Tokens the agent spent, round by round.</summary>
    public IReadOnlyList<RoundUsage> Usage => _usage;

    public TokenCount TotalTokens => _usage.Aggregate(TokenCount.Zero, (sum, round) => sum + round.Tokens);

    public IReadOnlyList<TaskNote> Notes => _notes;

    /// <summary>The steps this task must follow, fixed when it first started.</summary>
    public IReadOnlyList<ProcedureStep> Procedure => _procedure;

    public IReadOnlyList<ProcedureStep> MissingSteps => [.. _procedure.Where(s => !s.IsDone)];

    /// <summary>How many times the agent was reminded of missing steps since it last started.</summary>
    public int ProcedureReminders { get; private set; }

    /// <summary>The agent's session and workspace, once the task has started at least once.</summary>
    public AgentRun? Run { get; private set; }

    /// <summary>The larger request this task is part of, if any.</summary>
    public EpicId? EpicId { get; private set; }

    /// <summary>The branch the accepted work was merged into, if it was.</summary>
    public string? MergedInto { get; private set; }

    /// <summary>
    /// Tasks of the same office this one builds on: it stays in the queue until each of them
    /// is accepted and merged, so its agent starts from their work.
    /// </summary>
    public IReadOnlyList<WorkTaskId> DependsOn => [.. _dependsOn.Select(id => new WorkTaskId(id))];

    /// <summary>
    /// Whether work that builds on this task can start: accepted and merged, or accepted
    /// with no branch to merge.
    /// </summary>
    public bool IsDoneForOthers => Status == WorkTaskStatus.Done && (MergedInto is not null || Run is null);

    public bool IsClosed => Status is WorkTaskStatus.Done or WorkTaskStatus.Discarded;

    /// <summary>Accepted work on a branch of its own, not merged anywhere yet.</summary>
    public bool AwaitsMerge => Status == WorkTaskStatus.Done && Run is not null && MergedInto is null;

    /// <summary>The pull request the accepted work waits in, while one is open.</summary>
    public string? PullRequestUrl { get; private set; }

    /// <summary>The branch the open pull request merges into.</summary>
    public string? PullRequestTarget { get; private set; }

    /// <summary>
    /// The office's reserve model working on the task in place of its desk's, while the
    /// desk's account is over its limits; null when the desk's own agent works on it.
    /// </summary>
    public string? ReserveModel { get; private set; }

    /// <summary>A task waiting for human approval, possibly without a desk yet.</summary>
    public static WorkTask Propose(
        Office office, string title, string? description, string requestedBy, DateTimeOffset now, Epic? epic = null)
    {
        if (epic is not null && epic.OfficeId != office?.Id)
        {
            throw new DomainException("A task can only be part of an epic of its own office.");
        }

        ArgumentNullException.ThrowIfNull(office);
        var task = new WorkTask(
            WorkTaskId.New(),
            office.Id,
            Guard.NotBlank(title, "Task title", MaxTitleLength),
            description?.Trim() ?? string.Empty,
            Guard.NotBlank(requestedBy, "Requester", 80),
            WorkTaskStatus.Proposed,
            now);
        task.EpicId = epic?.Id;
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
        if (Status == WorkTaskStatus.Done)
        {
            throw InvalidTransition(WorkTaskStatus.Queued);
        }

        if (Status == WorkTaskStatus.Proposed && DeskId is null)
        {
            throw new DomainException("A task needs a desk before it can be queued.");
        }

        MoveTo(WorkTaskStatus.Queued, now);
    }

    /// <summary>Hands a queued task to a reserve model, or back to its desk with null.</summary>
    public void UseReserve(string? model)
    {
        if (Status != WorkTaskStatus.Queued)
        {
            throw new DomainException($"Only a queued task can change model, not a {Status} one.");
        }

        ReserveModel = AgentProfile.ValidModel(model);
    }

    public void Start(DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Queued)
        {
            throw InvalidTransition(WorkTaskStatus.Running);
        }

        ProcedureReminders = 0;
        MoveTo(WorkTaskStatus.Running, now);
    }

    /// <summary>Records where the agent works. Kept across retries so the agent can resume.</summary>
    public void AttachRun(AgentRun run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (Status != WorkTaskStatus.Running)
        {
            throw new DomainException($"Only a running task can get an agent session, not a {Status} one.");
        }

        Run = run;
        Raise(new WorkTaskRunAttached(Id, run.Branch, run.WorkspacePath, now));
    }

    /// <summary>
    /// Fixes the room's procedure on the task when it first starts, so later changes to the
    /// room do not move the goalposts of work already under way.
    /// </summary>
    public void AdoptProcedure(IEnumerable<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (_procedure.Count > 0 || Run is not null)
        {
            return;
        }

        _procedure.AddRange(steps.Select((text, i) => new ProcedureStep(i + 1, text)));
    }

    public void CompleteStep(int number, string evidence, DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Running)
        {
            throw new DomainException($"Procedure steps can only be completed while the task is running, not {Status}.");
        }

        var step = _procedure.Find(s => s.Number == number)
            ?? throw new DomainException(_procedure.Count == 0
                ? "This task has no required procedure."
                : $"There is no step {number}: the procedure has steps 1 to {_procedure.Count}.");
        step.Complete(Guard.NotBlank(evidence, "Evidence", 4000), now);
        Raise(new WorkTaskStepCompleted(Id, step.Number, step.Text, step.Evidence!, now));
    }

    /// <summary>Counts a reminder; returns false once the allowed reminders are used up.</summary>
    public bool RemindOfProcedure(int allowed)
    {
        if (ProcedureReminders >= allowed)
        {
            return false;
        }

        ProcedureReminders++;
        return true;
    }

    public void SubmitForReview(DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Running)
        {
            throw InvalidTransition(WorkTaskStatus.Review);
        }

        if (MissingSteps.Count > 0)
        {
            throw new DomainException($"The required procedure is not complete: step {string.Join(", ", MissingSteps.Select(s => s.Number))} still to do.");
        }

        MoveTo(WorkTaskStatus.Review, now);
    }

    /// <summary>
    /// The agent is stopped before it finished, by a person or because it went in circles:
    /// its work so far goes to review as it is, missing steps included, to be accepted or
    /// sent back with directions.
    /// </summary>
    public void Interrupt(string reason, DateTimeOffset now)
    {
        if (Status != WorkTaskStatus.Running)
        {
            throw InvalidTransition(WorkTaskStatus.Review);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        AddNote(NoteAuthor.System, reason, now);
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
        Round++;

        // The work will change: what was tried by hand must be tried again.
        _manualChecks.ForEach(c => c.Tick(false, now));
        MoveTo(WorkTaskStatus.Queued, now);
    }

    /// <summary>
    /// Lists something a person must try by hand before merging. The same check listed twice
    /// is kept once. Returns its number.
    /// </summary>
    public int AddManualCheck(string what, string? how, DateTimeOffset now)
    {
        if (Status is not (WorkTaskStatus.Running or WorkTaskStatus.Review))
        {
            throw new DomainException($"Checks to try by hand are listed while the work is done or reviewed, not when the task is {Status}.");
        }

        var text = Guard.NotBlank(what, "Check", 300);
        if (_manualChecks.Find(c => string.Equals(c.What, text, StringComparison.OrdinalIgnoreCase)) is { } known)
        {
            return known.Number;
        }

        if (_manualChecks.Count >= MaxManualChecks)
        {
            throw new DomainException($"A task lists at most {MaxManualChecks} checks to try by hand: keep the ones that matter most.");
        }

        var check = new ManualCheck(_manualChecks.Count + 1, text, how?.Trim() is { Length: > 0 } h ? h[..Math.Min(h.Length, 1000)] : string.Empty);
        _manualChecks.Add(check);
        Raise(new WorkTaskCheckListed(Id, check.Number, check.What, now));
        return check.Number;
    }

    /// <summary>A person tried a check by hand, or wants it back on the list.</summary>
    public void TickManualCheck(int number, bool done, DateTimeOffset now)
    {
        if (Status is not (WorkTaskStatus.Review or WorkTaskStatus.Done))
        {
            throw new DomainException($"Checks are tried once the work is in review, not while the task is {Status}.");
        }

        var check = _manualChecks.Find(c => c.Number == number)
            ?? throw new DomainException($"There is no check {number} on this task.");
        check.Tick(done, now);
        Raise(new WorkTaskCheckTicked(Id, number, done, now));
    }

    /// <summary>Counts what the agent spent on the current round.</summary>
    public void RecordUsage(TokenCount tokens, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Input < 0 || tokens.Output < 0 || tokens.CacheRead < 0 || tokens.CacheWrite < 0)
        {
            throw new DomainException("Token counts cannot be negative.");
        }

        var round = _usage.Find(u => u.Round == Round);
        if (round is null)
        {
            round = new RoundUsage(Round);
            _usage.Add(round);
        }

        round.Add(tokens);
        Raise(new WorkTaskTokensSpent(Id, Round, tokens, now));
    }

    public void Discard(DateTimeOffset now) => MoveTo(WorkTaskStatus.Discarded, now);

    /// <summary>
    /// Checks that the work can be merged into <paramref name="targetBranch"/>, before git is
    /// touched, and returns the branch as it will be recorded.
    /// </summary>
    public string CheckMerge(string targetBranch)
    {
        var target = Guard.NotBlank(targetBranch, "Target branch", 200);
        if (Status != WorkTaskStatus.Done || Run is null)
        {
            throw new DomainException("Only accepted work done by an agent can be merged.");
        }

        if (MergedInto is not null)
        {
            throw new DomainException($"This work was already merged into {MergedInto}.");
        }

        if (PullRequestUrl is not null)
        {
            throw new DomainException($"This work waits in a pull request: merge it there ({PullRequestUrl}).");
        }

        return target;
    }

    /// <summary>Records that the accepted work now lives in <paramref name="targetBranch"/>.</summary>
    public void RecordMerge(string targetBranch, DateTimeOffset now)
    {
        MergedInto = CheckMerge(targetBranch);
        Raise(new WorkTaskMerged(Id, MergedInto, now));
    }

    /// <summary>The accepted work went up for review in a pull request instead of a local merge.</summary>
    public void RecordPullRequest(string url, string targetBranch, DateTimeOffset now)
    {
        var target = CheckMerge(targetBranch);
        if (PullRequestUrl is not null)
        {
            throw new DomainException($"This work already waits in a pull request: {PullRequestUrl}");
        }

        PullRequestUrl = Guard.NotBlank(url, "Pull request", 500);
        PullRequestTarget = target;
        Raise(new WorkTaskPullRequestOpened(Id, PullRequestUrl, target, now));
    }

    /// <summary>The pull request was closed without merging: the work can be merged or proposed again.</summary>
    public void ForgetPullRequest()
    {
        PullRequestUrl = null;
        PullRequestTarget = null;
    }

    /// <summary>
    /// Accepted work that conflicts with where it should go goes back to its desk, as a new
    /// round, to be brought up to date; the feedback says what conflicts.
    /// </summary>
    public void ReopenToCatchUp(NoteAuthor author, string feedback, DateTimeOffset now)
    {
        if (!AwaitsMerge)
        {
            throw new DomainException("Only accepted work that is not merged yet can be brought up to date.");
        }

        AddNote(author, feedback, now);
        Round++;
        _manualChecks.ForEach(c => c.Tick(false, now));
        MoveTo(WorkTaskStatus.Queued, now);
    }

    /// <summary>
    /// Sets the tasks this one waits for. Cycles across several tasks are the application's
    /// to check, as it sees all of them.
    /// </summary>
    public void DependOn(IEnumerable<WorkTask> tasks, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (Status is not (WorkTaskStatus.Proposed or WorkTaskStatus.Queued or WorkTaskStatus.Failed))
        {
            throw new DomainException($"What a task waits for is set before it starts, not while it is {Status}.");
        }

        var others = tasks.DistinctBy(t => t.Id).ToList();
        if (others.Any(t => t.Id == Id))
        {
            throw new DomainException("A task cannot wait for itself.");
        }

        if (others.Any(t => t.OfficeId != OfficeId))
        {
            throw new DomainException("A task can only wait for tasks of its own office.");
        }

        if (others.Count > MaxDependencies)
        {
            throw new DomainException($"A task waits for at most {MaxDependencies} others.");
        }

        _dependsOn = [.. others.Select(t => t.Id.Value)];
        Raise(new WorkTaskDependenciesSet(Id, now));
    }

    public void AddNote(NoteAuthor author, string text, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(author);
        var note = new TaskNote(author, Guard.NotBlank(text, "Note", MaxNoteLength), now);
        _notes.Add(note);
        Raise(new WorkTaskNoteAdded(Id, note, now, OfficeId, DeskId));
    }

    private void MoveTo(WorkTaskStatus target, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(target))
        {
            throw InvalidTransition(target);
        }

        var from = Status;
        Status = target;
        StatusChangedAt = now;
        Raise(new WorkTaskStatusChanged(Id, from, target, now, OfficeId, DeskId));
    }

    private DomainException InvalidTransition(WorkTaskStatus target) =>
        new($"A task cannot go from {Status} to {target}.");
}
