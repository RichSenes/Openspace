namespace Openspace.Domain.Offices;

/// <summary>What runs the agent at a desk.</summary>
public enum AgentEngine
{
    /// <summary>An agent of the OpenCode server, with any model OpenCode knows.</summary>
    OpenCode,

    /// <summary>Claude Code on this machine, with the account it is signed in with.</summary>
    ClaudeCode,
}
