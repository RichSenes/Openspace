using Openspace.Application.Characters;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class CharacterTests
{
    private readonly Harness _h = new();

    /// <summary>The audit log of the task so far, as the database would record it.</summary>
    private void Log(WorkTask task)
    {
        foreach (var e in task.DomainEvents)
        {
            if (AuditTrail.FromDomainEvent(e) is { } entry)
            {
                _h.Audit.Add(entry);
            }
        }

        task.ClearDomainEvents();
    }

    private WorkTask Task(Desk desk, string title = "Old work")
    {
        var task = WorkTask.Assign(_h.Office, desk.Id, title, null, "elliot", _h.Clock.GetUtcNow());
        _h.Tasks.Add(task);
        task.Start(_h.Clock.GetUtcNow());
        task.AttachRun(AgentRun.Create($"ses_{task.Key}", $"/tmp/{task.Key}", $"openspace/{task.Key}"), _h.Clock.GetUtcNow());
        return task;
    }

    private void Changed(WorkTask task, string file) =>
        _h.Audit.Add(AuditEntry.Record(task.Id, _h.Clock.GetUtcNow(), AuditKind.Change, $"Changed {file}", succeeded: true));

    private WorkTask Finished(Desk desk, bool merged = false, string title = "Old work")
    {
        var task = Task(desk, title);
        task.SubmitForReview(_h.Clock.GetUtcNow());
        task.Accept(_h.Clock.GetUtcNow());
        if (merged)
        {
            task.RecordMerge("main", _h.Clock.GetUtcNow());
        }

        Log(task);
        return task;
    }

    private Desk Desk(string name) => _h.Office.AddDesk(_h.Room.Id, name, AgentProfile.Create("dev"), _h.Clock.GetUtcNow());

    private CharacterQueries Queries() => new(_h.Offices, _h.Tasks, _h.Audit, new InMemoryAgreements(), _h.Clock);

    /// <summary>A desk whose traits move neither its baseline nor its changes.</summary>
    private Desk Plain(string name) =>
        _h.Office.AddDesk(_h.Room.Id, name, AgentProfile.Create("dev"), _h.Clock.GetUtcNow(), personality: Personality.Of([Trait.Curious, Trait.Chatty]));

    private async Task<MoodLine> MoodOfAsync(Desk desk) =>
        (await Queries().GetAsync(_h.Office.Id, default)).Characters.Single(c => c.DeskId == desk.Id.Value).Mood;

    [Fact]
    public async Task Merged_work_lifts_the_mood_and_work_sent_back_or_failed_lowers_it()
    {
        var desk = Plain("Screens");
        Finished(desk, merged: true, title: "Add the header");
        _h.Clock.Advance(TimeSpan.FromMinutes(5));

        var line = await MoodOfAsync(desk);
        Assert.Equal((Mood.Neutral, Mood.Neutral + 8), (line.Baseline, line.Value));
        Assert.Equal("“Add the header” was merged", Assert.Single(line.History).Reason);

        var back = Task(desk, "Fix the footer");
        back.SubmitForReview(_h.Clock.GetUtcNow());
        back.SendBack(NoteAuthor.Reviewer("Review"), "The footer still overlaps.", _h.Clock.GetUtcNow());
        Log(back);
        var failed = Task(desk, "Tidy the icons");
        failed.Fail("It broke.", _h.Clock.GetUtcNow());
        Log(failed);

        Assert.Equal(Mood.Neutral + 8 - 6 - 10, (await MoodOfAsync(desk)).Value);
    }

    [Fact]
    public async Task The_mood_rests_halfway_back_to_its_baseline_each_night()
    {
        var desk = Plain("Screens");
        var failed = Task(desk, "Tidy the icons");
        failed.Fail("It broke.", _h.Clock.GetUtcNow());
        Log(failed);
        Assert.Equal(Mood.Neutral - 10, (await MoodOfAsync(desk)).Value);

        _h.Clock.Advance(TimeSpan.FromDays(1));
        var line = await MoodOfAsync(desk);

        Assert.Equal(Mood.Neutral - 5, line.Value);
        Assert.Equal((Mood.Rested, 5), (line.History[^1].Reason, line.History[^1].Change));

        _h.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(Mood.Neutral, (await MoodOfAsync(desk)).Value);
    }

    [Fact]
    public void Traits_set_the_baseline_and_how_much_events_move_it_never_dice()
    {
        Assert.Equal(Mood.Neutral + 10, Mood.BaselineOf([Trait.Sunny, Trait.Curious]));
        Assert.Equal(Mood.Neutral - 10, Mood.BaselineOf([Trait.Shy, Trait.DryHumoured]));
        Assert.Equal(-5, Mood.ChangeOf(LifeEventKind.Failed, [Trait.Calm, Trait.Curious]));
        Assert.Equal(-9, Mood.ChangeOf(LifeEventKind.SentBack, [Trait.Meticulous, Trait.Shy]));
        Assert.Equal(-3, Mood.ChangeOf(LifeEventKind.SentBack, [Trait.Sunny, Trait.Shy]));
        Assert.Equal(8, Mood.ChangeOf(LifeEventKind.Merged, [Trait.Sunny, Trait.Shy]));
    }

    private async Task<RelationshipView?> TieAsync(Desk a, Desk b) =>
        (await Queries().GetAsync(_h.Office.Id, default)).Relationships
            .SingleOrDefault(r => new[] { r.FirstId, r.SecondId }.Order().SequenceEqual(new[] { a.Id.Value, b.Id.Value }.Order()));

    [Fact]
    public async Task Two_desks_on_the_same_files_within_days_grow_closer()
    {
        var screens = Plain("Screens");
        var one = Task(_h.Desk, "Add the header");
        Changed(one, "header.tsx");
        Changed(one, "menu.tsx");
        var two = Task(screens, "Restyle the header");
        Changed(two, "header.tsx");

        var tie = await TieAsync(_h.Desk, screens);

        Assert.Equal(Relationships.SameFilesChange, tie!.Affinity);
        Assert.Equal("Frontend and Screens both worked on header.tsx", Assert.Single(tie.History).Text);
    }

    [Fact]
    public async Task Files_touched_weeks_apart_or_by_one_desk_make_no_tie()
    {
        var screens = Plain("Screens");
        Changed(Task(_h.Desk, "Add the header"), "header.tsx");
        Changed(Task(_h.Desk, "Fix the header"), "header.tsx");
        _h.Clock.Advance(TimeSpan.FromDays(10));
        Changed(Task(screens, "Restyle the header"), "header.tsx");

        Assert.Null(await TieAsync(_h.Desk, screens));
    }

    [Fact]
    public async Task Work_that_goes_in_where_another_desk_failed_is_remembered()
    {
        var screens = Plain("Screens");
        var failed = Task(_h.Desk, "Fix the cart");
        Changed(failed, "cart.ts");
        failed.Fail("Tests fail.", _h.Clock.GetUtcNow());
        Log(failed);
        _h.Clock.Advance(TimeSpan.FromDays(5));
        var fix = Task(screens, "Make the cart work");
        Changed(fix, "cart.ts");
        fix.SubmitForReview(_h.Clock.GetUtcNow());
        fix.Accept(_h.Clock.GetUtcNow());
        fix.RecordMerge("main", _h.Clock.GetUtcNow());
        Log(fix);

        var tie = await TieAsync(_h.Desk, screens);

        var fixed_ = Assert.Single(tie!.History, e => e.Kind == TieKind.FixedFailure);
        Assert.Equal("Screens's “Make the cart work” went in where Frontend's “Fix the cart” had failed", fixed_.Text);
        Assert.Equal(Relationships.FixChange, tie.Affinity);
    }

    [Fact]
    public async Task A_merge_conflict_between_two_desks_cools_them()
    {
        var screens = Plain("Screens");
        var first = Task(screens, "Rename the routes");
        Changed(first, "routes.ts");
        first.SubmitForReview(_h.Clock.GetUtcNow());
        first.Accept(_h.Clock.GetUtcNow());
        first.RecordMerge("main", _h.Clock.GetUtcNow());
        Log(first);
        _h.Clock.Advance(TimeSpan.FromDays(4));
        var second = Task(_h.Desk, "Add a route");
        Changed(second, "routes.ts");
        second.SubmitForReview(_h.Clock.GetUtcNow());
        second.Accept(_h.Clock.GetUtcNow());
        second.ReopenToCatchUp(NoteAuthor.Human("elliot"), "It conflicts.", _h.Clock.GetUtcNow());
        Log(second);

        var tie = await TieAsync(_h.Desk, screens);

        Assert.Equal(Relationships.ConflictChange, tie!.Affinity);
        Assert.Contains("no longer merged after Screens's “Rename the routes” went in", Assert.Single(tie.History).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notes_left_on_another_desks_work_count_as_a_review_and_the_person_is_never_in_a_tie()
    {
        var screens = Plain("Screens");
        var task = Task(_h.Desk, "Add the header");
        task.AddNote(NoteAuthor.FromDesk(screens.Id), "The header misses its aria label.", _h.Clock.GetUtcNow());
        task.AddNote(NoteAuthor.Human("elliot"), "Looks fine to me.", _h.Clock.GetUtcNow());

        var board = await Queries().GetAsync(_h.Office.Id, default);

        var tie = Assert.Single(board.Relationships);
        Assert.Equal((TieKind.Reviewed, Relationships.ReviewChange), (Assert.Single(tie.History).Kind, tie.Affinity));
        Assert.Equal("Screens left notes on Frontend's “Add the header”", tie.History[0].Text);
    }

    [Fact]
    public async Task Relationships_forget_after_a_month()
    {
        var screens = Plain("Screens");
        Changed(Task(_h.Desk, "Add the header"), "header.tsx");
        Changed(Task(screens, "Restyle the header"), "header.tsx");
        _h.Clock.Advance(Relationships.Window + TimeSpan.FromDays(1));

        Assert.Null(await TieAsync(_h.Desk, screens));
    }

    private sealed class Engines : Functions.IEngineText
    {
        public string? Answer { get; set; }

        public List<string> Asked { get; } = [];

        public System.Threading.Tasks.Task<string> AskAsync(Functions.ModelEngine engine, string? model, string instructions, string text, CancellationToken cancellationToken)
        {
            Asked.Add(text);
            return Answer is null ? throw new InvalidOperationException("No engine here") : System.Threading.Tasks.Task.FromResult(Answer);
        }
    }

    private sealed class NoChat : LocalModels.ILocalChat
    {
        public System.Threading.Tasks.Task<string> AskAsync(LocalModels.LocalAsk ask, CancellationToken cancellationToken) => throw new TimeoutException("The card is busy.");
    }

    private readonly Engines _engines = new();

    /// <summary>The chronicle on Claude Code, so the fake engine writes it; local models stay off.</summary>
    private Chronicle Chronicle() => new(
        _h.Offices, _h.Tasks, _h.Audit, _h.Vault, new NoChat(), _engines,
        new LiveOptions<Functions.FunctionModelOptions>(() => new() { Journals = new() { Engine = Functions.ModelEngine.ClaudeCode, Model = "haiku" } }),
        new LiveOptions<LocalModels.LocalModelOptions>(() => new()), _h.Clock);

    private DateOnly Today => DateOnly.FromDateTime(_h.Clock.GetUtcNow().UtcDateTime);

    private void ADayOfWork()
    {
        var screens = Plain("Screens");
        Finished(screens, merged: true, title: "Add the header");
        var failed = Task(_h.Desk, "Tidy the icons");
        failed.Fail("It broke.", _h.Clock.GetUtcNow());
        Log(failed);
    }

    [Fact]
    public async Task The_chronicle_is_written_by_the_model_from_facts_and_kept_in_the_vault()
    {
        ADayOfWork();
        _engines.Answer = "- Screens had a good day: the header is in.\n\nFrontend's icons will have to wait.";

        var day = await Chronicle().WriteAsync(Today, "English", default);

        Assert.True(day.ByModel);
        Assert.Equal(["Screens had a good day: the header is in.", "Frontend's icons will have to wait."], Assert.Single(day.Sections).Lines);
        Assert.Contains("Screens: “Add the header” was merged.", _engines.Asked.Single(), StringComparison.Ordinal);
        Assert.Contains("Frontend: “Tidy the icons” failed.", _engines.Asked.Single(), StringComparison.Ordinal);
        var note = _h.Vault.Notes[Characters.Chronicle.PathOf(Today)];
        Assert.Contains("## E Corp", note.Body, StringComparison.Ordinal);
        Assert.Contains("- Screens had a good day: the header is in.", note.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_model_the_chronicle_tells_the_plain_facts()
    {
        ADayOfWork();

        var day = await Chronicle().WriteAsync(Today, "English", default);

        Assert.False(day.ByModel);
        Assert.Contains("Screens: “Add the header” was merged.", Assert.Single(day.Sections).Lines);
    }

    [Fact]
    public async Task A_day_already_written_is_read_back_from_the_vault_without_asking_again()
    {
        ADayOfWork();
        await Chronicle().WriteAsync(Today, "English", default);

        var again = await Chronicle().DayAsync(Today, "English", default);

        Assert.Single(_engines.Asked, _ => true);
        Assert.Equal("E Corp", Assert.Single(again.Sections).Office);
        Assert.Contains("Screens: “Add the header” was merged.", again.Sections[0].Lines);
    }

    [Fact]
    public async Task Yesterdays_chronicle_is_kept_once_a_day_after_its_hour()
    {
        ADayOfWork();
        var day = Today;
        var options = new ChronicleOptions { At = new TimeOnly(1, 0) };
        _h.Clock.Advance(new DateTimeOffset(2015, 6, 25, 0, 30, 0, TimeSpan.Zero) - _h.Clock.GetUtcNow());

        Assert.Null(await Chronicle().KeepYesterdayAsync(options, default));

        _h.Clock.Advance(TimeSpan.FromHours(1));
        var written = await Chronicle().KeepYesterdayAsync(options, default);

        Assert.Equal(day, written?.Day);
        Assert.True(_h.Vault.Notes.ContainsKey(Characters.Chronicle.PathOf(day)));
        Assert.Null(await Chronicle().KeepYesterdayAsync(options, default));
        Assert.Null(await Chronicle().KeepYesterdayAsync(new ChronicleOptions { Enabled = false }, default));
    }

    [Fact]
    public async Task The_kept_chronicle_never_goes_to_a_free_online_model()
    {
        ADayOfWork();
        _engines.Answer = "Gossip.";
        _h.Clock.Advance(TimeSpan.FromDays(1));
        var free = new Chronicle(
            _h.Offices, _h.Tasks, _h.Audit, _h.Vault, new NoChat(), _engines,
            new LiveOptions<Functions.FunctionModelOptions>(() => new() { Journals = new() { Engine = Functions.ModelEngine.OpenCode, Model = "opencode/big-pickle-free" } }),
            new LiveOptions<LocalModels.LocalModelOptions>(() => new()), _h.Clock);

        var written = await free.KeepYesterdayAsync(new ChronicleOptions { At = TimeOnly.MinValue }, default);

        Assert.False(written!.ByModel);
        Assert.Empty(_engines.Asked);
    }

    [Fact]
    public async Task The_weeks_roundup_takes_a_line_a_day_per_office_from_the_vault()
    {
        for (var d = 0; d < 7; d++)
        {
            var date = Today.AddDays(-d);
            _h.Vault.Put(Characters.Chronicle.PathOf(date), $"# Chronicle\n\n## E Corp\n\n- Day {d} first.\n- Day {d} second.\n");
        }

        _h.Vault.Put(Characters.Chronicle.PathOf(Today.AddDays(-2)), "# Chronicle\n\n## E Corp\n\n- Day 2 first.\n\n## Allsafe\n\n- Allsafe met.\n");

        var week = await Characters.Chronicle.RoundupAsync(_h.Vault, Today, 7, default);

        Assert.Equal(["E Corp", "Allsafe"], week.Select(s => s.Office));
        Assert.Equal(["Day 4 first.", "Day 3 first.", "Day 2 first.", "Day 1 first.", "Day 0 first."], week[0].Lines);
        Assert.Equal(["Allsafe met."], week[1].Lines);
    }

    [Fact]
    public async Task A_quiet_day_has_no_lines_and_a_birthday_is_news()
    {
        var quiet = await Chronicle().WriteAsync(Today, "English", default);
        Assert.True(quiet.IsEmpty);

        _h.Office.SetDeskBirthday(_h.Desk.Id, Birthday.On(Today.Month, Today.Day), _h.Clock.GetUtcNow());
        var party = await Chronicle().WriteAsync(Today, "English", default);

        Assert.Equal(["It is Frontend's birthday."], Assert.Single(party.Sections).Lines);
    }

    [Fact]
    public void The_paper_prints_the_chronicle_under_its_own_heading_unless_turned_off()
    {
        var day = new ChronicleDay(Today, "Chronicle/x.md", [new("E Corp", ["Screens had a good day."]), new("Allsafe", ["Quiet."])], false);
        var lines = Routines.Kinds.MorningPaper.ChronicleLines(day);
        Assert.Equal(["E Corp: Screens had a good day.", "Allsafe: Quiet."], lines);

        var settings = Routines.Kinds.PaperSettings.Read(System.Text.Json.JsonDocument.Parse(new Routines.Kinds.MorningPaper(null!, null!, null!, null!, null!, null!).DefaultSettings).RootElement);
        var html = Routines.Kinds.PaperPage.Render(settings, System.Globalization.CultureInfo.GetCultureInfo("it-IT"), 1, Today,
            new Routines.Kinds.PaperOpening("Buongiorno", ""), null, null, [], chronicle: lines);
        Assert.Contains("<h2>Vita d&#39;ufficio</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<li>E Corp: Screens had a good day.</li>", html, StringComparison.Ordinal);

        Assert.True(Routines.Kinds.MorningPaper.WantsChronicle(System.Text.Json.JsonDocument.Parse("{}").RootElement));
        Assert.False(Routines.Kinds.MorningPaper.WantsChronicle(System.Text.Json.JsonDocument.Parse("{\"chronicle\":false}").RootElement));
    }

    [Fact]
    public async Task Another_desks_events_do_not_move_its_mood()
    {
        var desk = Plain("Screens");
        var failed = Task(_h.Desk, "Tidy the icons");
        failed.Fail("It broke.", _h.Clock.GetUtcNow());
        Log(failed);

        Assert.Empty((await MoodOfAsync(desk)).History);
    }

    [Fact]
    public void A_desk_becomes_its_rooms_expert_after_three_finished_tasks_and_the_most_of_them()
    {
        var screens = Desk("Screens");
        Finished(screens);
        Finished(screens);
        Finished(_h.Desk);
        Assert.Null(Seniority.ExpertOf(_h.Office, _h.Room, _h.Tasks.Items));

        Finished(screens);
        Finished(_h.Desk);
        Finished(_h.Desk);

        var seniority = Seniority.Of(_h.Office, _h.Tasks.Items);
        Assert.Equal(3, seniority.Single(s => s.Desk == screens.Id).Done);
        Assert.Equal(3, seniority.Single(s => s.Desk == _h.Desk.Id).Done);

        // A tie goes to the desk whose name comes first: never at random.
        Assert.Equal(_h.Desk.Id, Seniority.ExpertOf(_h.Office, _h.Room, _h.Tasks.Items));
    }

    [Fact]
    public void Work_not_finished_does_not_count_for_seniority()
    {
        Task(_h.Desk);
        var failed = Task(_h.Desk);
        failed.Fail("It broke.", _h.Clock.GetUtcNow());

        Assert.Equal(0, Seniority.Of(_h.Office, _h.Tasks.Items).Single().Done);
    }

    [Fact]
    public async Task The_character_monitor_names_each_rooms_expert()
    {
        for (var i = 0; i < 3; i++)
        {
            Finished(_h.Desk);
        }

        var board = await Queries().GetAsync(_h.Office.Id, default);

        var expert = Assert.Single(board.Experts);
        Assert.Equal(("Web", "Frontend", 3), (expert.RoomName, expert.DeskName, expert.Done));
        var character = Assert.Single(board.Characters);
        Assert.True(Assert.Single(character.Seniority).Expert);
        Assert.Equal(_h.Desk.Traits, character.Traits);
    }
}
