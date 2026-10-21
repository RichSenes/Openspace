using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed record EpicView(Guid Id, Guid OfficeId, string Title, string Request, string RequestedBy, DateTimeOffset CreatedAt)
{
    public static EpicView From(Epic epic)
    {
        ArgumentNullException.ThrowIfNull(epic);
        return new(epic.Id.Value, epic.OfficeId.Value, epic.Title, epic.Request, epic.RequestedBy, epic.CreatedAt);
    }
}

/// <summary>What the supervisor produced: the epic and its proposed tasks.</summary>
public sealed record PlanView(EpicView Epic, IReadOnlyList<WorkTaskView> Tasks);
