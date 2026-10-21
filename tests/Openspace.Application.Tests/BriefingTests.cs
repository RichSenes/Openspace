using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class BriefingTests
{
    private readonly Harness _h = new();

    [Fact]
    public void A_new_task_brief_names_the_desk_the_rules_the_task_and_how_to_report()
    {
        var task = _h.Enqueue();
        task.AddNote(NoteAuthor.Human("darlene"), "Reuse the existing icon.", Harness.Start);

        var text = Briefing.ForNewTask(_h.Office, _h.Room, _h.Desk, task);

        Assert.Contains("\"Frontend\"", text, StringComparison.Ordinal);
        Assert.Contains("Never touch the payments module.", text, StringComparison.Ordinal);
        Assert.Contains("Top right corner.", text, StringComparison.Ordinal);
        Assert.Contains("darlene: Reuse the existing icon.", text, StringComparison.Ordinal);
        Assert.Contains("todo list", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resuming_after_review_quotes_only_the_new_feedback()
    {
        var task = _h.Enqueue();
        task.AddNote(NoteAuthor.Human("elliot"), "Old remark.", Harness.Start);
        task.AddNote(NoteAuthor.FromDesk(_h.Desk.Id), "Added the button.", Harness.Start);
        task.AddNote(NoteAuthor.Human("elliot"), "Make it red.", Harness.Start);

        var text = Briefing.ForResume(task);

        Assert.Contains("Make it red.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Old remark.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resuming_without_feedback_asks_to_continue()
    {
        var task = _h.Enqueue();
        task.AddNote(NoteAuthor.System, "Openspace restarted.", Harness.Start);

        Assert.Contains("continue", Briefing.ForResume(task), StringComparison.Ordinal);
    }
}
