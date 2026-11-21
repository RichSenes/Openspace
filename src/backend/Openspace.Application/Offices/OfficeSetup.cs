using System.Text.Json;
using System.Text.Json.Nodes;
using Openspace.Application.Abstractions;
using Openspace.Application.Routines;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Routines;

namespace Openspace.Application.Offices;

/// <summary>
/// How an office is set up, as a file to keep or share: its rooms with their desks, the roles
/// those desks use, its routines and its own settings. Never what is the person's alone: no
/// repository, no private folders, no Google account, no key, no task and nothing of the code.
/// </summary>
/// <param name="Format">Always <see cref="FormatName"/>: tells this file from any other JSON.</param>
public sealed record OfficeSetup(
    string Format,
    int Version,
    string Name,
    string? Theme,
    bool LocalOnly,
    bool HoldOverlappingWork,
    IReadOnlyList<string>? ReserveModels,
    IReadOnlyList<RoleSetup>? Roles,
    IReadOnlyList<RoomSetup>? Rooms,
    IReadOnlyList<RoutineSetup>? Routines)
{
    public const string FormatName = "openspace-office";

    public const int CurrentVersion = 1;
}

public sealed record RoleSetup(
    string Name, string? Description, string? Instructions, string? Model, RolePermissions? Permissions, IReadOnlyList<string>? Skills);

public sealed record RoomSetup(string Name, string? Context, IReadOnlyList<string>? Procedure, string? Check, string? Mood, IReadOnlyList<DeskSetup>? Desks);

/// <param name="Role">The role's name, among the file's roles or the ones already here; null for a plain agent.</param>
public sealed record DeskSetup(string Name, string AgentName, string? Model, AgentEngine Engine = AgentEngine.OpenCode, string? Role = null);

/// <param name="Settings">The kind's settings as a JSON object; null for the kind's defaults.</param>
public sealed record RoutineSetup(string Name, string Kind, ScheduleView Schedule, JsonElement? Settings = null, bool Enabled = true, bool AskFirst = false);

/// <summary>Writes an office's setup to a file, and opens a new office from one.</summary>
public sealed class OfficeSetups(
    IOfficeRepository offices,
    IRoleRepository roles,
    IRoutineRepository routines,
    IEnumerable<IRoutineKind> kinds,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Routine settings that name the person's own things: left out of the file.</summary>
    internal static readonly string[] PersonalSettings = ["account"];

    public async Task<OfficeSetup> ExportAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var allRoles = await roles.ListAsync(cancellationToken).ConfigureAwait(false);
        var work = office.Rooms.Where(r => r.Kind == RoomKind.Work).ToList();
        var used = work.SelectMany(r => r.Desks).Select(d => d.RoleId).Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        var roleName = allRoles.ToDictionary(r => r.Id, r => r.Name);
        var officeRoutines = await routines.ListAsync(officeId, cancellationToken).ConfigureAwait(false);

        return new OfficeSetup(
            OfficeSetup.FormatName,
            OfficeSetup.CurrentVersion,
            office.Name,
            office.Theme,
            office.LocalOnly,
            office.HoldOverlappingWork,
            [.. office.ReserveModels],
            [
                .. allRoles.Where(r => used.Contains(r.Id)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new RoleSetup(r.Name, r.Description, r.Instructions, r.Model, r.Permissions, r.Skills is null ? null : [.. r.Skills])),
            ],
            [
                .. work.Select(r => new RoomSetup(
                    r.Name,
                    r.Context,
                    [.. r.Procedure],
                    r.Check,
                    r.Mood,
                    [
                        .. r.Desks.Select(d => new DeskSetup(
                            d.Name, d.Agent.AgentName, d.Agent.Model, d.Agent.Engine, d.RoleId is { } id && roleName.TryGetValue(id, out var name) ? name : null)),
                    ])),
            ],
            [
                .. officeRoutines.OrderBy(r => r.CreatedAt).Select(r => new RoutineSetup(
                    r.Name,
                    r.Kind,
                    ScheduleView.Of(r.Schedule) with { Since = null },
                    WithoutPersonal(r.Settings),
                    r.Enabled,
                    r.AskFirst)),
            ]);
    }

    /// <summary>
    /// Opens a new office from a setup, all of it or nothing: roles already here with the same
    /// name are used as they are, the others are added.
    /// </summary>
    /// <param name="name">The new office's name; the file's when null or blank.</param>
    public async Task<OfficeId> ImportAsync(OfficeSetup setup, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (!string.Equals(setup.Format, OfficeSetup.FormatName, StringComparison.Ordinal))
        {
            throw new RejectedException("setup-format", "This is not an office file made by Openspace.");
        }

        if (setup.Version is < 1 or > OfficeSetup.CurrentVersion)
        {
            throw new RejectedException("setup-version", $"This office file is version {setup.Version}; this Openspace reads up to version {OfficeSetup.CurrentVersion}. Update Openspace first.");
        }

        var now = clock.GetUtcNow();
        var office = Office.Open(string.IsNullOrWhiteSpace(name) ? setup.Name : name, now);
        if (!string.IsNullOrWhiteSpace(setup.Theme))
        {
            office.ChangeTheme(setup.Theme, now);
        }

        office.KeepLocal(setup.LocalOnly, now);
        office.HoldOverlaps(setup.HoldOverlappingWork, now);
        office.SetReserveModels(setup.ReserveModels ?? [], now);

        var known = (await roles.ListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        var added = new List<Role>();
        foreach (var r in setup.Roles ?? [])
        {
            if (known.ContainsKey(r.Name?.Trim() ?? string.Empty))
            {
                continue;
            }

            var role = Role.Define(r.Name!, r.Description, r.Instructions, r.Model, r.Permissions ?? RolePermissions.Default, now);
            if (r.Skills is not null)
            {
                role.UseSkills(r.Skills, now);
            }

            known[role.Name] = role.Id;
            added.Add(role);
        }

        foreach (var r in setup.Rooms ?? [])
        {
            var room = office.OpenRoom(r.Name, now);
            office.UpdateRoomContext(room.Id, r.Context, now);
            office.SetRoomProcedure(room.Id, r.Procedure ?? [], now);
            office.SetRoomCheck(room.Id, r.Check, now);
            office.ChangeRoomMood(room.Id, r.Mood, now);
            foreach (var d in r.Desks ?? [])
            {
                RoleId? roleId = null;
                if (!string.IsNullOrWhiteSpace(d.Role))
                {
                    roleId = known.TryGetValue(d.Role.Trim(), out var id)
                        ? id
                        : throw new RejectedException("setup-role-missing", $"Desk '{d.Name}' uses the role '{d.Role}', which is neither in the file nor here.");
                }

                office.AddDesk(room.Id, d.Name, AgentProfile.Create(d.AgentName, d.Model, d.Engine), now, roleId);
            }
        }

        var made = new List<Routine>();
        foreach (var r in setup.Routines ?? [])
        {
            var kind = kinds.FirstOrDefault(k => k.Key == r.Kind?.Trim())
                ?? throw new RejectedException("setup-routine-kind", $"Routine '{r.Name}' is of a kind this Openspace does not know ('{r.Kind}').");
            if (r.Settings is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null and not JsonValueKind.Undefined })
            {
                throw new DomainException($"The settings of routine '{r.Name}' must be a JSON object.");
            }

            var settings = r.Settings is { ValueKind: JsonValueKind.Object } given ? given.GetRawText() : kind.DefaultSettings;
            var routine = Routine.Create(office, r.Name, kind.Key, (r.Schedule ?? throw new DomainException($"Routine '{r.Name}' has no schedule.")).ToSchedule(), settings, now, clock.LocalTimeZone);
            if (!r.Enabled)
            {
                routine.TurnOn(false, now, clock.LocalTimeZone);
            }

            routine.AskBeforeActing(r.AskFirst, now);
            made.Add(routine);
        }

        if (made.Count > 0)
        {
            office.RoutinesRoom(RoutineCommands.RoomName, now);
        }

        // Only now, when everything was accepted: a file refused halfway leaves nothing behind.
        foreach (var role in added)
        {
            roles.Add(role);
        }

        offices.Add(office);
        foreach (var routine in made)
        {
            routines.Add(routine);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return office.Id;
    }

    internal static JsonElement WithoutPersonal(string settings)
    {
        var node = JsonNode.Parse(settings) as JsonObject ?? [];
        foreach (var key in PersonalSettings)
        {
            node.Remove(key);
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
