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

    private CharacterQueries Queries() => new(_h.Offices, _h.Tasks, _h.Audit, _h.Clock);

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
