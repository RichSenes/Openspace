namespace Openspace.Domain.Common;

/// <summary>Lets infrastructure collect events from any aggregate, whatever its id type.</summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
