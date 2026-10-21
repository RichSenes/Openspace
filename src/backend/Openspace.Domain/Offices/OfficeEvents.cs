using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

public sealed record OfficeOpened(OfficeId OfficeId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OfficeRenamed(OfficeId OfficeId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RoomOpened(OfficeId OfficeId, RoomId RoomId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskAdded(OfficeId OfficeId, RoomId RoomId, DeskId DeskId, AgentProfile Agent, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RoomContextUpdated(OfficeId OfficeId, RoomId RoomId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskReassigned(OfficeId OfficeId, DeskId DeskId, AgentProfile Agent, DateTimeOffset OccurredAt) : IDomainEvent;
