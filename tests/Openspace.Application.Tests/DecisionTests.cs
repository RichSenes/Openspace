using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;

namespace Openspace.Application.Tests;

/// <summary>Shiori's rule on proposals and the person's answers, on a made-up vault and office.</summary>
public sealed class DecisionTests
{
    private readonly Harness _h = new();
    private readonly InMemoryVault _vault;

    public DecisionTests() => _vault = _h.Vault;

    private string Text(string path) => Brain.VaultFormat.Write(_vault.Notes[path]);

    private static string Decision(string status, string offices = "[E Corp]", string title = "Merge order", string extra = "") => $"""
        ---
        kind: decision
        status: {status}
        offices: {offices}
        author: Frontend
        {extra}
        ---
        # {title}

        Merge by approval, oldest first.
        """;

    private Task<IReadOnlyList<string>> AcceptAsync()
    {
        using var acceptor = new DecisionAcceptor(_vault);
        return acceptor.RunAsync(default);
    }

    private NeedsYouQueries Needs() => new(_h.Offices, _h.Tasks, _h.Routines, _vault, _h.Clock);

    private DecisionCommands Commands() => new(_vault, _h.Clock);

    [Fact]
    public async Task A_proposal_about_one_office_is_accepted_by_Shiori_to_be_reviewed()
    {
        _vault.Put("Decisions/2015-06-24-merge-order.md", Decision("proposed"));
        _vault.Put("Offices/tabs.md", Decision("proposed", "[]", "Tabs, not spaces").Replace("kind: decision", "kind: note", StringComparison.Ordinal));

        var accepted = await AcceptAsync();

        Assert.Equal(["Decisions/2015-06-24-merge-order.md", "Offices/tabs.md"], accepted.Order(StringComparer.Ordinal));
        var text = Text("Decisions/2015-06-24-merge-order.md");
        Assert.Contains("status: accepted", text, StringComparison.Ordinal);
        Assert.Contains("accepted_by: Shiori", text, StringComparison.Ordinal);
        Assert.Contains("reviewed: false", text, StringComparison.Ordinal);
        Assert.All(_vault.Writes, w => Assert.Equal("Shiori", w.Author));

        // A second pass finds nothing more to do.
        Assert.Empty(await AcceptAsync());
    }

    [Fact]
    public async Task Proposals_about_many_offices_rules_replacements_and_clashes_stay_for_the_person()
    {
        _vault.Put("Decisions/2015-01-01-merge-order.md", Decision("accepted", "[]", "Merge order"));
        _vault.Put("Decisions/2015-06-24-merge-again.md", Decision("proposed", "[E Corp]", "Merge order!"));
        _vault.Put("Decisions/2015-06-24-releases.md", Decision("proposed", "[E Corp, Allsafe]", "Release on Fridays"));
        _vault.Put("Decisions/2015-06-24-deploys.md", Decision("proposed", "[E Corp]", "Deploy at night", "supersedes: \"[[2015-01-01-deploys]]\""));
        _vault.Put("Constitution-14.md", Decision("proposed", "[]", "Rule 14").Replace("kind: decision", "kind: rule", StringComparison.Ordinal));

        Assert.Empty(await AcceptAsync());
        Assert.Empty(_vault.Writes);

        var waiting = (await Needs().ListAsync(default)).Where(i => i.Kind == NeedsYouKind.DecisionToAccept).ToDictionary(i => i.Note!);
        Assert.Equal(4, waiting.Count);
        Assert.StartsWith("[[Decisions/2015-01-01-merge-order]] is accepted on the same subject", waiting["Decisions/2015-06-24-merge-again.md"].Detail, StringComparison.Ordinal);
        Assert.StartsWith("It is about 2 offices", waiting["Decisions/2015-06-24-releases.md"].Detail, StringComparison.Ordinal);
        Assert.StartsWith("It would replace [[2015-01-01-deploys]]", waiting["Decisions/2015-06-24-deploys.md"].Detail, StringComparison.Ordinal);
        Assert.StartsWith("A rule of the constitution", waiting["Constitution-14.md"].Detail, StringComparison.Ordinal);
        Assert.EndsWith("· proposed by Frontend", waiting["Decisions/2015-06-24-releases.md"].Detail, StringComparison.Ordinal);
        Assert.Equal("E Corp, Allsafe", waiting["Decisions/2015-06-24-releases.md"].OfficeName);
    }

    [Fact]
    public async Task An_agents_proposal_for_every_office_waits_for_the_person_and_says_why()
    {
        _vault.Put("Decisions/2015-06-24-tabs.md", Decision("proposed", "[]", "Tabs everywhere", "from: \"task 01a1\""));
        _vault.Put("Decisions/2015-06-24-spaces.md", Decision("proposed", "[]", "Spaces in docs", "from: \"session 42\""));

        Assert.Equal(["Decisions/2015-06-24-spaces.md"], await AcceptAsync());

        var waiting = Assert.Single(await Needs().ListAsync(default), i => i.Kind == NeedsYouKind.DecisionToAccept);
        Assert.Equal("Decisions/2015-06-24-tabs.md", waiting.Note);
        Assert.StartsWith("It is about every office: Shiori accepts only what stays in one.", waiting.Detail, StringComparison.Ordinal);
        Assert.Equal("Every office", waiting.OfficeName);
    }

    [Fact]
    public async Task A_decision_of_another_office_on_the_same_subject_is_no_clash_and_the_first_of_two_twins_wins()
    {
        _vault.Put("Decisions/2015-01-01-merge-order.md", Decision("accepted", "[Allsafe]"));
        _vault.Put("Decisions/2015-06-24-merge-order.md", Decision("proposed"));
        _vault.Put("Decisions/2015-06-25-merge-order.md", Decision("proposed"));

        Assert.Equal(["Decisions/2015-06-24-merge-order.md"], await AcceptAsync());
    }

    [Fact]
    public async Task Accepted_decisions_wait_to_be_reviewed_in_their_office_and_Fine_puts_them_away()
    {
        _vault.Put("Decisions/2015-06-24-merge-order.md", Decision("proposed"));
        await AcceptAsync();

        var item = Assert.Single(await Needs().ListAsync(default));
        Assert.Equal((NeedsYouKind.DecisionToReview, _h.Office.Id.Value, "E Corp", "Merge order"), (item.Kind, item.OfficeId, item.OfficeName, item.Title));
        Assert.Equal("Accepted by Shiori · proposed by Frontend", item.Detail);
        Assert.Equal(DecisionRules.IdOf("Decisions/2015-06-24-merge-order.md"), item.Id);

        var note = await Commands().DoAsync("Decisions/2015-06-24-merge-order", DecisionVerb.Fine, "operator", null, default);

        Assert.Equal("true", DecisionRules.Field(note!, DecisionRules.Reviewed));
        Assert.Equal(("operator", "reviewed Decisions/2015-06-24-merge-order.md"), (_vault.Writes[^1].Author, _vault.Writes[^1].Summary));
        Assert.Empty(await Needs().ListAsync(default));
    }

    [Fact]
    public async Task Undoing_rejects_the_decision_and_tells_the_desk_that_proposed_it_in_its_journal()
    {
        _vault.Put("Decisions/2015-06-24-merge-order.md", Decision("proposed"));
        _vault.Put("Inhabitants/Frontend/journal.md", "---\nkind: journal\nauthor: Frontend\n---\n# Frontend's journal\n\n## 2015-06-23\n\n- 10:00 · Logout: added the button.\n");
        await AcceptAsync();

        await Commands().DoAsync("Decisions/2015-06-24-merge-order.md", DecisionVerb.Undo, "operator", "We merge by size here.", default);

        Assert.Contains("status: rejected", Text("Decisions/2015-06-24-merge-order.md"), StringComparison.Ordinal);
        var journal = Text("Inhabitants/Frontend/journal.md");
        Assert.EndsWith(
            "- 10:00 · Logout: added the button.\n\n## 2015-06-24\n\n- 09:00 · Shiori: the person undid [[Decisions/2015-06-24-merge-order|Merge order]]. Why: We merge by size here.\n",
            journal, StringComparison.Ordinal);
        Assert.Equal("Shiori", _vault.Writes[^1].Author);
        Assert.Empty(await Needs().ListAsync(default));
    }

    [Fact]
    public async Task The_person_accepts_or_rejects_what_Shiori_left_and_a_journal_is_started_when_there_is_none()
    {
        _vault.Put("Decisions/a.md", Decision("proposed", "[E Corp, Allsafe]"));
        _vault.Put("Decisions/b.md", Decision("proposed", "[E Corp, Allsafe]", "Other"));

        var accepted = await Commands().DoAsync("Decisions/a.md", DecisionVerb.Accept, "operator", null, default);
        await Commands().DoAsync("Decisions/b.md", DecisionVerb.Reject, "operator", null, default);

        Assert.Equal(("accepted", "operator", "true"), (accepted!.Status, DecisionRules.Field(accepted, DecisionRules.AcceptedBy), DecisionRules.Field(accepted, DecisionRules.Reviewed)));
        Assert.Contains("status: rejected", Text("Decisions/b.md"), StringComparison.Ordinal);
        Assert.EndsWith("# Frontend's journal\n\n## 2015-06-24\n\n- 09:00 · Shiori: the person did not accept [[Decisions/b|Other]]. No reason given.\n", Text("Inhabitants/Frontend/journal.md"), StringComparison.Ordinal);
        Assert.Empty(await Needs().ListAsync(default));
    }

    [Fact]
    public async Task An_answer_that_does_not_fit_the_status_is_refused_and_a_missing_note_is_nothing()
    {
        _vault.Put("Decisions/a.md", Decision("accepted"));

        var refused = await Assert.ThrowsAsync<RejectedException>(() => Commands().DoAsync("Decisions/a.md", DecisionVerb.Accept, "operator", null, default));
        Assert.Equal(DecisionCommands.WrongStatusCode, refused.Code);
        Assert.Null(await Commands().DoAsync("Decisions/none.md", DecisionVerb.Fine, "operator", null, default));
    }

    private Routine ReviewsRoutine()
    {
        var routine = Routine.Create(_h.Office, "Reviews", "decision-reviews", RoutineSchedule.Daily(new TimeOnly(7, 0)), """{"channel":"STEWARD"}""", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.Add(routine);
        return routine;
    }

    private async Task<RoutineRun> RunAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await new RoutineRunner(_h.Routines, _h.Offices, _h.Progress, [new DecisionReviews(_vault, _h.Offices)], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance)
            .RunWaitingAsync(default);
        return _h.Routines.Runs.Where(r => r.RoutineId == routine.Id).OrderBy(r => r.StartedAt).Last();
    }

    [Fact]
    public async Task Decisions_due_come_back_once_the_routine_has_looked_and_Keep_moves_them_three_months_on()
    {
        _vault.Put("Decisions/merge-order.md", Decision("accepted", extra: "review: 2015-06-30"));
        _vault.Put("Decisions/theirs.md", Decision("accepted", "[Allsafe]", "Theirs", "review: 2015-06-01"));
        _vault.Put("Decisions/later.md", Decision("accepted", "[]", "Later", "review: 2015-09-01"));
        var routine = ReviewsRoutine();

        // Before its date: nothing to say, nothing in the inbox.
        Assert.Equal("No decision is due for review.", (await RunAsync(routine)).Summary);
        Assert.Empty(_h.Outbox.Sent);
        Assert.Empty(await Needs().ListAsync(default));

        _h.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Empty(await Needs().ListAsync(default));
        var run = await RunAsync(routine);

        Assert.Equal("1 decision due for review.", run.Summary);
        var (_, text, _) = Assert.Single(_h.Outbox.Sent);
        Assert.StartsWith("Decisions to look at again (1)\n• Merge order, due 2015-06-30", text, StringComparison.Ordinal);
        var due = Assert.Single(await Needs().ListAsync(default));
        Assert.Equal((NeedsYouKind.DecisionDue, "Decisions/merge-order.md", "Review was due 2015-06-30"), (due.Kind, due.Note, due.Detail));

        var kept = await Commands().DoAsync("Decisions/merge-order.md", DecisionVerb.Keep, "operator", null, default);

        Assert.Equal(new DateOnly(2015, 10, 1), kept!.Review);
        Assert.Empty(await Needs().ListAsync(default));
    }

    [Fact]
    public async Task Supersede_retires_a_decision_due_for_review()
    {
        _vault.Put("Decisions/merge-order.md", Decision("accepted", extra: "review: 2015-06-01"));
        await RunAsync(ReviewsRoutine());
        Assert.Single(await Needs().ListAsync(default));

        await Commands().DoAsync("Decisions/merge-order.md", DecisionVerb.Supersede, "operator", null, default);

        Assert.Contains("status: superseded", Text("Decisions/merge-order.md"), StringComparison.Ordinal);
        Assert.Empty(await Needs().ListAsync(default));
    }
}

/// <summary>The weekly recap kept in the vault, week by week and gathered by month.</summary>
public sealed class DigestTests
{
    private readonly InMemoryVault _vault = new();

    private string Text(string path) => Brain.VaultFormat.Write(_vault.Notes[path]);

    [Fact]
    public async Task A_week_writes_its_digest_and_adds_itself_to_the_month_replacing_its_own_part_and_leaving_other_routines_parts()
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
        await Digests.WriteAsync(_vault, new DateOnly(2026, 11, 15), "Week one.", "routine Week", culture, default);
        await Digests.WriteAsync(_vault, new DateOnly(2026, 11, 22), "Week two.", "routine Week", culture, default);
        await Digests.WriteAsync(_vault, new DateOnly(2026, 11, 15), "Week one, again.", "routine Week", culture, default);

        await Digests.WriteAsync(_vault, new DateOnly(2026, 11, 15), "The shop's week.", "routine Shop week", culture, default);

        Assert.Equal("---\nkind: digest\nfrom: routine Week\nauthor: Shiori\n---\n# 2026-W46\n\n## Week\n\nWeek one, again.\n\n## Shop week\n\nThe shop's week.\n", Text("Digests/2026-W46.md"));
        var month = (await _vault.ReadAsync("Digests/2026-11.md", default))!;
        Assert.Equal(("digest", "November 2026"), (month.Kind, month.Title));
        Assert.Equal("# November 2026\n\n## [[2026-W46]] · Week\n\nWeek one, again.\n\n## [[2026-W47]] · Week\n\nWeek two.\n\n## [[2026-W46]] · Shop week\n\nThe shop's week.\n", month.Body);
        Assert.All(_vault.Writes, w => Assert.Equal("Shiori", w.Author));
    }
}
