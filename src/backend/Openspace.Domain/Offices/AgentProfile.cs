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

        return new AgentProfile(name, ValidModel(model));
    }

    /// <summary>A trimmed <c>provider/model</c>, or null when none is given.</summary>
    internal static string? ValidModel(string? model)
    {
        var trimmed = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        var slash = trimmed?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        if (trimmed is not null && (slash <= 0 || slash == trimmed.Length - 1))
        {
            throw new DomainException($"Model '{trimmed}' must be in the form provider/model.");
        }

        return trimmed;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex AgentNamePattern();
}
