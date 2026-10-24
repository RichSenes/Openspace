using Openspace.Application.Agents;
using Openspace.Application.Review;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class ReviewTests
{
    private readonly Harness _h = new();

    /// <summary>A task whose agent just finished: it waits for review.</summary>
    private async Task<WorkTask> FinishedTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().FinishedAsync("ses_1", default);
        return task;
    }

    [Fact]
    public async Task Work_reaching_review_waits_for_the_reviewer()
    {
        var task = await FinishedTaskAsync();

        await foreach (var waiting in _h.Reviews.ReadAllAsync(default))
        {
            Assert.Equal(task.Id, waiting);
            break;
        }
    }

    [Fact]
    public async Task The_reviewer_leaves_its_verdict_first_then_its_findings()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.Review = """{"verdict":"changes","summary":"The button works but is untested.","findings":[{"file":"header.php","problem":"No test covers the logout link."}]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        var note = task.Notes[^1];
        Assert.Equal((NoteAuthorKind.Reviewer, TaskReviewer.Name), (note.Author.Kind, note.Author.Name));
        Assert.StartsWith("Changes suggested. The button works but is untested.", note.Text, StringComparison.Ordinal);
        Assert.Contains("- header.php: No test covers the logout link.", note.Text, StringComparison.Ordinal);
        Assert.Equal(WorkTaskStatus.Review, task.Status);
        Assert.Contains(_h.Audit.Items, e => e.Summary == $"Reviewed by {TaskReviewer.Name}");
    }

    [Fact]
    public async Task An_ok_with_findings_is_not_an_ok()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.Review = """{"verdict":"ok","summary":"Fine.","findings":[{"file":"a.cs","problem":"Typo in a comment."}]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        Assert.StartsWith("Changes suggested.", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_work_is_marked_ready_to_accept()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.Review = """{"verdict":"ok","summary":"Small and tested.","findings":[]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        Assert.Equal("OK: ready to accept. Small and tested.", task.Notes[^1].Text);
    }

    [Fact]
    public async Task Without_a_reviewer_nothing_happens()
    {
        var task = await FinishedTaskAsync();
        var notes = task.Notes.Count;
        _h.Runtime.Review = null;

        await _h.Reviewer().ReviewAsync(task.Id, default);

        Assert.Equal(notes, task.Notes.Count);
    }

    [Fact]
    public async Task A_review_that_cannot_run_says_so_and_leaves_the_work_to_you()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.FailOnReview = new InvalidOperationException("Claude Code stopped: usage limit reached");

        await _h.Reviewer().ReviewAsync(task.Id, default);

        var note = task.Notes[^1];
        Assert.Equal(NoteAuthorKind.System, note.Author.Kind);
        Assert.Contains("usage limit reached", note.Text, StringComparison.Ordinal);
        Assert.Equal(WorkTaskStatus.Review, task.Status);
    }

    [Fact]
    public async Task Work_accepted_meanwhile_is_not_reviewed()
    {
        var task = await FinishedTaskAsync();
        task.Accept(_h.Clock.GetUtcNow());
        _h.Runtime.Review = """{"verdict":"ok","summary":"Fine.","findings":[]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        Assert.Empty(_h.Runtime.ReviewRequests);
    }

    [Fact]
    public async Task The_reviewer_reads_the_request_and_the_reported_procedure()
    {
        _h.Office.SetRoomProcedure(_h.Room.Id, ["Run the tests"], Harness.Start);
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        task.CompleteStep(1, "dotnet test: 12 passed", _h.Clock.GetUtcNow());
        await _h.Signals().FinishedAsync("ses_1", default);
        _h.Runtime.Review = """{"verdict":"ok","summary":"Fine.","findings":[]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        var (folder, brief) = Assert.Single(_h.Runtime.ReviewRequests);
        Assert.Equal(task.Run!.WorkspacePath, folder);
        Assert.Contains("Task: Add a logout button", brief, StringComparison.Ordinal);
        Assert.Contains("1. Run the tests: done, evidence: dotnet test: 12 passed", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_reviewer_adds_the_checks_to_try_by_hand_that_were_missing()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().AddManualCheckAsync(task.Id, "The button signs you out", "Tap Logout.", default);
        await _h.Signals().FinishedAsync("ses_1", default);
        _h.Runtime.Review = """{"verdict":"ok","summary":"Fine.","findings":[],"manualChecks":[{"what":"The button signs you out","how":"Again."},{"what":"The menu still opens on a phone","how":"Open the site at 360px and tap the menu."}]}""";

        await _h.Reviewer().ReviewAsync(task.Id, default);

        Assert.Equal(["The button signs you out", "The menu still opens on a phone"], task.ManualChecks.Select(c => c.What));
        Assert.Contains("The button signs you out", _h.Runtime.ReviewRequests[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_sent_back_with_the_review_carries_it_to_the_agent()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.Review = """{"verdict":"changes","summary":"Untested.","findings":[{"file":"header.php","problem":"No test."}]}""";
        await _h.Reviewer().ReviewAsync(task.Id, default);

        await _h.Commands().SendBackAsync(task.Id, "elliot", "Also keep it small.", withReview: true, default);

        var feedback = task.Notes[^1];
        Assert.Equal(NoteAuthorKind.Human, feedback.Author.Kind);
        Assert.StartsWith("Also keep it small.", feedback.Text, StringComparison.Ordinal);
        Assert.Contains("- header.php: No test.", feedback.Text, StringComparison.Ordinal);
        Assert.Contains("- header.php: No test.", Briefing.ForResume(task), StringComparison.Ordinal);
        Assert.Equal(2, task.Round);
    }

    [Fact]
    public async Task Work_sent_back_to_a_desk_that_changed_engine_is_taken_over_in_the_same_copy()
    {
        var task = await FinishedTaskAsync();
        var (folder, branch) = (task.Run!.WorkspacePath, task.Run.Branch);
        await _h.OfficeCommands().ChangeDeskAgentAsync(_h.Office.Id, _h.Desk.Id, AgentEngine.ClaudeCode, "build", "sonnet", default);

        await _h.Commands().SendBackAsync(task.Id, "elliot", "The footer link is still missing.", withReview: false, default);
        await _h.Runner().RunQueueAsync(default);

        Assert.NotEqual("ses_1", task.Run!.SessionId);
        Assert.Equal((folder, branch, AgentEngine.ClaudeCode), (task.Run.WorkspacePath, task.Run.Branch, _h.Runtime.EngineOf(task.Run.SessionId)));
        var (sessionId, _, text) = _h.Runtime.Prompts[^1];
        Assert.Equal(task.Run.SessionId, sessionId);
        Assert.Contains("Your task: Add a logout button", text, StringComparison.Ordinal);
        Assert.Contains("The footer link is still missing.", text, StringComparison.Ordinal);
        Assert.Contains("git diff", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_sent_back_to_the_same_engine_keeps_its_session()
    {
        var task = await FinishedTaskAsync();

        await _h.Commands().SendBackAsync(task.Id, "elliot", "One more thing.", withReview: false, default);
        await _h.Runner().RunQueueAsync(default);

        Assert.Equal("ses_1", task.Run!.SessionId);
        Assert.StartsWith("Your work was reviewed and sent back.", _h.Runtime.Prompts[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_old_review_is_not_sent_back_with_new_work()
    {
        var task = await FinishedTaskAsync();
        _h.Runtime.Review = """{"verdict":"changes","summary":"Untested.","findings":[]}""";
        await _h.Reviewer().ReviewAsync(task.Id, default);
        await _h.Commands().SendBackAsync(task.Id, "elliot", null, withReview: true, default);
        await _h.Runner().RunQueueAsync(default);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await _h.Signals().FinishedAsync("ses_1", default);

        await Assert.ThrowsAsync<Domain.Common.DomainException>(() =>
            _h.Commands().SendBackAsync(task.Id, "elliot", null, withReview: true, default));
    }
}

public class ClaudeCodeDeskTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task A_Claude_Code_desk_keeps_its_own_model_and_ignores_the_role_s()
    {
        var role = Domain.Roles.Role.Define("Reviewer", null, "Be strict.", "opencode/big-pickle", new Domain.Roles.RolePermissions(
            Domain.Roles.PermissionLevel.Allow, Domain.Roles.PermissionLevel.Ask, Domain.Roles.PermissionLevel.Deny), Harness.Start);
        _h.Roles.Items.Add(role);
        var desk = _h.Office.AddDesk(_h.Room.Id, "Claude", AgentProfile.Create("build", null, AgentEngine.ClaudeCode), Harness.Start, role.Id);

        var setup = await new AgentSetups(_h.Roles).ForDeskAsync(desk, default);

        Assert.Equal((AgentEngine.ClaudeCode, (string?)null, "Be strict."), (setup.Engine, setup.Model, setup.Instructions));
    }

    [Fact]
    public async Task A_Claude_Code_desk_waits_when_Claude_Code_is_missing()
    {
        await _h.OfficeCommands().ChangeDeskAgentAsync(_h.Office.Id, _h.Desk.Id, AgentEngine.ClaudeCode, "build", "sonnet", default);
        _h.Runtime.ClaudeCode = false;
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("Claude Code is not available", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Equal(("sonnet", AgentEngine.ClaudeCode), (_h.Desk.Agent.Model, _h.Desk.Agent.Engine));
    }

    [Fact]
    public async Task An_agent_asking_through_Openspace_waits_for_the_person()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);

        var asking = _h.Signals().RequestPermissionAsync(task.Id, Fakes.Run("npm test"), default);
        var waiting = Assert.Single(_h.Activity.Snapshot());
        Assert.Equal((ActivityKind.Waiting, "Can I run the tests?"), (waiting.Kind, waiting.Doing));
        Assert.False(asking.IsCompleted);

        Assert.True(_h.Permissions.Answer(waiting.PermissionId!, allow: true));
        Assert.True(await asking);
    }

    [Fact]
    public async Task Stopping_an_agent_refuses_what_it_still_waits_for()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        var asking = _h.Signals().RequestPermissionAsync(task.Id, Fakes.Run("rm -rf build"), default);

        _h.Permissions.RefuseAll(task.Run!.SessionId);

        Assert.False(await asking);
        Assert.Empty(_h.Permissions.Pending(task.Run.WorkspacePath));
    }
}
