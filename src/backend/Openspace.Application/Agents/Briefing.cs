using System.Globalization;
using System.Text;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>Writes the messages Openspace sends to agents.</summary>
public static class Briefing
{
    /// <summary>The first message of a task: who you are, where, what to do and how to report.</summary>
    public static string ForNewTask(Office office, Room room, Desk desk, WorkTask task)
    {
        ArgumentNullException.ThrowIfNull(office);
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(desk);
        ArgumentNullException.ThrowIfNull(task);

        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"You work at the desk \"{desk.Name}\" in the room \"{room.Name}\" of the office \"{office.Name}\".")
            .AppendLine("You are in a private working copy of the repository: change files only here.");

        if (room.Context.Length > 0)
        {
            text.AppendLine().AppendLine("Rules of this room:").AppendLine(room.Context);
        }

        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Your task: {task.Title}");
        if (task.Description.Length > 0)
        {
            text.AppendLine(task.Description);
        }

        AppendProcedure(text, task);

        var humanNotes = task.Notes.Where(n => n.Author.Kind == NoteAuthorKind.Human).ToList();
        if (humanNotes.Count > 0)
        {
            text.AppendLine().AppendLine("Notes from the team:");
            humanNotes.ForEach(n => text.AppendLine(CultureInfo.InvariantCulture, $"- {n.Author.Name}: {n.Text}"));
        }

        return text
            .AppendLine()
            .AppendLine("How to report:")
            .AppendLine("- Keep a todo list and update it before each step. Write each item as a short sentence a person understands, saying what you change and why, e.g. \"Adding the logout button to the header\". The team sees your current item live.")
            .AppendLine("- Do not commit: Openspace commits your work once it is accepted.")
            .AppendLine("- If the openspace tools are available: use add_task_note to leave the team decisions, findings or open questions worth keeping, and get_task to re-read this brief with the latest notes.")
            .Append("- When you are done, answer with a short summary of what you changed and anything left open.")
            .ToString();
    }

    /// <summary>The message sent when an agent stops with required steps still to do.</summary>
    public static string ForMissingSteps(WorkTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var text = new StringBuilder()
            .AppendLine("You stopped, but the required procedure is not complete. Openspace will not send your work to review until it is.")
            .AppendLine("Still to do:");
        foreach (var step in task.MissingSteps)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{step.Number}. {step.Text}");
        }

        return text
            .Append("Do them now, report each one with the openspace tool complete_step (step number and evidence), then answer with a short summary.")
            .ToString();
    }

    private static void AppendProcedure(StringBuilder text, WorkTask task)
    {
        if (task.Procedure.Count == 0)
        {
            return;
        }

        text.AppendLine().AppendLine("Required procedure: Openspace checks it before your work goes to review.");
        foreach (var step in task.Procedure)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{step.Number}. {step.Text}{(step.IsDone ? " (done)" : string.Empty)}");
        }

        text.AppendLine("When you finish a step, call the openspace tool complete_step with its number and the evidence: files, commands, results.");
    }

    /// <summary>
    /// The message that makes an agent pick a task up again in the same session.
    /// Whatever people wrote after the agent's last note is treated as feedback.
    /// </summary>
    public static string ForResume(WorkTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var feedback = task.Notes
            .Reverse()
            .TakeWhile(n => n.Author.Kind == NoteAuthorKind.Human)
            .Reverse()
            .ToList();

        if (feedback.Count == 0)
        {
            return "Openspace was interrupted while you worked on this task. Check where you were and continue. When you are done, answer with a short summary.";
        }

        var text = new StringBuilder().AppendLine("Your work was reviewed and sent back. Feedback:");
        feedback.ForEach(n => text.AppendLine(CultureInfo.InvariantCulture, $"- {n.Author.Name}: {n.Text}"));
        return text
            .Append("Address it, keep your todo list updated, and answer with a short summary when done.")
            .ToString();
    }
}
