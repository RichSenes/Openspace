namespace Openspace.Application.Shiori;

/// <summary>
/// The names of Shiori's own Openspace tools, and which of them each mode allows. The engine is
/// given only the mode's list, and every tool checks the mode again when called, so a switch
/// counts even in the middle of an answer.
/// </summary>
public static class ShioriTools
{
    /// <summary>The header that carries a session's secret to Openspace's MCP server.</summary>
    public const string Header = "X-Openspace-Shiori";

    public const string SearchBrain = "search_brain";
    public const string ReadNote = "read_note";
    public const string ListOffices = "list_offices";
    public const string Whiteboard = "whiteboard";
    public const string ProposeDecision = "propose_decision";
    public const string AcceptDecision = "accept_decision";
    public const string PlanWork = "plan_work";
    public const string AssignTask = "assign_task";

    /// <summary>Every tool of Shiori's: the MCP server shows these to Shiori's sessions and nothing else to them.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SearchBrain, ReadNote, ListOffices, Whiteboard, ProposeDecision, AcceptDecision, PlanWork, AssignTask,
    };

    /// <summary>Reading the brain and the offices, and the session's own whiteboard: allowed in every mode.</summary>
    private static readonly string[] Reading = [SearchBrain, ReadNote, ListOffices, Whiteboard];

    public static IReadOnlyList<string> For(ShioriMode mode) => mode switch
    {
        ShioriMode.Decide => [.. Reading, ProposeDecision, AcceptDecision],
        ShioriMode.Organise => [.. Reading, AssignTask, PlanWork],
        _ => Reading,
    };

    public static bool Allows(ShioriMode mode, string tool) => For(mode).Contains(tool, StringComparer.Ordinal);
}
