namespace Openspace.Domain.Common;

/// <summary>Something meaningful that happened in the domain.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
