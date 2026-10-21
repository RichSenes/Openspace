using System.Text.Json;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

using Openspace.Application.Work;

namespace Openspace.Application.Tests;

public class SupervisorTests
{
    private readonly Harness _h = new();
    private readonly Desk _tester;

    public SupervisorTests()
    {
        var qa = _h.Office.OpenRoom("QA", Harness.Start);
        _h.Office.UpdateRoomContext(qa.Id, "Only checklists and tests.", Harness.Start);
        _tester = _h.Office.AddDesk(qa.Id, "Tester", AgentProfile.Create("build"), Harness.Start);
    }

    private static string Plan(string title, params (string Title, string Desk)[] tasks) =>
        JsonSerializer.Serialize(new
        {
            title,
            tasks = tasks.Select(t => new { title = t.Title, description = $"Do: {t.Title}", desk = t.Desk }),
        });

    [Fact]
    public async Task A_request_becomes_an_epic_of_proposed_tasks_on_the_right_desks()
    {
        _h.Runtime.Plan = Plan("Contact page", ("Build the contact form", "Frontend"), ("Write the release checklist", "tester"));

        var plan = await _h.Supervisor().PlanAsync(_h.Office.Id, "Add a contact page.", "elliot", default);

        var epic = Assert.Single(_h.Epics.Items);
        Assert.Equal(("Contact page", "Add a contact page."), (epic.Title, epic.Request));
        Assert.Equal(2, plan.Tasks.Count);
        Assert.All(_h.Tasks.Items, t => Assert.Equal((WorkTaskStatus.Proposed, (EpicId?)epic.Id), (t.Status, t.EpicId)));
        Assert.Equal([_h.Desk.Id, _tester.Id], _h.Tasks.Items.Select(t => t.DeskId!.Value));
        Assert.Contains("Contact page", _h.Tasks.Items[0].Notes[0].Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Web / Frontend")]
    [InlineData("Frontend (room Web)")]
    [InlineData("  FRONTEND ")]
    public async Task Desk_names_are_matched_forgivingly(string written)
    {
        _h.Runtime.Plan = Plan("Logout", ("Add a logout button", written));

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add logout.", "elliot", default);

        Assert.Equal(_h.Desk.Id, _h.Tasks.Items[0].DeskId);
    }

    [Fact]
    public async Task A_task_for_an_unknown_desk_is_proposed_without_one_saying_what_was_suggested()
    {
        _h.Runtime.Plans.Enqueue(Plan("Logout", ("Add a logout button", "Mobile")));
        _h.Runtime.Plans.Enqueue("""{"desks":["Nobody"]}""");

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add logout.", "elliot", default);

        var task = Assert.Single(_h.Tasks.Items);
        Assert.Null(task.DeskId);
        Assert.Contains("suggested a desk named \"Mobile\"", task.Notes[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_schema_lists_the_desks_as_the_only_allowed_values()
    {
        _h.Runtime.Plan = Plan("Logout", ("Add a logout button", "Frontend"));

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add logout.", "elliot", default);

        using var schema = JsonDocument.Parse(Assert.Single(_h.Runtime.PlanSchemas));
        var desk = schema.RootElement.GetProperty("properties").GetProperty("tasks").GetProperty("items").GetProperty("properties").GetProperty("desk");
        Assert.Equal(["Frontend", "Tester"], desk.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("desk", schema.RootElement.GetProperty("properties").GetProperty("tasks").GetProperty("items").GetProperty("properties").EnumerateObject().First().Name);
    }

    [Fact]
    public async Task Missing_or_wrong_desks_are_filled_in_by_a_short_follow_up_question()
    {
        _h.Runtime.Plans.Enqueue(Plan("Logout", ("Add a logout button", "Sviluppatore"), ("Check it", ""), ("Style it", "Frontend")));
        _h.Runtime.Plans.Enqueue("""{"desks":["Frontend","Tester","Tester"]}""");

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add logout.", "elliot", default);

        Assert.Equal(2, _h.Runtime.PlanRequests.Count);
        var question = _h.Runtime.PlanRequests[1].Text;
        Assert.Contains("1. Add a logout button", question, StringComparison.Ordinal);
        Assert.Contains("exactly 3 desk names", question, StringComparison.Ordinal);
        Assert.Equal([_h.Desk.Id, _tester.Id, _h.Desk.Id], _h.Tasks.Items.Select(t => t.DeskId!.Value));
    }

    [Fact]
    public async Task A_useless_follow_up_answer_keeps_the_plan_and_explains()
    {
        _h.Runtime.Plans.Enqueue(Plan("Logout", ("Add a logout button", "")));
        _h.Runtime.Plans.Enqueue("not json");

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add logout.", "elliot", default);

        var task = Assert.Single(_h.Tasks.Items);
        Assert.Null(task.DeskId);
        Assert.Contains("did not name a desk", task.Notes[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_a_single_desk_everything_goes_to_it_without_asking_again()
    {
        var single = new Harness();
        single.Runtime.Plan = Plan("Phone field", ("Add the phone field", ""), ("Write the checklist", "Sviluppatore"));

        await single.Supervisor().PlanAsync(single.Office.Id, "Aggiungi il telefono.", "elliot", default);

        Assert.Single(single.Runtime.PlanRequests);
        Assert.All(single.Tasks.Items, t => Assert.Equal(single.Desk.Id, t.DeskId));
    }

    [Fact]
    public async Task The_model_gets_the_desks_their_roles_and_the_room_rules_and_reads_the_repository()
    {
        var auditor = Role.Define("Auditor", "Reviews code", null, null, new RolePermissions(PermissionLevel.Deny, PermissionLevel.Deny, PermissionLevel.Deny), Harness.Start);
        _h.Roles.Add(auditor);
        _h.Office.SetDeskRole(_tester.Id, auditor.Id, Harness.Start);
        _h.Runtime.Plan = Plan("Review", ("Review the form", "Tester"));

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Review the contact form.", "elliot", default);

        var (folder, text) = Assert.Single(_h.Runtime.PlanRequests);
        Assert.Equal("/srv/repos/e-corp", folder);
        Assert.Contains("- Tester (room QA): Auditor: Reviews code (may not change files)", text, StringComparison.Ordinal);
        Assert.Contains("Room rules: Only checklists and tests.", text, StringComparison.Ordinal);
        Assert.Contains("- Frontend (room Web): general agent (frontend-dev)", text, StringComparison.Ordinal);
        Assert.EndsWith("Review the contact form.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plans_are_cut_to_a_reasonable_size()
    {
        _h.Runtime.Plan = Plan("Big", [.. Enumerable.Range(1, 20).Select(i => ($"Step {i}", "Frontend"))]);

        var plan = await _h.Supervisor().PlanAsync(_h.Office.Id, "Everything.", "elliot", default);

        Assert.Equal(Supervisor.MaxTasks, plan.Tasks.Count);
    }

    [Theory]
    [InlineData("""{"title":"Nothing","tasks":[]}""")]
    [InlineData("not json at all")]
    public async Task An_empty_or_unreadable_plan_is_refused_and_nothing_is_saved(string answer)
    {
        _h.Runtime.Plan = answer;

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.Supervisor().PlanAsync(_h.Office.Id, "Something.", "elliot", default));

        Assert.Equal("planning-failed", error.Code);
        Assert.Empty(_h.Epics.Items);
        Assert.Equal(0, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task An_unreachable_model_is_explained()
    {
        _h.Runtime.FailOnPlan = new HttpRequestException("connection refused");

        var error = await Assert.ThrowsAsync<RejectedException>(() => _h.Supervisor().PlanAsync(_h.Office.Id, "Something.", "elliot", default));

        Assert.Contains("connection refused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planning_needs_a_repository_and_desks()
    {
        var bare = new Harness(withRepository: false);

        var noRepo = await Assert.ThrowsAsync<RejectedException>(() => bare.Supervisor().PlanAsync(bare.Office.Id, "Something.", "elliot", default));
        var empty = await Assert.ThrowsAsync<RejectedException>(() => _h.Supervisor().PlanAsync(_h.Office.Id, "  ", "elliot", default));

        Assert.Equal("no-repository", noRepo.Code);
        Assert.Equal("empty-text", empty.Code);
    }

    [Fact]
    public async Task Approving_an_epic_queues_its_proposals_that_have_a_desk()
    {
        _h.Runtime.Plan = Plan("Contact page", ("Build the form", "Frontend"), ("Mystery work", "Nobody"));
        var plan = await _h.Supervisor().PlanAsync(_h.Office.Id, "Add a contact page.", "elliot", default);

        var approved = await _h.Supervisor().ApproveAllAsync(new EpicId(plan.Epic.Id), default);

        Assert.Equal(1, approved);
        Assert.Equal([WorkTaskStatus.Queued, WorkTaskStatus.Proposed], _h.Tasks.Items.Select(t => t.Status));
    }

    [Theory]
    [InlineData("WordPress Developer")]
    [InlineData("Web")]
    [InlineData("Web / WordPress Developer")]
    public async Task A_desk_named_after_its_engine_is_still_found_by_role_or_room(string written)
    {
        var role = Role.Define("WordPress Developer", "Develops the theme", null, null, RolePermissions.Default, Harness.Start);
        _h.Roles.Add(role);
        _h.Office.RenameDesk(_h.Desk.Id, "build", Harness.Start);
        _h.Office.SetDeskRole(_h.Desk.Id, role.Id, Harness.Start);
        _h.Runtime.Plan = Plan("Contact page", ("Build the form", written));

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add a contact page.", "elliot", default);

        Assert.Equal(_h.Desk.Id, _h.Tasks.Items[0].DeskId);
    }

    [Fact]
    public async Task A_role_or_room_shared_by_several_desks_is_too_vague_to_assign()
    {
        var role = Role.Define("Developer", null, null, null, RolePermissions.Default, Harness.Start);
        _h.Roles.Add(role);
        _h.Office.SetDeskRole(_h.Desk.Id, role.Id, Harness.Start);
        _h.Office.SetDeskRole(_tester.Id, role.Id, Harness.Start);
        _h.Runtime.Plan = Plan("Contact page", ("Build the form", "Developer"));

        await _h.Supervisor().PlanAsync(_h.Office.Id, "Add a contact page.", "elliot", default);

        Assert.Null(_h.Tasks.Items[0].DeskId);
    }
}
