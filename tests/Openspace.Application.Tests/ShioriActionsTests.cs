using System.Text.Json;
using Openspace.Application.Shiori;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>Shiori's tools, as the MCP server calls them for a session.</summary>
public class ShioriActionsTests
{
    private readonly ShioriHarness _s = new();
    private readonly ShioriSessions _sessions;
    private readonly ShioriActions _actions;

    public ShioriActionsTests()
    {
        _sessions = _s.Sessions();
        _actions = _s.Actions(_sessions);
    }

    private async Task<(Guid Id, string Token)> OpenAsync(ShioriMode mode)
    {
        var session = await _sessions.CreateAsync("Merge order", mode, "elliot", default);
        return (session.Id, await _s.TokenAsync(session.Id));
    }

    private async Task DrainAsync()
    {
        foreach (var job in _s.Jobs.Snapshot())
        {
            await _s.Jobs.Completion(job.Id);
        }
    }

    private static ShioriProposals.Draft Decision(string title, params string[] offices) =>
        new("decision", title, "Tasks merge in the order they were approved.", "Fewer conflicts.", offices, [], []);

    [Fact]
    public async Task Brainstorm_reads_the_brain_but_cannot_propose_accept_or_plan()
    {
        _s.H.Vault.Put("Decisions/2026-11-20-merge-order.md", "---\nkind: decision\nstatus: accepted\n---\n# Merge order\n\nApproval order.\n");
        var (_, token) = await OpenAsync(ShioriMode.Brainstorm);

        var found = await _actions.SearchAsync(token, "merge", null, null, default);
        var read = await _actions.ReadAsync(token, "Decisions/2026-11-20-merge-order", default);
        var propose = await Assert.ThrowsAsync<RejectedException>(() => _actions.ProposeAsync(token, Decision("Merge in approval order"), null, default));
        var accept = await Assert.ThrowsAsync<RejectedException>(() => _actions.AcceptAsync(token, "Decisions/2026-11-20-merge-order.md", default));
        var plan = await Assert.ThrowsAsync<RejectedException>(() => _actions.PlanAsync(token, "E Corp", "Add a logout button", default));

        Assert.Contains("Decisions/2026-11-20-merge-order.md · decision, accepted · Merge order", found, StringComparison.Ordinal);
        Assert.Contains("Approval order.", read, StringComparison.Ordinal);
        Assert.Equal([ShioriActions.ModeCode, ShioriActions.ModeCode, ShioriActions.ModeCode], [propose.Code, accept.Code, plan.Code]);
        Assert.Contains("brainstorm mode", propose.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_s.H.Vault.Notes.Keys, k => k.StartsWith("Decisions/2015", StringComparison.Ordinal));
        Assert.Empty(_s.H.Runtime.PlanRequests);
    }

    [Fact]
    public async Task Decide_proposes_a_decision_signed_by_Shiori_from_the_session()
    {
        var (id, token) = await OpenAsync(ShioriMode.Decide);
        await _sessions.ChangeBoardAsync(id, new(WhiteboardAction.Add, Kind: WhiteboardKind.Decision, Text: "Merge in approval order"), default);

        var said = await _actions.ProposeAsync(token, Decision("Merge in approval order", "E Corp"), "w1", default);

        const string path = "Decisions/2015-06-24-merge-in-approval-order.md";
        Assert.Contains(path, said, StringComparison.Ordinal);
        var note = _s.H.Vault.Notes[path];
        Assert.Equal(("decision", "proposed", "Shiori", $"session {id}"), (note.Kind, note.Status, note.Author, note.From));
        Assert.Equal(["E Corp"], note.Offices);
        Assert.Contains("## Reasons\n\nFewer conflicts.", note.Body, StringComparison.Ordinal);

        // The item is linked, so ending the session does not propose the same decision again.
        var ending = await _sessions.EndAsync(id, default);
        Assert.Empty(ending.Proposed);
        Assert.Equal(path, ending.Session.Board.Single().Note);
    }

    [Fact]
    public async Task Decide_accepts_a_proposal_within_one_office_and_leaves_one_across_offices_to_the_person()
    {
        _s.H.Vault.Put("Decisions/2026-11-20-merge-order.md", "---\nkind: decision\nstatus: proposed\noffices: [E Corp]\nauthor: Frontend\n---\n# Merge order\n");
        _s.H.Vault.Put("Decisions/2026-11-20-one-linter.md", "---\nkind: decision\nstatus: proposed\noffices: [E Corp, Allsafe]\n---\n# One linter\n");
        var (_, token) = await OpenAsync(ShioriMode.Decide);

        await _actions.AcceptAsync(token, "Decisions/2026-11-20-merge-order.md", default);
        var waits = await Assert.ThrowsAsync<RejectedException>(() => _actions.AcceptAsync(token, "Decisions/2026-11-20-one-linter.md", default));
        var again = await Assert.ThrowsAsync<RejectedException>(() => _actions.AcceptAsync(token, "Decisions/2026-11-20-merge-order.md", default));

        var accepted = _s.H.Vault.Notes["Decisions/2026-11-20-merge-order.md"];
        Assert.Equal("accepted", accepted.Status);
        Assert.True(Attention.DecisionRules.ToReview(accepted));
        Assert.Equal("Shiori", _s.H.Vault.Writes.Last(w => w.Path == accepted.Path).Author);
        Assert.Equal("decision-waits", waits.Code);
        Assert.Contains("2 offices", waits.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", _s.H.Vault.Notes["Decisions/2026-11-20-one-linter.md"].Status);
        Assert.Equal(Attention.DecisionCommands.WrongStatusCode, again.Code);
    }

    [Fact]
    public async Task A_rule_for_the_constitution_can_be_proposed_but_never_accepted_by_Shiori()
    {
        var (_, token) = await OpenAsync(ShioriMode.Decide);

        await _actions.ProposeAsync(token, new("rule", "Ask before deploying", "Deployments wait for a yes.", "They cannot be undone.", [], [], []), null, default);
        var refused = await Assert.ThrowsAsync<RejectedException>(() => _actions.AcceptAsync(token, "Decisions/2015-06-24-ask-before-deploying.md", default));

        Assert.Equal("rule", _s.H.Vault.Notes["Decisions/2015-06-24-ask-before-deploying.md"].Kind);
        Assert.Contains("only you change those", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Organise_asks_the_supervisor_of_each_office_for_a_plan_of_proposed_tasks()
    {
        var other = Office.Open("Allsafe", Harness.Start);
        var room = other.OpenRoom("Ops", Harness.Start);
        other.AddDesk(room.Id, "Backend", AgentProfile.Create("build"), Harness.Start);
        other.SetRepository("/srv/repos/allsafe", Harness.Start);
        _s.H.Offices.Add(other);
        _s.H.Runtime.Plan = JsonSerializer.Serialize(new { title = "Logout", tasks = new[] { new { title = "Add a logout button", description = "Top right.", desk = "Frontend" } } });
        var (id, token) = await OpenAsync(ShioriMode.Organise);

        // One plan after the other: the fakes behind both supervisors are not made for two at once.
        var first = await _actions.PlanAsync(token, "e corp", "Add a logout button", default);
        await DrainAsync();
        await _actions.PlanAsync(token, "Allsafe", "Log the logouts", default);
        await DrainAsync();

        Assert.Contains("The supervisor of E Corp is planning it", first, StringComparison.Ordinal);
        Assert.Equal(["/srv/repos/e-corp", "/srv/repos/allsafe"], _s.H.Runtime.PlanRequests.Select(p => p.Folder));
        Assert.All(_s.Jobs.Snapshot(), j => Assert.Equal(("shiori", Work.SupervisorJobState.Done), (j.Kind, j.State)));
        var proposed = _s.H.Tasks.Items.Where(t => t.Status == WorkTaskStatus.Proposed).ToList();
        Assert.NotEmpty(proposed);
        Assert.All(proposed, t => Assert.Equal("Shiori", t.RequestedBy));
        var board = (await _sessions.GetAsync(id, default)).Board;
        Assert.Equal(["Plan asked of E Corp: Add a logout button", "Plan asked of Allsafe: Log the logouts"], board.Select(i => i.Text));
        Assert.All(board, i => Assert.Equal(WhiteboardKind.Task, i.Kind));
    }

    [Fact]
    public async Task Organise_gives_a_single_task_straight_to_the_desk_named_approved_and_queued()
    {
        var (id, token) = await OpenAsync(ShioriMode.Organise);

        var said = await _actions.AssignAsync(token, "e corp", "frontend", "Read the app's version", "Say which version package.json has.", default);

        var task = Assert.Single(_s.H.Tasks.Items);
        Assert.Equal((WorkTaskStatus.Queued, _s.H.Desk.Id, "Shiori", "Read the app's version"), (task.Status, task.DeskId!.Value, task.RequestedBy, task.Title));
        Assert.Contains("No approval needed", said, StringComparison.Ordinal);
        Assert.Contains("Read the app's version → E Corp, Frontend", (await _sessions.GetAsync(id, default)).Board.Select(i => i.Text));
        Assert.Empty(_s.H.Runtime.PlanRequests);
    }

    [Fact]
    public async Task Without_a_desk_the_task_goes_to_the_least_busy_one_and_a_wrong_desk_is_named()
    {
        var spare = _s.H.Office.AddDesk(_s.H.Room.Id, "Backend", AgentProfile.Create("build"), Harness.Start);
        _s.H.Enqueue("Already busy");
        var (_, token) = await OpenAsync(ShioriMode.Organise);

        await _actions.AssignAsync(token, "E Corp", null, "Read the version", null, default);
        var wrong = await Assert.ThrowsAsync<RejectedException>(() => _actions.AssignAsync(token, "E Corp", "Design", "Anything", null, default));

        Assert.Equal(spare.Id, _s.H.Tasks.Items.Single(t => t.Title == "Read the version").DeskId);
        Assert.Contains("Its desks are: Frontend, Backend", wrong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_organise_gives_tasks()
    {
        var (_, token) = await OpenAsync(ShioriMode.Brainstorm);

        var refused = await Assert.ThrowsAsync<RejectedException>(() => _actions.AssignAsync(token, "E Corp", null, "Read the version", null, default));

        Assert.Equal(ShioriActions.ModeCode, refused.Code);
        Assert.Empty(_s.H.Tasks.Items);
    }

    [Fact]
    public async Task Organise_names_the_offices_when_asked_for_one_that_does_not_exist_and_cannot_propose()
    {
        var (_, token) = await OpenAsync(ShioriMode.Organise);

        var missing = await Assert.ThrowsAsync<RejectedException>(() => _actions.PlanAsync(token, "Evil Corp", "Anything", default));
        var propose = await Assert.ThrowsAsync<RejectedException>(() => _actions.ProposeAsync(token, Decision("Merge in approval order"), null, default));

        Assert.Contains("The offices are: E Corp", missing.Message, StringComparison.Ordinal);
        Assert.Equal(ShioriActions.ModeCode, propose.Code);
    }

    [Fact]
    public async Task A_mode_switch_counts_for_the_tools_at_once_even_mid_answer()
    {
        var (id, token) = await OpenAsync(ShioriMode.Brainstorm);
        await Assert.ThrowsAsync<RejectedException>(() => _actions.ProposeAsync(token, Decision("Merge in approval order"), null, default));

        await _sessions.SetModeAsync(id, ShioriMode.Decide, default);

        Assert.Contains("Proposed as", await _actions.ProposeAsync(token, Decision("Merge in approval order"), null, default), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_whiteboard_tool_changes_the_board_in_every_mode()
    {
        var (id, token) = await OpenAsync(ShioriMode.Brainstorm);

        var added = await _actions.WhiteboardAsync(token, new(WhiteboardAction.Add, Kind: WhiteboardKind.Doubt, Text: "What about hotfixes?"), default);

        Assert.Equal("Added w1 (doubt): What about hotfixes?", added);
        Assert.Equal("What about hotfixes?", (await _sessions.GetAsync(id, default)).Board.Single().Text);
        Assert.Equal(id, _s.Publisher.Boards.Single().Session);
    }

    [Fact]
    public async Task Tools_refuse_an_unknown_secret_and_an_ended_session()
    {
        var (id, token) = await OpenAsync(ShioriMode.Decide);
        await _sessions.EndAsync(id, default);

        var unknown = await Assert.ThrowsAsync<RejectedException>(() => _actions.OfficesAsync("not-a-session", default));
        var ended = await Assert.ThrowsAsync<RejectedException>(() => _actions.OfficesAsync(token, default));

        Assert.Equal(("shiori-unknown", ShioriSessions.EndedCode), (unknown.Code, ended.Code));
    }

    [Fact]
    public async Task The_offices_are_listed_with_their_rooms_and_desks()
    {
        var (_, token) = await OpenAsync(ShioriMode.Brainstorm);

        var offices = await _actions.OfficesAsync(token, default);

        Assert.Equal("E Corp\n- room Web: Frontend", offices.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Tools_used_while_Shiori_answers_land_on_the_board_and_the_answer_is_kept_too()
    {
        var (id, token) = await OpenAsync(ShioriMode.Brainstorm);
        _s.Runtime.During = turn => _actions.WhiteboardAsync(turn.ToolsToken, new(WhiteboardAction.Add, Kind: WhiteboardKind.Idea, Text: "Batch merges at night"), default);

        await _sessions.SendAsync(id, "Ideas?", default);
        await _sessions.Completion(id);

        var session = await _sessions.GetAsync(id, default);
        Assert.Equal(token, _s.Runtime.Turns.Single().ToolsToken);
        Assert.Equal("Batch merges at night", session.Board.Single().Text);
        Assert.Equal("Noted.", session.Transcript[^1].Text);
        Assert.Contains("- [ ] Batch merges at night", _s.H.Vault.Notes[session.Note].Body, StringComparison.Ordinal);
    }
}
