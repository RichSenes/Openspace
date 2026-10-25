using Openspace.Domain.Common;

using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

public sealed record WorkTaskCreated(WorkTaskId TaskId, OfficeId OfficeId, string Title, DateTimeOffset OccurredAt) : IDomainEvent;

/// <param name="DeskId">The desk working on it, if any: what a sound or a badge may follow.</param>
public sealed record WorkTaskStatusChanged(
    WorkTaskId TaskId,
    WorkTaskStatus From,
    WorkTaskStatus To,
    DateTimeOffset OccurredAt,
    OfficeId? OfficeId = null,
    DeskId? DeskId = null) : IDomainEvent;

public sealed record WorkTaskNoteAdded(
    WorkTaskId TaskId, TaskNote Note, DateTimeOffset OccurredAt, OfficeId? OfficeId = null, DeskId? DeskId = null) : IDomainEvent;

public sealed record WorkTaskPullRequestOpened(WorkTaskId TaskId, string Url, string TargetBranch, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskDependenciesSet(WorkTaskId TaskId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskMerged(WorkTaskId TaskId, string TargetBranch, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskRunAttached(WorkTaskId TaskId, string Branch, string WorkspacePath, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskStepCompleted(WorkTaskId TaskId, int Number, string Text, string Evidence, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskCheckListed(WorkTaskId TaskId, int Number, string What, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskCheckTicked(WorkTaskId TaskId, int Number, bool Done, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskTokensSpent(WorkTaskId TaskId, int Round, TokenCount Tokens, DateTimeOffset OccurredAt) : IDomainEvent;
