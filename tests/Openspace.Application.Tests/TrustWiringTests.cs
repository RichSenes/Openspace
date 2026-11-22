using Openspace.Application.Agents;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>A desk's trust for its journal and its proposals, as the brain's tools and Shiori honour it.</summary>
public sealed class TrustWiringTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> RunningTaskAsync(TrustedAction action, TrustLevel level)
    {
        _h.Office.SetDeskTrust(_h.Desk.Id, action, level, _h.Clock.GetUtcNow());
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    private async Task<IReadOnlyList<string>> AcceptAsync()
    {
        using var acceptor = new DecisionAcceptor(_h.Vault);
        return await acceptor.RunAsync(default);
    }

    [Fact]
    public async Task A_desk_that_asks_first_leaves_its_journal_line_waiting_for_the_person()
    {
        var task = await RunningTaskAsync(TrustedAction.Journal, TrustLevel.AsksFirst);

        var path = await _h.BrainTools().JournalAsync(task.Id, "Added the button.", default);

        Assert.True(AgentBrain.IsWaiting(path));
        Assert.False(_h.Vault.Notes.ContainsKey(Journals.PathOf("Frontend")));
        Assert.Empty(await AcceptAsync());
        var needs = await new NeedsYouQueries(_h.Offices, _h.Tasks, _h.Routines, _h.Vault, _h.Clock).ListAsync(default);
        var item = Assert.Single(needs, n => n.Note == path);
        Assert.Equal(NeedsYouKind.DecisionToAccept, item.Kind);
        Assert.Contains("asks you first", item.Detail, StringComparison.Ordinal);

        // Accepted by the person, the line goes into the journal.
        await new DecisionCommands(_h.Vault, _h.Clock).DoAsync(path, DecisionVerb.Accept, "angela", null, default);

        Assert.Contains("Add a logout button: Added the button.", _h.Vault.Notes[Journals.PathOf("Frontend")].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_desk_that_tells_writes_its_journal_and_leaves_a_line_in_the_feed()
    {
        var task = await RunningTaskAsync(TrustedAction.Journal, TrustLevel.Tells);

        var path = await _h.BrainTools().JournalAsync(task.Id, "Added the button.", default);

        Assert.Equal(Journals.PathOf("Frontend"), path);
        var line = Assert.Single(_h.Audit.Items, e => e.Summary == "Wrote in its journal");
        Assert.Equal((task.Id, AuditKind.Note, "Added the button."), (line.TaskId, line.Kind, line.Detail));
    }

    [Fact]
    public async Task A_desk_alone_writes_its_journal_without_a_feed_line()
    {
        var task = await RunningTaskAsync(TrustedAction.Journal, TrustLevel.Alone);

        await _h.BrainTools().JournalAsync(task.Id, "Added the button.", default);

        Assert.DoesNotContain(_h.Audit.Items, e => e.Summary == "Wrote in its journal");
    }

    [Fact]
    public async Task Shiori_leaves_the_proposals_of_a_desk_that_asks_first_to_the_person()
    {
        var task = await RunningTaskAsync(TrustedAction.Proposals, TrustLevel.AsksFirst);

        var path = await _h.BrainTools().ProposeAsync(task.Id, "decision", "Buttons use the shared component", "Always.", null, null, null, "Drift.", default);

        Assert.Empty(await AcceptAsync());
        Assert.Equal(NoteStatus.Proposed, _h.Vault.Notes[path].Status);
        Assert.Contains("asks you first", DecisionRules.WhyWaiting(_h.Vault.Notes[path], []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_desk_that_tells_proposes_with_a_feed_line_and_Shiori_accepts_as_usual()
    {
        var task = await RunningTaskAsync(TrustedAction.Proposals, TrustLevel.Tells);

        var path = await _h.BrainTools().ProposeAsync(task.Id, "decision", "Buttons use the shared component", "Always.", null, null, null, "Drift.", default);

        Assert.Equal([path], await AcceptAsync());
        var line = Assert.Single(_h.Audit.Items, e => e.Kind == AuditKind.Note);
        Assert.Equal("Proposed “Buttons use the shared component” to the brain", line.Summary);
    }

    [Theory]
    [InlineData(true, TrustLevel.AsksFirst)]
    [InlineData(false, TrustLevel.Tells)]
    public void A_routines_switch_is_a_trust_level(bool askFirst, TrustLevel level)
    {
        var routine = Routine.Create(_h.Office, "Mail", "mail-sorting", RoutineSchedule.Daily(new TimeOnly(7, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        routine.AskBeforeActing(askFirst, _h.Clock.GetUtcNow());

        Assert.Equal(level, routine.Trust);
    }

    [Theory]
    [InlineData(TrustLevel.Alone, true, true)]
    [InlineData(TrustLevel.Tells, true, true)]
    [InlineData(TrustLevel.AsksFirst, true, false)]
    [InlineData(TrustLevel.Tells, false, false)]
    public void Acting_alone_needs_trust_and_a_way_back(TrustLevel level, bool canUndo, bool alone) =>
        Assert.Equal(alone, TrustSettings.ActsAlone(level, canUndo));
}
