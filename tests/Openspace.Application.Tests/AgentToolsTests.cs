using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class AgentToolsTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> RunningTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public async Task Announcing_updates_the_bubble_and_the_log()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().AnnounceAsync(task.Id, "  Adding the logout button to the header  ", default);

        Assert.Equal("Adding the logout button to the header", _h.Activity.ForTask(task.Id.Value)?.Step);
        Assert.Equal((AuditKind.Step, "Adding the logout button to the header"), (_h.Audit.Items[^1].Kind, _h.Audit.Items[^1].Summary));
    }

    [Fact]
    public async Task A_note_from_the_tools_is_signed_by_the_desk()
    {
        var task = await RunningTaskAsync();

        await _h.Signals().NoteAsync(task.Id, "The header template is shared with the admin area.", default);

        var note = task.Notes[^1];
        Assert.Equal((NoteAuthorKind.Desk, (Domain.Offices.DeskId?)_h.Desk.Id), (note.Author.Kind, note.Author.DeskId));
    }

    [Fact]
    public async Task Describing_the_task_gives_the_brief_with_the_latest_notes()
    {
        var task = await RunningTaskAsync();
        await _h.Commands().AddNoteAsync(task.Id, "darlene", "Use the red icon.", default);

        var text = await _h.Signals().DescribeAsync(task.Id, default);

        Assert.Contains("Add a logout button", text, StringComparison.Ordinal);
        Assert.Contains("darlene: Use the red icon.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tools_refuse_tasks_that_are_not_being_worked_on()
    {
        var queued = _h.Enqueue();

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.Signals().NoteAsync(queued.Id, "Hello", default));

        Assert.Equal("task-not-running", error.Code);
    }

    [Fact]
    public async Task Empty_text_is_refused()
    {
        var task = await RunningTaskAsync();

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.Signals().AnnounceAsync(task.Id, "   ", default));

        Assert.Equal("empty-text", error.Code);
    }
}
