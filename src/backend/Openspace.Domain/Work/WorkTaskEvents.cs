using Openspace.Domain.Common;

using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

public sealed record WorkTaskCreated(WorkTaskId TaskId, OfficeId OfficeId, string Title, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskStatusChanged(
    WorkTaskId TaskId,
    WorkTaskStatus From,
    WorkTaskStatus To,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskNoteAdded(WorkTaskId TaskId, TaskNote Note, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskMerged(WorkTaskId TaskId, string TargetBranch, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskRunAttached(WorkTaskId TaskId, string Branch, string WorkspacePath, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record WorkTaskStepCompleted(WorkTaskId TaskId, int Number, string Text, string Evidence, DateTimeOffset OccurredAt) : IDomainEvent;
