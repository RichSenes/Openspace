using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Application.Agents;

/// <summary>Builds the setup of a desk's agent from the desk and its role.</summary>
public sealed class AgentSetups(IRoleRepository roles)
{
    /// <summary>
    /// The desk's engine, agent and model, completed by its role: the desk's own model wins
    /// over the role's. Role models are OpenCode's, so Claude Code desks only use their own.
    /// </summary>
    /// <param name="privatePaths">The office's private folders, which the agent's engine must refuse.</param>
    public async Task<AgentSetup> ForDeskAsync(Desk desk, CancellationToken cancellationToken, IReadOnlyList<string>? privatePaths = null)
    {
        ArgumentNullException.ThrowIfNull(desk);
        var role = desk.RoleId is { } id ? await roles.FindAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return new AgentSetup(
            desk.Agent.AgentName,
            desk.Agent.Engine == AgentEngine.ClaudeCode ? desk.Agent.Model : desk.Agent.Model ?? role?.Model,
            string.IsNullOrEmpty(role?.Instructions) ? null : role.Instructions,
            role?.Permissions,
            desk.Agent.Engine,
            privatePaths);
    }
}
