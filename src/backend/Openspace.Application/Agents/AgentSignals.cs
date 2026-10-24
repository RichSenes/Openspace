using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>
/// Reacts to what agents report while they work. Signals for sessions Openspace
/// did not start (e.g. someone using OpenCode directly) are ignored.
/// </summary>
public sealed partial class AgentSignals(
    IOfficeRepository offices,
    AgentSetups setups,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    ActivityBoard activity,
    IAuditLog audit,
    IActivityPublisher publisher,
    Review.PendingReviews reviews,
    PermissionRequests permissions,
    IOptionsMonitor<AgentOptions> agentOptions,
    EngineLimits limits,
    TimeProvider clock)
{
    /// <param name="step">The agent's current todo item, if this update is about its plan.</param>
    /// <param name="doing">The concrete action, if this update is about a tool.</param>
    public async Task ProgressAsync(string sessionId, string? step, string? doing, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        await ShowProgressAsync(task, step, doing, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent said, through the Openspace tools, what it is doing.</summary>
    public async Task AnnounceAsync(WorkTaskId taskId, string text, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var step = Guard(text, "Announcement", 200);
        await ShowProgressAsync(task, step, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent left a note on its task, through the Openspace tools.</summary>
    public async Task NoteAsync(WorkTaskId taskId, string text, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        task.AddNote(NoteAuthor.FromDesk(task.DeskId!.Value), Guard(text, "Note", 4000), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The agent listed something a person must try by hand, through the Openspace tools.</summary>
    public async Task<int> AddManualCheckAsync(WorkTaskId taskId, string what, string? how, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var number = task.AddManualCheck(what, how, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return number;
    }

    /// <summary>
    /// An engine reported the tokens a session spent. They count on the task's current round,
    /// whatever its status: the report may arrive just after the work went to review.
    /// </summary>
    public async Task UsedAsync(string sessionId, TokenCount tokens, CancellationToken cancellationToken)
    {
        var task = await tasks.FindBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null || tokens.Total == 0)
        {
            return;
        }

        task.RecordUsage(tokens, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The brief again, with the notes written since: what the agent reads to get its bearings.</summary>
    public async Task<string> DescribeAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var (room, desk) = office.Locate(task.DeskId!.Value);
        return Briefing.ForNewTask(office, room, desk, task);
    }

    /// <summary>A tool call ended: it goes in the task's log, once, with its evidence.</summary>
    public async Task ToolFinishedAsync(
        string sessionId,
        string callId,
        AuditKind kind,
        string summary,
        string? detail,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null || await audit.ContainsAsync(task.Id, callId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // Paths inside the private working copy read better relative to the repository.
        var workspace = task.Run!.WorkspacePath.TrimEnd('/') + "/";
        var relative = detail?.Replace(workspace, string.Empty, StringComparison.Ordinal);
        await LogAsync(
            AuditEntry.Record(task.Id, clock.GetUtcNow(), kind, summary, relative, succeeded, callId),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// OpenCode holds a request for the agent. Openspace answers it on its own when it may;
    /// otherwise the desk waits for a person.
    /// </summary>
    public async Task PermissionAskedAsync(
        string sessionId, string permissionId, AgentAction action, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        var privatePaths = await PrivatePathsAsync(task, cancellationToken).ConfigureAwait(false);
        if (PermissionPolicy.PrivateFolder(action, privatePaths) is { } folder)
        {
            await runtime.ReplyToPermissionAsync(permissionId, task.Run!.WorkspacePath, false, cancellationToken).ConfigureAwait(false);
            await RefusePrivateAsync(task, action, folder, permissionId, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (MayAllowOnItsOwn(task, action, privatePaths.Count > 0))
        {
            await runtime.ReplyToPermissionAsync(permissionId, task.Run!.WorkspacePath, true, cancellationToken).ConfigureAwait(false);
            await LogAsync(AllowedOnItsOwn(task, action, permissionId), cancellationToken).ConfigureAwait(false);
            return;
        }

        await LogAsync(Asked(task, action, permissionId, privatePaths.Count > 0), cancellationToken).ConfigureAwait(false);
        await ShowWaitingAsync(task, permissionId, action, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The agent asked, through the Openspace tools, to do something its role does not allow
    /// outright. Openspace answers on its own when it may; otherwise it waits until a person
    /// answers in Openspace. True allows it once.
    /// </summary>
    public async Task<bool> RequestPermissionAsync(WorkTaskId taskId, AgentAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        var privatePaths = await PrivatePathsAsync(task, cancellationToken).ConfigureAwait(false);
        if (PermissionPolicy.PrivateFolder(action, privatePaths) is { } folder)
        {
            await RefusePrivateAsync(task, action, folder, null, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (MayAllowOnItsOwn(task, action, privatePaths.Count > 0))
        {
            await LogAsync(AllowedOnItsOwn(task, action, null), cancellationToken).ConfigureAwait(false);
            return true;
        }

        var id = permissions.Open(task.Run!.SessionId, task.Run.WorkspacePath, action with { Target = Guard(action.Target, "Permission", 2000) });
        await LogAsync(Asked(task, action, id, privatePaths.Count > 0), cancellationToken).ConfigureAwait(false);

        // Another request of the same agent may be on show: this one waits its turn.
        if (activity.ForTask(task.Id.Value) is not { Kind: ActivityKind.Waiting })
        {
            await ShowWaitingAsync(task, id, action, cancellationToken).ConfigureAwait(false);
        }

        return await permissions.WaitAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> PrivatePathsAsync(WorkTask task, CancellationToken cancellationToken) =>
        (await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false)).PrivatePaths;

    /// <summary>A request naming a private folder: refused at once, and said so on the task.</summary>
    private async Task RefusePrivateAsync(WorkTask task, AgentAction action, string folder, string? permissionId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        task.AddNote(NoteAuthor.System, $"Refused on its own: the agent asked to {ActionPhrases.Request(action)}, which touches the private folder {folder}. Nobody was asked: private folders are never opened.", now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await LogAsync(
            AuditEntry.Record(task.Id, now, AuditKind.Permission, $"Refused on its own: {ActionPhrases.Request(action)}", $"Private folder: {folder}", externalId: permissionId),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether Openspace answers this request without a person: in auto mode, unless it is risky.</summary>
    /// <param name="guarded">The office has private folders: commands climbing above the copy need a person too.</param>
    public bool MayAllowOnItsOwn(WorkTask task, AgentAction action, bool guarded = false)
    {
        ArgumentNullException.ThrowIfNull(task);
        return agentOptions.CurrentValue.Autonomy == Autonomy.Auto
            && PermissionPolicy.NeedsPerson(action, task.Run!.WorkspacePath, guarded) is null;
    }

    /// <summary>A permission was answered in OpenCode itself.</summary>
    public async Task PermissionRepliedAsync(string sessionId, string permissionId, bool allowed, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is not null)
        {
            await PermissionAnsweredAsync(task, permissionId, allowed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After an answer the agent works again, unless its engine holds more requests for the
    /// same session that need a person: then the next one is shown. The engine is the source
    /// of truth here; requests Openspace may answer on its own are answered as they arrive.
    /// </summary>
    public async Task PermissionAnsweredAsync(WorkTask task, string permissionId, bool allowed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        var now = clock.GetUtcNow();
        await activity.ResumeAsync(task.Id.Value, permissionId, allowed ? "Permission granted" : "Permission refused", now, cancellationToken)
            .ConfigureAwait(false);

        var pending = await runtime.PendingPermissionsAsync(task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
        var guarded = (await PrivatePathsAsync(task, cancellationToken).ConfigureAwait(false)).Count > 0;
        if (pending.FirstOrDefault(p => p.SessionId == task.Run.SessionId && p.Id != permissionId && !MayAllowOnItsOwn(task, p.Action, guarded)) is { } next)
        {
            await ShowWaitingAsync(task, next.Id, next.Action, cancellationToken).ConfigureAwait(false);
        }
    }

    private AuditEntry Asked(WorkTask task, AgentAction action, string permissionId, bool guarded) =>
        AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Permission, $"Asked to {ActionPhrases.Request(action)}", Reason(task, action, guarded), externalId: permissionId);

    private AuditEntry AllowedOnItsOwn(WorkTask task, AgentAction action, string? permissionId) =>
        AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Permission, $"Allowed on its own: {ActionPhrases.Request(action)}", action.Target, externalId: permissionId);

    /// <summary>Why a person is asked: in auto mode, what makes the request risky.</summary>
    private string? Reason(WorkTask task, AgentAction action, bool guarded) =>
        agentOptions.CurrentValue.Autonomy == Autonomy.Auto && PermissionPolicy.NeedsPerson(action, task.Run!.WorkspacePath, guarded) is { } why
            ? $"{action.Target}\nA person decides because {why}."
            : action.Target;

    private Task ShowWaitingAsync(WorkTask task, string permissionId, AgentAction action, CancellationToken cancellationToken) =>
        activity.UpdateAsync(
            new DeskActivity(task.OfficeId.Value, task.DeskId!.Value.Value, task.Id.Value, ActivityKind.Waiting, null,
                ActionPhrases.Asking(action), permissionId, clock.GetUtcNow(), ActionPhrases.Shorten(action.Target, 300)),
            cancellationToken);

    /// <summary>The agent stopped talking: its work goes to review with its summary as a note.</summary>
    public async Task FinishedAsync(string sessionId, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var summary = await runtime.GetLastReplyAsync(sessionId, task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(summary))
        {
            task.AddNote(NoteAuthor.FromDesk(task.DeskId!.Value), summary, now);
        }

        if (task.MissingSteps.Count > 0)
        {
            await HandleMissingStepsAsync(task, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        task.SubmitForReview(now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Stopped, null, "Done: waiting for review", null, cancellationToken).ConfigureAwait(false);
        reviews.Enqueue(task.Id);
    }

    /// <summary>How many times an agent is sent back to its missing steps before the task fails.</summary>
    public const int ProcedureReminders = 2;

    /// <summary>
    /// The agent stopped with required steps missing: remind it, a couple of times, then
    /// fail the task so a person looks at it. Work never reaches review without them.
    /// </summary>
    private async Task HandleMissingStepsAsync(WorkTask task, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var missing = string.Join(", ", task.MissingSteps.Select(s => s.Number));
        if (!task.RemindOfProcedure(ProcedureReminders))
        {
            task.Fail($"The required procedure was not completed (step {missing} still to do) after {ProcedureReminders} reminders.", now);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await ShowAsync(task, ActivityKind.Stopped, null, "Stuck: procedure not completed", null, cancellationToken).ConfigureAwait(false);
            return;
        }

        task.AddNote(NoteAuthor.System, $"The agent stopped with step {missing} of the procedure still to do: reminded it ({task.ProcedureReminders} of {ProcedureReminders}).", now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var office = await offices.GetAsync(task.OfficeId, cancellationToken).ConfigureAwait(false);
        var (_, desk) = office.Locate(task.DeskId!.Value);
        var setup = await setups.ForDeskAsync(desk, cancellationToken, office.PrivatePaths).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Working, null, "Back to the required procedure", null, cancellationToken).ConfigureAwait(false);
        await runtime.PromptAsync(task.Run!.SessionId, task.Run.WorkspacePath, setup, Briefing.ForMissingSteps(task), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The agent reported a procedure step as done, through the Openspace tools.</summary>
    public async Task CompleteStepAsync(WorkTaskId taskId, int number, string evidence, CancellationToken cancellationToken)
    {
        var task = await WorkingTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        task.CompleteStep(number, evidence?.Trim() ?? string.Empty, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The agent's account is over its usage limits: the task goes back in the queue, the
    /// session stops retrying, and the desks on that account wait until <paramref name="until"/>.
    /// </summary>
    /// <param name="scope">The account, as <see cref="EngineLimits"/> names it.</param>
    public async Task OutOfLimitsAsync(
        string sessionId, string scope, DateTimeOffset? until, string reason, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        limits.Pause(scope, until);
        var now = clock.GetUtcNow();
        var resumes = limits.PausedUntil(scope) ?? now;
        task.AddNote(
            NoteAuthor.System,
            $"{reason} Back in the queue: it starts again by itself after {resumes.ToLocalTime():HH:mm}, where it left off.",
            now);
        task.Requeue(now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await activity.ClearTaskAsync(task.Id.Value, now, cancellationToken).ConfigureAwait(false);

        // OpenCode would keep retrying until the reset: the queue starts it again instead.
        await runtime.AbortAsync(sessionId, task.Run!.WorkspacePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task FailedAsync(string sessionId, string reason, CancellationToken cancellationToken)
    {
        var task = await RunningTaskAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            return;
        }

        task.Fail(reason, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ShowAsync(task, ActivityKind.Stopped, null, "Stuck: needs a look", null, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShowProgressAsync(WorkTask task, string? step, string? doing, CancellationToken cancellationToken)
    {
        if (step is not null && activity.ForTask(task.Id.Value)?.Step != step)
        {
            await LogAsync(AuditEntry.Record(task.Id, clock.GetUtcNow(), AuditKind.Step, step), cancellationToken).ConfigureAwait(false);
        }

        await ShowAsync(task, ActivityKind.Working, step, doing, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tools only act on a task while its agent is working on it.</summary>
    private async Task<WorkTask> WorkingTaskAsync(WorkTaskId taskId, CancellationToken cancellationToken)
    {
        var task = await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false);
        return task is { Status: WorkTaskStatus.Running, DeskId: not null, Run: not null }
            ? task
            : throw new RejectedException("task-not-running", $"Task '{task.Title}' is {task.Status}: Openspace tools only work while its agent is working on it.");
    }

    private static string Guard(string? text, string what, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new RejectedException("empty-text", $"{what} cannot be empty.");
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        audit.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await publisher.LogChangedAsync(entry.TaskId.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkTask?> RunningTaskAsync(string sessionId, CancellationToken cancellationToken)
    {
        var task = await tasks.FindBySessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        return task is { Status: WorkTaskStatus.Running, DeskId: not null } ? task : null;
    }

    private Task ShowAsync(
        WorkTask task, ActivityKind kind, string? step, string? doing, string? permissionId, CancellationToken cancellationToken) =>
        activity.UpdateAsync(
            new DeskActivity(task.OfficeId.Value, task.DeskId!.Value.Value, task.Id.Value, kind, step, doing, permissionId, clock.GetUtcNow()),
            cancellationToken);
}
