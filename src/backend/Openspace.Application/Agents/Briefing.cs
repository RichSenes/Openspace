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
            .Append("- When you are done, answer with a short summary of what you changed and anything left open.")
            .ToString();
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
