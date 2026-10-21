namespace Openspace.Domain.Work;

public readonly record struct WorkTaskId(Guid Value)
{
    public static WorkTaskId New() => new(Guid.CreateVersion7());
}
