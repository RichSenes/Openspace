using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

internal static class IdConverters
{
    internal sealed class OfficeIdConverter() : ValueConverter<OfficeId, Guid>(id => id.Value, value => new OfficeId(value));

    internal sealed class RoomIdConverter() : ValueConverter<RoomId, Guid>(id => id.Value, value => new RoomId(value));

    internal sealed class DeskIdConverter() : ValueConverter<DeskId, Guid>(id => id.Value, value => new DeskId(value));

    internal sealed class RoleIdConverter() : ValueConverter<RoleId, Guid>(id => id.Value, value => new RoleId(value));

    internal sealed class WorkTaskIdConverter() : ValueConverter<WorkTaskId, Guid>(id => id.Value, value => new WorkTaskId(value));
}
