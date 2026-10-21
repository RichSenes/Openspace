using Openspace.Domain.Common;
using Openspace.Domain.Roles;

namespace Openspace.Domain.Offices;

public sealed record OfficeOpened(OfficeId OfficeId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OfficeRenamed(OfficeId OfficeId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RoomOpened(OfficeId OfficeId, RoomId RoomId, string Name, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskAdded(OfficeId OfficeId, RoomId RoomId, DeskId DeskId, AgentProfile Agent, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record RoomContextUpdated(OfficeId OfficeId, RoomId RoomId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskReassigned(OfficeId OfficeId, DeskId DeskId, AgentProfile Agent, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record OfficeRepositorySet(OfficeId OfficeId, string Path, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record DeskRoleChanged(OfficeId OfficeId, DeskId DeskId, RoleId? RoleId, DateTimeOffset OccurredAt) : IDomainEvent;
