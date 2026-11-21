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

    private CharacterQueries Queries() => new(_h.Offices, _h.Tasks);

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
