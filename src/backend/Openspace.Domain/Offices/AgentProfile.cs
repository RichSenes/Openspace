using System.Text.RegularExpressions;
using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>
/// Which OpenCode agent sits at a desk, and optionally which model it uses.
/// The agent itself (prompt, tools, permissions) is defined in OpenCode.
/// </summary>
public sealed partial record AgentProfile
{
    private AgentProfile(string agentName, string? model)
    {
        AgentName = agentName;
        Model = model;
    }

    /// <summary>OpenCode agent identifier, e.g. <c>frontend-dev</c>.</summary>
    public string AgentName { get; }

    /// <summary>Model override in OpenCode's <c>provider/model</c> form; null means the agent's default.</summary>
    public string? Model { get; }

    public static AgentProfile Create(string agentName, string? model = null)
    {
        var name = Guard.NotBlank(agentName, "Agent name", 64);
        if (!AgentNamePattern().IsMatch(name))
        {
            throw new DomainException(
                $"Agent name '{name}' must use lowercase letters, digits and dashes only.");
        }

        var trimmedModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (trimmedModel is not null && !trimmedModel.Contains('/', StringComparison.Ordinal))
        {
            throw new DomainException($"Model '{trimmedModel}' must be in the form provider/model.");
        }

        return new AgentProfile(name, trimmedModel);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex AgentNamePattern();
}
