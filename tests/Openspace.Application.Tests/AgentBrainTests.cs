using Openspace.Application.Agents;
using Openspace.Application.Brain;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>What agents do with the brain through their tools.</summary>
public class AgentBrainTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> RunningTaskAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public async Task A_journal_is_made_for_the_desk_and_keeps_the_entries_of_a_day_under_one_heading()
    {
        var task = await RunningTaskAsync();
        var brain = _h.BrainTools();

        var path = await brain.JournalAsync(task.Id, "Added the button.", default);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await brain.JournalAsync(task.Id, "The header is shared\nwith admin.", default);
        _h.Clock.Advance(TimeSpan.FromDays(1));
        await brain.JournalAsync(task.Id, "Back to it.", default);

        Assert.Equal("Inhabitants/Frontend/journal.md", path);
        var journal = _h.Vault.Notes[path];
        Assert.Equal(("journal", "Frontend"), (journal.Kind, journal.Author));
        Assert.Equal(
            "# Frontend's journal\n\n## 2015-06-24\n\n- 09:00 · Add a logout button: Added the button.\n- 09:05 · Add a logout button: The header is shared with admin.\n\n## 2015-06-25\n\n- 09:05 · Add a logout button: Back to it.\n",
            journal.Body);
        Assert.All(_h.Vault.Writes, w => Assert.Equal(("Frontend", "wrote in its journal"), (w.Author, w.Summary)));
    }

    [Fact]
    public async Task A_proposal_is_a_new_proposed_note_signed_by_the_desk()
    {
        var task = await RunningTaskAsync();
        var brain = _h.BrainTools();

        var first = await brain.ProposeAsync(task.Id, "Decision", "Buttons use the shared component!", "Never style one by hand.", null, ["Web"], ["src/web/**"], "Three styles drifted apart.", default);
        var second = await brain.ProposeAsync(task.Id, "decision", "Buttons use the shared component!", "Again.", ["Allsafe"], null, null, "Twice.", default);

        Assert.Equal(("Decisions/2015-06-24-buttons-use-the-shared-component.md", "Decisions/2015-06-24-buttons-use-the-shared-component-2.md"), (first, second));
        var note = _h.Vault.Notes[first];
        Assert.Equal(("decision", NoteStatus.Proposed, "Frontend", $"task {task.Id.Value}"), (note.Kind, note.Status, note.Author, note.From));
        Assert.Equal(["E Corp"], note.Offices);
        Assert.Equal(["Web"], note.Rooms);
        Assert.Equal(["src/web/**"], note.Paths);
        Assert.Equal("Buttons use the shared component!", note.Title);
        Assert.Contains("## Reasons\n\nThree styles drifted apart.", note.Body, StringComparison.Ordinal);
        Assert.Equal(["Allsafe"], _h.Vault.Notes[second].Offices);
        Assert.Equal(("Frontend", $"proposed {first}"), (_h.Vault.Writes[0].Author, _h.Vault.Writes[0].Summary));
    }

    [Fact]
    public async Task A_star_proposes_for_every_office_with_no_offices_in_the_front_matter()
    {
        var task = await RunningTaskAsync();

        var path = await _h.BrainTools().ProposeAsync(task.Id, "decision", "Tabs everywhere", "Tabs.", [AgentBrain.EveryOffice, "Allsafe"], null, null, "One style.", default);

        Assert.Empty(_h.Vault.Notes[path].Offices);
        Assert.DoesNotContain("offices:", VaultFormat.Write(_h.Vault.Notes[path]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_office_note_goes_in_the_office_folder()
    {
        var task = await RunningTaskAsync();

        Assert.Equal("Offices/E Corp/glossary.md", await _h.BrainTools().ProposeAsync(task.Id, "note", "Glossary", "Words.", null, null, null, "Newcomers asked.", default));
    }

    [Theory]
    [InlineData("rule")]
    [InlineData("journal")]
    [InlineData("")]
    public async Task Agents_cannot_propose_rules_or_write_journals_as_proposals(string kind)
    {
        var task = await RunningTaskAsync();

        var refused = await Assert.ThrowsAsync<RejectedException>(() =>
            _h.BrainTools().ProposeAsync(task.Id, kind, "Always", "Do it.", null, null, null, "Because.", default));

        Assert.Equal("brain-kind", refused.Code);
        Assert.Empty(_h.Vault.Writes);
    }

    [Fact]
    public async Task A_proposal_needs_its_reasons()
    {
        var task = await RunningTaskAsync();

        await Assert.ThrowsAsync<RejectedException>(() =>
            _h.BrainTools().ProposeAsync(task.Id, "decision", "Always", "Do it.", null, null, null, "  ", default));
    }

    [Fact]
    public async Task Search_ranks_the_callers_own_room_first()
    {
        var task = await RunningTaskAsync();
        _h.Vault.Put("Decisions/a.md", "---\nkind: decision\nstatus: accepted\nrooms: [Api]\n---\n# Api merges\n\nMerge on Fridays.\n");
        _h.Vault.Put("Decisions/b.md", "---\nkind: decision\nstatus: accepted\nrooms: [Web]\n---\n# Web merges\n\nMerge on Mondays.\n");

        var text = await _h.BrainTools().SearchAsync(task.Id, "merge", null, null, null, null, default);

        Assert.StartsWith("- Decisions/b.md · decision, accepted · Web merges (Web)", text, StringComparison.Ordinal);
        Assert.Contains("Decisions/a.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_a_missing_note_says_how_to_find_one()
    {
        var task = await RunningTaskAsync();

        var refused = await Assert.ThrowsAsync<RejectedException>(() => _h.BrainTools().ReadAsync(task.Id, "Decisions/nothing", default));

        Assert.Contains("brain_search", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Now_lists_the_others_working_in_the_office()
    {
        var task = await RunningTaskAsync();
        _h.Work.Items.Add(new WorkInFlight(task.Id.Value, _h.Office.Id.Value, "E Corp", "Web", "Frontend", task.Title, ["src/web/header.ts"]));
        _h.Work.Items.Add(new WorkInFlight(Guid.NewGuid(), _h.Office.Id.Value, "E Corp", "Api", "Backend", "Fix the cart", ["src/api/cart.cs"]));
        _h.Work.Items.Add(new WorkInFlight(Guid.NewGuid(), Guid.NewGuid(), "Allsafe", "Web", "Screens", "Elsewhere", []));

        var text = await _h.BrainTools().NowAsync(task.Id, default);

        Assert.Equal("Working in this office right now:\n- \"Fix the cart\" at the desk \"Backend\" (room Api): changing src/api/cart.cs", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_tools_answer_only_a_task_its_agent_is_working_on()
    {
        var task = _h.Enqueue();

        var refused = await Assert.ThrowsAsync<RejectedException>(() => _h.BrainTools().JournalAsync(task.Id, "Hello.", default));

        Assert.Equal("task-not-running", refused.Code);
    }
}
