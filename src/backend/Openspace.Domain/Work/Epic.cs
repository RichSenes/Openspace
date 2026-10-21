using Openspace.Domain.Common;
using Openspace.Domain.Offices;

namespace Openspace.Domain.Work;

public readonly record struct EpicId(Guid Value)
{
    public static EpicId New() => new(Guid.CreateVersion7());
}

/// <summary>
/// A larger request, as a person asked for it, that was split into tasks. It keeps
/// the original words so the tasks can always be traced back to what was wanted.
/// </summary>
public sealed class Epic : AggregateRoot<EpicId>
{
    private const int MaxRequestLength = 4000;

    private Epic(EpicId id, OfficeId officeId, string title, string request, string requestedBy, DateTimeOffset createdAt)
        : base(id)
    {
        OfficeId = officeId;
        Title = title;
        Request = request;
        RequestedBy = requestedBy;
        CreatedAt = createdAt;
    }

    public OfficeId OfficeId { get; }

    public string Title { get; }

    /// <summary>The request in the words of whoever asked.</summary>
    public string Request { get; }

    public string RequestedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    public static Epic Open(Office office, string title, string request, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(office);
        var epic = new Epic(
            EpicId.New(),
            office.Id,
            Guard.NotBlank(title, "Epic title", 200),
            Guard.NotBlank(request, "Request", MaxRequestLength),
            Guard.NotBlank(requestedBy, "Requester", 80),
            now);
        epic.Raise(new EpicOpened(epic.Id, office.Id, epic.Title, now));
        return epic;
    }
}

public sealed record EpicOpened(EpicId EpicId, OfficeId OfficeId, string Title, DateTimeOffset OccurredAt) : IDomainEvent;
