using Openspace.Domain.Offices;
using Openspace.Domain.Roles;

namespace Openspace.Application.Abstractions;

/// <summary>
/// Everything that decides how an agent behaves in a session: which engine and agent
/// run it, with which model, following which instructions, allowed to do what.
/// Built from a desk and its role.
/// </summary>
public sealed record AgentSetup(
    string AgentName, string? Model, string? Instructions, RolePermissions? Permissions, AgentEngine Engine = AgentEngine.OpenCode);
