using Openspace.Domain.Offices;
using Openspace.Domain.Roles;

namespace Openspace.Application.Abstractions;

/// <summary>
/// Everything that decides how an agent behaves in a session: which engine and agent
/// run it, with which model, following which instructions, allowed to do what.
/// Built from a desk and its role.
/// </summary>
/// <param name="PrivatePaths">Folders the agent may never touch: its engine is told to refuse them.</param>
public sealed record AgentSetup(
    string AgentName,
    string? Model,
    string? Instructions,
    RolePermissions? Permissions,
    AgentEngine Engine = AgentEngine.OpenCode,
    IReadOnlyList<string>? PrivatePaths = null);
