using Openspace.Application.Work;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class AuditTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> RunningTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public void A_task_life_reads_as_plain_sentences()
    {
        var task = _h.Enqueue();
        task.Start(Harness.Start);
        task.AttachRun(AgentRun.Create("ses_1", "/srv/worktrees/t1", "openspace/t1"), Harness.Start);
        task.SubmitForReview(Harness.Start);
        task.SendBack(NoteAuthor.Human("elliot"), "Make it red.", Harness.Start);

        var lines = task.DomainEvents.Select(AuditTrail.FromDomainEvent).OfType<AuditEntry>().Select(e => e.Summary).ToList();

        Assert.Equal(
            [
                "Task created", "Queued for its desk", "Agent started working", "Working copy ready on branch openspace/t1",
                "Agent finished: waiting for review", "Note from elliot", "Sent back to the queue",
            ],
            lines);
    }

    [Fact]
    public async Task Finished_tools_are_logged_once_with_paths_relative_to_the_repository()
    {
        var task = await RunningTaskAsync();
        var workspace = task.Run!.WorkspacePath;

        await _h.Signals().ToolFinishedAsync("ses_1", "call_1", AuditKind.Change, "Changed header.php", $"--- {workspace}/theme/header.php", true, default);
        await _h.Signals().ToolFinishedAsync("ses_1", "call_1", AuditKind.Change, "Changed header.php", "again", true, default);

        var entry = Assert.Single(_h.Audit.Items);
        Assert.Equal("--- theme/header.php", entry.Detail);
        Assert.Equal([task.Id.Value], _h.Publisher.LogChanges);
    }

    [Fact]
    public async Task A_new_step_is_logged_but_repeating_it_is_not()
    {
        await RunningTaskAsync();

        await _h.Signals().ProgressAsync("ses_1", "Adding the logout button", null, default);
        await _h.Signals().ProgressAsync("ses_1", "Adding the logout button", "Editing header.php", default);
        await _h.Signals().ProgressAsync("ses_1", "Checking the result", null, default);

        Assert.Equal(["Adding the logout button", "Checking the result"], _h.Audit.Items.Select(e => e.Summary));
    }

    [Fact]
    public async Task Permissions_asked_and_answered_are_logged()
    {
        var task = await RunningTaskAsync();
        await _h.Signals().PermissionAskedAsync("ses_1", "per_1", "bash rm -rf build", default);

        await _h.Commands().ReplyToPermissionAsync(task.Id, allow: false, default);

        Assert.Equal(["Asked permission: bash rm -rf build", "Permission refused"], _h.Audit.Items.Select(e => e.Summary));
        Assert.All(_h.Audit.Items, e => Assert.Equal(AuditKind.Permission, e.Kind));
    }

    [Fact]
    public async Task Signals_from_other_sessions_leave_no_trace()
    {
        await RunningTaskAsync();

        await _h.Signals().ToolFinishedAsync("ses_unknown", "call_9", AuditKind.Command, "Ran rm -rf /", null, true, default);

        Assert.Empty(_h.Audit.Items);
    }

    [Fact]
    public void The_report_lists_changes_commands_and_a_timeline_without_reads()
    {
        var task = _h.Enqueue();
        task.Start(Harness.Start);
        task.AttachRun(AgentRun.Create("ses_1", "/srv/worktrees/t1", "openspace/t1"), Harness.Start);
        var log = new List<AuditEntry>
        {
            AuditEntry.Record(task.Id, Harness.Start, AuditKind.Read, "Read index.html"),
            AuditEntry.Record(task.Id, Harness.Start, AuditKind.Change, "Changed index.html", "+<button>"),
            AuditEntry.Record(task.Id, Harness.Start, AuditKind.Change, "Changed index.html", "+<footer>"),
            AuditEntry.Record(task.Id, Harness.Start, AuditKind.Command, "Ran npm test", succeeded: false),
            AuditEntry.Record(task.Id, Harness.Start, AuditKind.Note, "Note from the agent", "Added the button."),
        };

        var report = TaskReport.Build(_h.Office, task, log);

        Assert.Contains("# Add a logout button", report, StringComparison.Ordinal);
        Assert.Contains("- **Desk:** Web / Frontend (frontend-dev)", report, StringComparison.Ordinal);
        Assert.Contains("- **Branch:** `openspace/t1`", report, StringComparison.Ordinal);
        Assert.Contains("## Files changed\n\n- Changed index.html\n\n", report.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("Ran npm test (failed)", report, StringComparison.Ordinal);
        Assert.Contains("> Added the button.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Read index.html", report, StringComparison.Ordinal);
    }
}
