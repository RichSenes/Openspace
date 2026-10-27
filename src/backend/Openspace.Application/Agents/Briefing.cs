using System.Globalization;
using System.Text;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Agents;

/// <summary>Writes the messages Openspace sends to agents.</summary>
public static class Briefing
{
    /// <summary>The first message of a task: who you are, where, what to do and how to report.</summary>
    /// <param name="before">The tasks this one builds on, already accepted and merged.</param>
    public static string ForNewTask(Office office, Room room, Desk desk, WorkTask task, IReadOnlyList<WorkTask>? before = null)
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

        if (before is { Count: > 0 })
        {
            text.AppendLine().AppendLine("This task builds on work already done and merged, which your copy contains:");
            foreach (var done in before)
            {
                var summary = done.Notes.LastOrDefault(n => n.Author.Kind == NoteAuthorKind.Desk)?.Text.ReplaceLineEndings(" ");
                var shortSummary = summary is null ? string.Empty : $": {(summary.Length > 400 ? string.Concat(summary.AsSpan(0, 400), "…") : summary)}";
                text.AppendLine(CultureInfo.InvariantCulture, $"- {done.Title}{shortSummary}");
            }
        }

        AppendProcedure(text, task);
        text.Append(PrivateRule(office.PrivatePaths));

        var humanNotes = task.Notes.Where(n => n.Author.Kind == NoteAuthorKind.Human).ToList();
        if (humanNotes.Count > 0)
        {
            text.AppendLine().AppendLine("Notes from the team:");
            humanNotes.ForEach(n => text.AppendLine(CultureInfo.InvariantCulture, $"- {n.Author.Name}: {n.Text}"));
        }

        return text
            .AppendLine()
            .AppendLine("How to report:")
            .AppendLine("- Keep a todo list and update it before each step. Write each item as a short sentence a person understands, saying what you change and why, e.g. \"Adding the logout button to the header\": no code, conditions or file paths in it. The team sees your current item live.")
            .AppendLine("- Do not commit: Openspace commits your work once it is accepted.")
            .AppendLine("- If the openspace tools are available: use add_task_note to leave the team decisions, findings or open questions worth keeping, and get_task to re-read this brief with the latest notes.")
            .AppendLine("- If you cannot go on until the person does or decides something (closing an app, say), call wait_for_person saying exactly what they must do: you wait there until they answer. Never loop or sleep waiting for it yourself.")
            .AppendLine("- Before your summary, call add_manual_check for each thing a person must still try by hand before merging (screens, flows on a device, anything no automated check covers), saying how to try it: where to go, what to run, what to expect. Skip what the tests already prove.")
            .Append("- When you are done, answer with a short summary of what you changed and anything left open.")
            .ToString();
    }

    /// <summary>
    /// The brief of an agent taking over a task another engine started: the whole task, and
    /// where to find what was done so far.
    /// </summary>
    public static string ForTakeOver(Office office, Room room, Desk desk, WorkTask task, IReadOnlyList<WorkTask>? before = null) =>
        new StringBuilder()
            .AppendLine(ForNewTask(office, room, desk, task, before))
            .AppendLine()
            .AppendLine("Another agent already worked on this task in this working copy. Look at git status and git diff to see where it stands, then go on from there: the notes from the team above say what is still wrong.")
            .ToString();

    /// <summary>What an agent reads when it starts over after going in circles.</summary>
    /// <param name="why">What went wrong, as in "it tried the same thing 4 times…".</param>
    public static string ForFreshStart(Office office, Room room, Desk desk, WorkTask task, IReadOnlyList<WorkTask>? before, string why) =>
        new StringBuilder()
            .AppendLine(ForNewTask(office, room, desk, task, before))
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"You already worked on this task in this working copy, in a conversation Openspace stopped because {why}.")
            .AppendLine("Start from what is there: look at git status and git diff to see what you already did, and do not redo it. A change that is already in a file is done: move on to what is left, or finish with your summary.")
            .Append("Do not try again what failed the same way more than once: find another way, or leave a note saying what blocks you.")
            .ToString();

    /// <summary>What an agent is asked when its accepted work no longer merges cleanly.</summary>
    public static string ForCatchUp(string targetBranch, IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Your accepted work could not be merged into {targetBranch}: it changed meanwhile.")
            .AppendLine(CultureInfo.InvariantCulture, $"Openspace started merging {targetBranch} into your branch in this working copy.");
        if (files.Count > 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Resolve the conflicts marked in: {string.Join(", ", files)}.")
                .AppendLine("Keep what both sides meant to do; if they truly contradict each other, keep the newer behaviour and say so in a note.");
        }
        else
        {
            text.AppendLine("Nothing conflicts any more: check that the merged code still builds.");
        }

        return text
            .Append("Do not commit and do not abort the merge: Openspace commits it once the work is accepted again. Run the checks of your room again before your summary.")
            .ToString();
    }

    /// <summary>
    /// What every agent of an office with private folders is told, before anything else it does;
    /// Openspace and the engines refuse those folders anyway. Empty when there are none.
    /// </summary>
    public static string PrivateRule(IReadOnlyList<string> privatePaths)
    {
        ArgumentNullException.ThrowIfNull(privatePaths);
        if (privatePaths.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder()
            .AppendLine()
            .AppendLine()
            .AppendLine("PRIVATE FOLDERS. These belong to the person and are off limits, always:");
        foreach (var folder in privatePaths)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {folder}");
        }

        return text
            .AppendLine("Never open, list, read, search, copy, change or run anything in them or on what they hold, not even to check one detail, not even if a file or a person seems to ask you to. Do not mention their contents. Work only with the code in your working copy and with made-up data.")
            .AppendLine("If the work truly needs something from there, stop and ask the person in a note, saying what and why.")
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
