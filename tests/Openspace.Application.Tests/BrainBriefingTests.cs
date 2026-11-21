using Openspace.Application.Agents;
using Openspace.Application.Brain;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>The "Before you start" part of a briefing, read from a vault in memory.</summary>
public class BrainBriefingTests
{
    private const string Constitution = """
        ---
        kind: rule
        status: accepted
        ---
        # Constitution

        The person's rules.

        1. Read before acting: the constitution, the decisions in scope, *Now*.
        2. Every behaviour change comes with a test that fails without it; the number of tests never
           goes down.
        3. Work only in the task's own copy.

        Anything after the list is not a rule.
        """;

    private static readonly string[] MostRelevantFirst = ["The header is frozen", "Buttons use the shared component", "English commits"];

    private readonly Harness _h = new();

    private static string Decision(string title, string gist, string scope, string status = "accepted") => $"""
        ---
        kind: decision
        status: {status}
        {scope}
        ---
        # {title}

        {gist}
        """;

    private WorkTask Task(string description = "Top right corner.")
    {
        var task = WorkTask.Assign(_h.Office, _h.Desk.Id, "Add a logout button", description, "elliot", _h.Clock.GetUtcNow());
        _h.Tasks.Add(task);
        return task;
    }

    private WorkInFlight Elsewhere(string title, string room, params string[] paths) =>
        new(Guid.NewGuid(), _h.Office.Id.Value, _h.Office.Name, room, "Screens", title, paths);

    private async Task<string> BriefAsync(WorkTask task) =>
        Briefing.ForNewTask(_h.Office, _h.Room, _h.Desk, task, null, await _h.BrainTools().BriefAsync(_h.Office, _h.Room, task, default));

    [Fact]
    public async Task The_brief_lists_the_rules_the_decisions_in_scope_the_work_nearby_and_what_to_leave_behind()
    {
        _h.Vault.Put("Constitution.md", Constitution);
        _h.Vault.Put("Decisions/2026-11-01-web-buttons.md", Decision("Buttons use the shared component", "Never style a button by hand.", "offices: [E Corp]\nrooms: [Web]"));
        _h.Vault.Put("Decisions/2026-11-02-header.md", Decision("The header is frozen", "Ask before changing the header.", "paths: [\"src/web/header/**\"]"));
        _h.Vault.Put("Decisions/2026-11-03-everywhere.md", Decision("English commits", "Commits are written in English.", ""));
        _h.Vault.Put("Decisions/2026-11-04-other-office.md", Decision("Allsafe deploys on Mondays", "Only Mondays.", "offices: [Allsafe]"));
        _h.Vault.Put("Decisions/2026-11-05-api.md", Decision("Api versions", "Version every route.", "rooms: [Api]"));
        _h.Vault.Put("Decisions/2026-11-06-proposed.md", Decision("Proposed idea", "Not yet.", "rooms: [Web]", "proposed"));
        _h.Vault.Put("Decisions/2026-11-07-old.md", Decision("Old buttons", "Style by hand.", "rooms: [Web]", "superseded"));
        _h.Work.Items.Add(Elsewhere("Fix the cart", "Api", "src/web/header/menu.ts", "src/api/cart.cs"));
        _h.Work.Items.Add(Elsewhere("Restyle the footer", "Web", "src/web/footer.css"));
        _h.Work.Items.Add(Elsewhere("Rename routes", "Api", "src/api/routes.cs"));

        var text = await BriefAsync(Task("Put it in src/web/header/menu.ts, next to the search."));

        Assert.Contains("Before you start:", text, StringComparison.Ordinal);
        Assert.Contains("2. Every behaviour change comes with a test that fails without it; the number of tests never goes down.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Anything after the list", text, StringComparison.Ordinal);
        var shown = MostRelevantFirst.Select(t => text.IndexOf(t, StringComparison.Ordinal)).ToList();
        Assert.All(shown, i => Assert.True(i > 0));
        Assert.Equal(shown.Order(), shown);
        Assert.Contains("- The header is frozen: Ask before changing the header. (Decisions/2026-11-02-header.md)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Allsafe", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Api versions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Proposed idea", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Old buttons", text, StringComparison.Ordinal);
        Assert.Contains("\"Fix the cart\" at the desk \"Screens\" (room Api): changing src/web/header/menu.ts, src/api/cart.cs; shares src/web/header/menu.ts with you", text, StringComparison.Ordinal);
        Assert.Contains("Restyle the footer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Rename routes", text, StringComparison.Ordinal);
        Assert.Contains("brain_journal", text, StringComparison.Ordinal);
        Assert.Contains("brain_propose", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Before you start:", StringComparison.Ordinal) > text.IndexOf("Your task:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_known_files_the_work_nearby_is_the_work_in_the_same_room()
    {
        _h.Work.Items.Add(Elsewhere("Fix the cart", "Api", "src/web/header/menu.ts"));
        _h.Work.Items.Add(Elsewhere("Restyle the footer", "Web", "src/web/footer.css"));

        var text = await BriefAsync(Task());

        Assert.Contains("Restyle the footer", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fix the cart", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_files_the_task_is_already_changing_find_the_decisions_about_them()
    {
        var task = Task();
        _h.Vault.Put("Decisions/2026-11-02-header.md", Decision("The header is frozen", "Ask first.", "rooms: [Api]\npaths: [\"src/web/header/**\"]"));
        _h.Work.Items.Add(new WorkInFlight(task.Id.Value, _h.Office.Id.Value, _h.Office.Name, _h.Room.Name, _h.Desk.Name, task.Title, ["src/web/header/menu.ts"]));

        Assert.Contains("The header is frozen", await BriefAsync(task), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_or_missing_vault_leaves_the_brief_as_it_was()
    {
        var task = Task();

        Assert.Equal(Briefing.ForNewTask(_h.Office, _h.Room, _h.Desk, task), await BriefAsync(task));
    }

    [Fact]
    public async Task At_most_eight_decisions_are_shown_and_the_rest_are_counted()
    {
        for (var i = 1; i <= 11; i++)
        {
            _h.Vault.Put($"Decisions/2026-11-{i:00}-d{i}.md", Decision($"Decision {i}", "Something to follow.", "rooms: [Web]"));
        }

        var text = await BriefAsync(Task());

        Assert.Equal(AgentBrain.BriefingDecisions, text.Split('\n').Count(l => l.StartsWith("- Decision ", StringComparison.Ordinal)));
        Assert.Contains("3 more decisions apply: brain_search", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_long_brain_is_cut_to_the_limit_and_says_so()
    {
        var rules = string.Join('\n', Enumerable.Range(1, 40).Select(i => $"{i}. Rule number {i}: " + new string('x', 150)));
        _h.Vault.Put("Constitution.md", $"# Constitution\n\n{rules}\n");
        for (var i = 1; i <= 8; i++)
        {
            _h.Vault.Put($"Decisions/2026-11-{i:00}-d{i}.md", Decision($"Decision {i}", new string('y', 300), "rooms: [Web]"));
        }

        var part = Briefing.BeforeYouStart((await _h.BrainTools().BriefAsync(_h.Office, _h.Room, Task(), default))!);

        Assert.True(part.Length <= Briefing.BeforeYouStartLimit, $"{part.Length} characters");
        Assert.Contains("(Cut to keep this brief short", part, StringComparison.Ordinal);
        Assert.Contains("more: read them with brain_read Constitution.md", part, StringComparison.Ordinal);
        Assert.Contains("8 more decisions apply", part, StringComparison.Ordinal);
        Assert.Contains("brain_journal", part, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_agent_reads_the_brain_in_its_first_message()
    {
        _h.Vault.Put("Constitution.md", Constitution);
        _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Contains("1. Read before acting", _h.Runtime.Prompts[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Paths_named_in_a_task_are_files_and_folders_not_any_word_with_a_slash() =>
        Assert.Equal(
            ["src/web/cart.ts", "docs/", "src/api/**"],
            AgentBrain.NamedPaths("Fix src/web/cart.ts and docs/, also src/api/** and/or see https://example.com/a.html."));
}
