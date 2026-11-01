using System.Text.RegularExpressions;
using Openspace.Domain.Common;

namespace Openspace.Domain.Offices;

/// <summary>
/// What runs the agent at a desk: an OpenCode agent, or Claude Code, and optionally
/// which model it uses. An OpenCode agent itself (prompt, tools) is defined in OpenCode.
/// </summary>
public sealed partial record AgentProfile
{
    private AgentProfile(string agentName, string? model, AgentEngine engine)
    {
        AgentName = agentName;
        Model = model;
        Engine = engine;
    }

    public AgentEngine Engine { get; }

    /// <summary>OpenCode agent identifier, e.g. <c>frontend-dev</c>.</summary>
    public string AgentName { get; }

    /// <summary>
    /// Model override: OpenCode's <c>provider/model</c>, or a Claude Code model such as
    /// <c>sonnet</c> or <c>claude-sonnet-5-5</c>; null means the engine's default.
    /// </summary>
    public string? Model { get; }

    public static AgentProfile Create(string agentName, string? model = null, AgentEngine engine = AgentEngine.OpenCode)
    {
        if (!Enum.IsDefined(engine))
        {
            throw new DomainException("Unknown agent engine.");
        }

        var name = Guard.NotBlank(agentName, "Agent name", 64);
        if (!AgentNamePattern().IsMatch(name))
        {
            throw new DomainException(
                $"Agent name '{name}' must use lowercase letters, digits and dashes only.");
        }

        return new AgentProfile(name, engine == AgentEngine.ClaudeCode ? ValidClaudeModel(model) : ValidModel(model), engine);
    }

    /// <summary>How a reserve on Claude Code is written, before its model: <c>claude-code/sonnet</c>.</summary>
    public const string ClaudeReserve = "claude-code/";

    /// <summary>
    /// A trimmed reserve model, or null when none is given: OpenCode's <c>provider/model</c>,
    /// or Claude Code's <c>claude-code/</c> followed by one of its models.
    /// </summary>
    public static string? ValidReserve(string? reserve)
    {
        var trimmed = string.IsNullOrWhiteSpace(reserve) ? null : reserve.Trim();
        if (trimmed is null || !trimmed.StartsWith(ClaudeReserve, StringComparison.Ordinal))
        {
            return ValidModel(trimmed);
        }

        return ValidClaudeModel(trimmed[ClaudeReserve.Length..]) is { } model
            ? ClaudeReserve + model
            : throw new DomainException($"Reserve '{trimmed}' needs a Claude model after {ClaudeReserve}, such as {ClaudeReserve}sonnet.");
    }

    /// <summary>The agent a reserve model runs as: Claude Code for <c>claude-code/</c> ones, OpenCode's agent otherwise.</summary>
    public static AgentProfile ForReserve(string reserve, string agentName)
    {
        var valid = ValidReserve(reserve) ?? throw new DomainException("A reserve model is needed.");
        return valid.StartsWith(ClaudeReserve, StringComparison.Ordinal)
            ? Create(agentName, valid[ClaudeReserve.Length..], AgentEngine.ClaudeCode)
            : Create(agentName, valid);
    }

    /// <summary>A trimmed Claude Code model name or alias, or null when none is given.</summary>
    internal static string? ValidClaudeModel(string? model)
    {
        var trimmed = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        if (trimmed is not null && !ClaudeModelPattern().IsMatch(trimmed))
        {
            throw new DomainException($"Model '{trimmed}' is not a Claude model name such as sonnet or claude-sonnet-5-5.");
        }

        return trimmed;
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

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]*(\[[a-z0-9]+\])?$")]
    private static partial Regex ClaudeModelPattern();
}
