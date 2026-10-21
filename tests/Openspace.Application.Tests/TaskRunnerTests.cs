using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class TaskRunnerTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task A_started_task_gets_a_workspace_a_session_and_a_brief()
    {
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Equal("ses_1", task.Run?.SessionId);
        Assert.Equal(_h.Workspaces.Prepared[0].Path, task.Run?.WorkspacePath);
        var prompt = Assert.Single(_h.Runtime.Prompts);
        Assert.Equal("frontend-dev", prompt.Setup.AgentName);
        Assert.Contains("Add a logout button", prompt.Text, StringComparison.Ordinal);
        Assert.Contains("Never touch the payments module.", prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_desk_shows_it_is_starting()
    {
        _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        var activity = Assert.Single(_h.Activity.Snapshot());
        Assert.Equal((_h.Desk.Id.Value, ActivityKind.Working), (activity.DeskId, activity.Kind));
    }

    [Fact]
    public async Task Without_a_repository_the_task_fails_with_a_clear_reason()
    {
        var h = new Harness(withRepository: false);
        var task = h.Enqueue();

        await h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("no repository", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Empty(h.Workspaces.Prepared);
    }

    [Fact]
    public async Task An_unknown_OpenCode_agent_fails_the_task()
    {
        _h.Runtime.Agents.Clear();
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("no agent named 'frontend-dev'", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runtime_error_fails_the_task_instead_of_leaving_it_running()
    {
        _h.Runtime.FailOnPrompt = new HttpRequestException("connection refused");
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("connection refused", task.Notes[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_task_sent_back_resumes_in_the_same_session_with_the_feedback()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().FinishedAsync("ses_1", default);
        await _h.Commands().SendBackAsync(task.Id, "elliot", "Make it red.", default);

        await _h.Runner().RunQueueAsync(default);

        Assert.Single(_h.Workspaces.Prepared);
        Assert.Equal(2, _h.Runtime.Prompts.Count);
        Assert.Equal("ses_1", _h.Runtime.Prompts[1].SessionId);
        Assert.Contains("Make it red.", _h.Runtime.Prompts[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovery_puts_running_tasks_back_in_the_queue_with_a_note()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);

        await _h.Runner().RecoverAsync(default);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(NoteAuthorKind.System, task.Notes[^1].Author.Kind);
        Assert.NotNull(task.Run);
    }

    [Fact]
    public async Task A_repository_that_broke_since_it_was_set_fails_the_task_before_any_git_work()
    {
        _h.Workspaces.Problem = new Abstractions.RepositoryProblem("not-a-repository", "'/srv/repos/e-corp' is not a git repository yet.", true);
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Failed, task.Status);
        Assert.Contains("is not a git repository yet", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Empty(_h.Workspaces.Prepared);
    }

    [Fact]
    public async Task The_agent_gets_the_Openspace_tools_for_its_own_task()
    {
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal((task.Run!.WorkspacePath, task.Id.Value), Assert.Single(_h.Runtime.ToolConnections));
    }

    [Fact]
    public async Task Without_the_tools_the_agent_still_starts()
    {
        _h.Runtime.FailOnConnect = new HttpRequestException("mcp unavailable");
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Single(_h.Runtime.Prompts);
    }

    [Fact]
    public async Task A_desk_role_brings_its_instructions_permissions_and_model()
    {
        var careful = new Domain.Roles.RolePermissions(Domain.Roles.PermissionLevel.Ask, Domain.Roles.PermissionLevel.Deny, Domain.Roles.PermissionLevel.Deny);
        var role = Domain.Roles.Role.Define("Auditor", null, "Never change code.", "opencode/careful", careful, Harness.Start);
        _h.Roles.Add(role);
        _h.Office.SetDeskRole(_h.Desk.Id, role.Id, Harness.Start);
        _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        var setup = Assert.Single(_h.Runtime.Prompts).Setup;
        Assert.Equal(("frontend-dev", "opencode/careful", "Never change code.", careful), (setup.AgentName, setup.Model, setup.Instructions, setup.Permissions));
        Assert.Equal(careful, Assert.Single(_h.Runtime.SessionSetups).Permissions);
    }

    [Fact]
    public async Task The_desk_model_wins_over_the_role_model_and_no_role_means_plain_OpenCode()
    {
        var role = Domain.Roles.Role.Define("Writer", null, null, "opencode/role-model", Domain.Roles.RolePermissions.Default, Harness.Start);
        _h.Roles.Add(role);
        var room = _h.Office.Rooms[0];
        var pinned = _h.Office.AddDesk(room.Id, "Pinned", Domain.Offices.AgentProfile.Create("build", "opencode/desk-model"), Harness.Start, role.Id);
        _h.Tasks.Add(WorkTask.Assign(_h.Office, pinned.Id, "With role", null, "elliot", Harness.Start));
        _h.Enqueue("Without role");

        await _h.Runner(seats: 2).RunQueueAsync(default);

        var byAgent = _h.Runtime.Prompts.ToDictionary(p => p.Setup.AgentName, p => p.Setup);
        Assert.Equal("opencode/desk-model", byAgent["build"].Model);
        Assert.Null(byAgent["build"].Instructions);
        Assert.Equal((null, null, null), (byAgent["frontend-dev"].Model, byAgent["frontend-dev"].Instructions, byAgent["frontend-dev"].Permissions));
    }
}
