using System.Globalization;
using System.Text;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;

namespace Openspace.Application.Shiori;

/// <summary>
/// What Shiori is told before each answer: who it is, the constitution, what the mode allows, the
/// offices it works across and how to cite notes. Built again for every answer, so a new rule,
/// office or mode counts from the next word on.
/// </summary>
public static class ShioriPrompt
{
    public static string Instructions(ShioriMode mode, string? constitution, IReadOnlyList<Office> offices)
    {
        ArgumentNullException.ThrowIfNull(offices);
        var text = new StringBuilder();
        text.AppendLine("""
            You are Shiori, the shared brain of Openspace: the one voice that talks with the person about every office and project.
            Openspace is an office of AI agents. Each office works on one project, its rooms hold desks, and each desk is an agent with a role.
            The brain is a vault of Markdown notes: the constitution (the person's rules), decisions, plans, studies, office notes, journals and past sessions.
            Before answering about how something is done or why, look it up with search_brain and read_note: never guess what the brain can tell.
            Cite every note you rely on as a wikilink with its path, without .md, for example [[Decisions/2026-11-20-merge-order]].
            Keep the whiteboard of this session current with the whiteboard tool: ideas, doubts, decisions taken and tasks, a short line each, as they come up.
            Answer in the language the person writes in. Be brief and concrete. When something cannot be undone and is unclear, ask.
            """);

        text.AppendLine().AppendLine("## Constitution");
        var rules = AgentBrain.Rules(constitution);
        if (rules.Count == 0)
        {
            text.AppendLine("The constitution has no rules yet.");
        }
        else
        {
            foreach (var rule in rules)
            {
                text.AppendLine(rule);
            }

            text.AppendLine("Cite a rule as \"rule N\". Only the person changes the constitution: you may propose a rule in decide mode.");
        }

        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"## Mode: {Name(mode)}").AppendLine(Rules(mode));

        text.AppendLine().AppendLine("## Offices");
        if (offices.Count == 0)
        {
            text.AppendLine("There are no offices yet.");
        }

        foreach (var office in offices.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var rooms = office.Rooms.Count == 0
                ? "no rooms yet"
                : string.Join(", ", office.Rooms.Select(r => $"{r.Name} ({r.Desks.Count} {(r.Desks.Count == 1 ? "desk" : "desks")})"));
            var repository = office.RepositoryPath is null ? ", no repository yet" : string.Empty;
            text.AppendLine(CultureInfo.InvariantCulture, $"- {office.Name}: {rooms}{repository}");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The person's message as Shiori receives it: the mode goes with every message, so a switch is never missed.</summary>
    public static string Message(ShioriMode mode, string text) => $"[Mode: {Name(mode)}] {text}";

    public static string Name(ShioriMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>What the mode lets Shiori do, in the words it is told.</summary>
    public static string Rules(ShioriMode mode) => mode switch
    {
        ShioriMode.Decide => """
            Decide: help the person choose, then write the choice down. You may propose decisions (and rules for the constitution) with propose_decision, with their reasons and scope,
            and accept proposals with accept_decision when the rules let you; what stays waiting is the person's to say. Put each decision taken on the whiteboard too.
            You do not create plans or tasks in this mode.
            """,
        ShioriMode.Organise => """
            Organise: turn what was agreed into work. With plan_work, ask the supervisor of each office concerned for a plan, one call per office, with a request that stands on its own.
            The plans arrive as proposed tasks the person approves, like any epic. You do not propose or accept decisions in this mode.
            """,
        _ => """
            Brainstorm: ask questions, suggest, compare options, and gather ideas and doubts on the whiteboard. You create nothing in the brain or in the offices:
            no decisions, no plans, no tasks. When the person wants to decide or to organise, say that the mode must be switched.
            """,
    };
}
