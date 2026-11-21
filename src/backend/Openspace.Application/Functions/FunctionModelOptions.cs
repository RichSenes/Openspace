namespace Openspace.Application.Functions;

/// <summary>What runs a function: one of the agent engines, or a model on this machine.</summary>
public enum ModelEngine
{
    ClaudeCode,
    OpenCode,

    /// <summary>A model of Settings › Local AI, named <c>local/&lt;id&gt;</c>.</summary>
    Local,
}

/// <summary>The engine and model of one function.</summary>
public sealed class FunctionModel
{
    public ModelEngine Engine { get; set; } = ModelEngine.ClaudeCode;

    /// <summary>A model of that engine; null means the engine's default (for Local, the first model of the list).</summary>
    public string? Model { get; set; }

    /// <summary>
    /// The OpenCode model used when the engine is Claude Code and Claude Code is not on this
    /// machine; null means OpenCode's default. Only Shiori has one.
    /// </summary>
    public string? OpenCodeModel { get; set; }
}

/// <summary>
/// The engine and model of each function of Openspace, set in Settings › Models. The reviewer
/// and the decider are not here: the reviewer still reads the Claude Code and OpenCode
/// settings, the decider is set in Settings › Local AI.
/// </summary>
public sealed class FunctionModelOptions
{
    public const string SectionName = "Models";

    /// <summary>
    /// Shiori, the chat and supervisor. Not read from this section: it is filled in from today's
    /// planner settings (Claude Code supervises, its model, OpenCode's draft model), so the
    /// planner and Shiori can never disagree.
    /// </summary>
    public FunctionModel Shiori { get; set; } = new() { Engine = ModelEngine.ClaudeCode, Model = "opus" };

    /// <summary>Weekly and monthly digests, wiki summaries.</summary>
    public FunctionModel Digests { get; set; } = new() { Engine = ModelEngine.ClaudeCode, Model = "sonnet" };

    /// <summary>Inhabitants' journals, the chronicle, the lines of the simulation: local, so they cost no quota.</summary>
    public FunctionModel Journals { get; set; } = new() { Engine = ModelEngine.Local };

    /// <summary>
    /// OpenCode models (<c>provider/model</c>) the person marks as free: such services may keep
    /// what they are sent, so personal data never goes to them. Names ending in <c>-free</c> or
    /// <c>:free</c> count as free without being listed.
    /// </summary>
    public List<string> FreeModels { get; set; } = [];
}
