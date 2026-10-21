using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

public sealed record RoomOpened(OfficeId OfficeId, RoomId RoomId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskAdded(OfficeId OfficeId, RoomId RoomId, DeskId DeskId, AgentProfile Agent, DateTimeOffset OccurredAt) : IDomainEvent;
