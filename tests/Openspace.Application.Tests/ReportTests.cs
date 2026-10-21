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
}
