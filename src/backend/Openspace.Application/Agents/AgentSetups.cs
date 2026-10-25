using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>Builds the setup of a desk's agent from the desk and its role.</summary>
public sealed class AgentSetups(IRoleRepository roles)
{
    /// <summary>The OpenCode agent a Claude Code desk's task goes on with, on a reserve model.</summary>
    public const string ReserveAgent = "build";

    /// <summary>
    /// The desk's engine, agent and model, completed by its role: the desk's own model wins
    /// over the role's. Role models are OpenCode's, so Claude Code desks only use their own.
    /// </summary>
    /// <param name="privatePaths">The office's private folders, which the agent's engine must refuse.</param>
    public Task<AgentSetup> ForDeskAsync(Desk desk, CancellationToken cancellationToken, IReadOnlyList<string>? privatePaths = null)
    {
        ArgumentNullException.ThrowIfNull(desk);
        return SetupAsync(desk, desk.Agent, privatePaths, cancellationToken);
    }

    /// <summary>
    /// The setup of the agent working on a task: the desk's, or one on the task's reserve
    /// model, with the same role, instructions and private folders.
    /// </summary>
    public Task<AgentSetup> ForTaskAsync(Desk desk, WorkTask task, CancellationToken cancellationToken, IReadOnlyList<string>? privatePaths = null)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(task);
        return SetupAsync(desk, AgentFor(desk, task), privatePaths, cancellationToken);
    }

    /// <summary>
    /// Who works on the task: the desk's agent, or an OpenCode one on the task's reserve model.
    /// A Claude Code desk has no OpenCode agent of its own, so it lends its role to OpenCode's.
    /// </summary>
    public static AgentProfile AgentFor(Desk desk, WorkTask task)
    {
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(task);
        return task.ReserveModel is not { } model
            ? desk.Agent
            : AgentProfile.Create(desk.Agent.Engine == AgentEngine.OpenCode ? desk.Agent.AgentName : ReserveAgent, model);
    }

    private async Task<AgentSetup> SetupAsync(
        Desk desk, AgentProfile agent, IReadOnlyList<string>? privatePaths, CancellationToken cancellationToken)
    {
        var role = desk.RoleId is { } id ? await roles.FindAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return new AgentSetup(
            agent.AgentName,
            agent.Engine == AgentEngine.ClaudeCode ? agent.Model : agent.Model ?? role?.Model,
            string.IsNullOrEmpty(role?.Instructions) ? null : role.Instructions,
            role?.Permissions,
            agent.Engine,
            privatePaths);
    }
}
