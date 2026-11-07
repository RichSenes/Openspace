using Openspace.Domain.Offices;
using Openspace.Domain.Roles;

namespace Openspace.Application.Abstractions;

/// <summary>
/// Everything that decides how an agent behaves in a session: which engine and agent
/// run it, with which model, following which instructions, allowed to do what.
/// Built from a desk and its role.
/// </summary>
/// <param name="PrivatePaths">Folders the agent may never touch: its engine is told to refuse them.</param>
/// <param name="Skills">The skills the agent may load, by name; null for every installed skill.</param>
/// <param name="OutputFolder">A folder outside its copy the agent may also write in; null for none.</param>
public sealed record AgentSetup(
    string AgentName,
    string? Model,
    string? Instructions,
    RolePermissions? Permissions,
    AgentEngine Engine = AgentEngine.OpenCode,
    IReadOnlyList<string>? PrivatePaths = null,
    IReadOnlyList<string>? Skills = null,
    string? OutputFolder = null);
