using Openspace.Application.Reports;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class ReportTests
{
    private readonly Harness _h = new();
    private static readonly DateTimeOffset Day = Harness.Start.Date;
    private static readonly DateTimeOffset NextDay = Day.AddDays(1);

    private WorkTask TaskWithWork(string title, DateTimeOffset at)
    {
        var task = _h.Enqueue(title);
        task.Start(at);
        task.AttachRun(AgentRun.Create($"ses_{title.Length}", $"/srv/worktrees/{title.Length}", $"openspace/{title.Length}"), at);
        task.AdoptProcedure([]);
        _h.Audit.Add(AuditEntry.Record(task.Id, at, AuditKind.Change, "Changed header.php", "+<button>"));
        _h.Audit.Add(AuditEntry.Record(task.Id, at, AuditKind.Command, "Ran npm test", succeeded: false));
        _h.Audit.Add(AuditEntry.Record(task.Id, at, AuditKind.Note, "Note from the agent", "Added the button."));
        return task;
    }

    [Fact]
    public void The_facts_list_what_was_recorded_and_nothing_else()
    {
        var task = TaskWithWork("Add a logout button", Harness.Start);

        var facts = ReportFacts.Build(_h.Office, "24 June", [new TaskActivity(task, _h.Audit.Items)]);

        Assert.Contains("# E Corp: 24 June", facts, StringComparison.Ordinal);
        Assert.Contains("- Desk: Frontend (room Web)", facts, StringComparison.Ordinal);
        Assert.Contains("  - Changed header.php", facts, StringComparison.Ordinal);
        Assert.Contains("  - Ran npm test (failed)", facts, StringComparison.Ordinal);
        Assert.Contains("Note from the agent: Added the button.", facts, StringComparison.Ordinal);
        Assert.Contains("Tasks: 1 (1 Running)", facts, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_period_says_so()
    {
        Assert.Contains("No task was worked on", ReportFacts.Build(_h.Office, "24 June", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_report_covers_this_office_and_that_day_only_and_is_written_by_the_AI_in_the_chosen_language()
    {
        TaskWithWork("Today", Harness.Start);
        TaskWithWork("Yesterday", Day.AddHours(-3));
        var other = Office.Open("Allsafe", Harness.Start);
        var room = other.OpenRoom("Security", Harness.Start);
        var desk = other.AddDesk(room.Id, "Auditor", AgentProfile.Create("build"), Harness.Start);
        _h.Offices.Add(other);
        var elsewhere = WorkTask.Assign(other, desk.Id, "Elsewhere", null, "angela", Harness.Start);
        _h.Tasks.Add(elsewhere);
        _h.Audit.Add(AuditEntry.Record(elsewhere.Id, Harness.Start, AuditKind.Change, "Changed firewall.conf"));
        _h.Runtime.Draft = "## Fatto\n- Pulsante di logout aggiunto.";

        var report = await _h.ReportWriter().WriteForPeriodAsync(_h.Office.Id, Day, NextDay, "24 giugno", "Italian", "elliot", default);

        var (instructions, facts) = Assert.Single(_h.Runtime.DraftRequests);
        Assert.Contains("Write the report in Italian.", instructions, StringComparison.Ordinal);
        Assert.Contains("## Today", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("Yesterday", facts, StringComparison.Ordinal);
        Assert.DoesNotContain("firewall", facts, StringComparison.Ordinal);
        Assert.Equal(("E Corp: 24 giugno", "## Fatto\n- Pulsante di logout aggiunto.", true), (report.Title, report.Body, report.WrittenByAi));
        Assert.Single(_h.Reports.Items);
    }

    [Fact]
    public async Task Without_the_AI_the_report_is_the_facts()
    {
        TaskWithWork("Today", Harness.Start);
        _h.Runtime.FailOnDraft = new HttpRequestException("connection refused");

        var report = await _h.ReportWriter().WriteForPeriodAsync(_h.Office.Id, Day, NextDay, "24 June", "English", "elliot", default);

        Assert.False(report.WrittenByAi);
        Assert.Contains("## Today", report.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_epic_report_starts_from_the_request_and_lists_its_tasks()
    {
        var epic = Epic.Open(_h.Office, "Contact page", "Add a contact page that emails the admin.", "elliot", Harness.Start);
        _h.Epics.Add(epic);
        var task = WorkTask.Propose(_h.Office, "Build the form", null, "elliot", Harness.Start, epic);
        _h.Tasks.Add(task);

        var report = await _h.ReportWriter().WriteForEpicAsync(epic.Id, "English", "elliot", default);

        var facts = Assert.Single(_h.Runtime.DraftRequests).Text;
        Assert.Contains("Request by elliot: Add a contact page that emails the admin.", facts, StringComparison.Ordinal);
        Assert.Contains("## Build the form", facts, StringComparison.Ordinal);
        Assert.Equal((Domain.Reports.ReportKind.Epic, (Guid?)epic.Id.Value), (report.Kind, report.EpicId));
    }

    /// <summary>A task the agent finished, Claude reviewed and a person accepted.</summary>
    private async Task<WorkTask> AcceptedAsync(string title, string summary)
    {
        var task = _h.Enqueue(title);
        await _h.Runner().RunQueueAsync(default);
        _h.Runtime.LastReply = summary;
        await _h.Signals().FinishedAsync(task.Run!.SessionId, default);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await _h.Commands().AcceptAsync(task.Id, default);
        return task;
    }

    [Fact]
    public async Task A_changelog_tells_users_what_was_accepted_since_the_previous_one()
    {
        await AcceptedAsync("Add a logout button", "Added a Logout link to the header of every page.");
        _h.Runtime.Draft = "## Novità\n- Puoi uscire dal tuo account da ogni pagina.";

        var first = await _h.ReportWriter().WriteChangelogAsync(_h.Office.Id, "Italian", "elliot", default);

        var (instructions, facts) = Assert.Single(_h.Runtime.DraftRequests);
        Assert.Contains("Never mention files, branches, tests", instructions, StringComparison.Ordinal);
        Assert.Contains("Write the changelog in Italian.", instructions, StringComparison.Ordinal);
        Assert.Contains("## Add a logout button", facts, StringComparison.Ordinal);
        Assert.Contains("The agent's summary: Added a Logout link", facts, StringComparison.Ordinal);
        Assert.Equal((Domain.Reports.ReportKind.Changelog, (DateTimeOffset?)null, true), (first.Kind, first.PeriodStart, first.WrittenByAi));

        _h.Clock.Advance(TimeSpan.FromHours(1));
        await AcceptedAsync("Round prices to two decimals", "Prices now show two decimals.");

        var second = await _h.ReportWriter().WriteChangelogAsync(_h.Office.Id, "Italian", "elliot", default);

        Assert.Equal(first.PeriodEnd, second.PeriodStart);
        Assert.DoesNotContain("logout", _h.Runtime.DraftRequests[^1].Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## Round prices", _h.Runtime.DraftRequests[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changelog_needs_new_accepted_work()
    {
        await Assert.ThrowsAsync<Domain.Common.DomainException>(() => _h.ReportWriter().WriteChangelogAsync(_h.Office.Id, "English", "elliot", default));

        await AcceptedAsync("Add a logout button", "Done.");
        await _h.ReportWriter().WriteChangelogAsync(_h.Office.Id, "English", "elliot", default);

        var error = await Assert.ThrowsAsync<Domain.Common.DomainException>(() => _h.ReportWriter().WriteChangelogAsync(_h.Office.Id, "English", "elliot", default));
        Assert.Contains("since the previous changelog", error.Message, StringComparison.Ordinal);
    }
}
