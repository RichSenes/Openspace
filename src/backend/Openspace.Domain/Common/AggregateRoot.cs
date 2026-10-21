namespace Openspace.Domain.Common;

/// <summary>
/// Consistency boundary. Collects domain events until the application layer
/// publishes them after a successful save.
/// </summary>
public abstract class AggregateRoot<TId>(TId id) : IHasDomainEvents
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; } = id;

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
