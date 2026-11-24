using Openspace.Application.Agents;
using Openspace.Application.Attention;
using Openspace.Application.World;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>The world in one look: who is in each office, what they do, and who queues at Shiori.</summary>
public sealed class WorldTests
{
    private readonly Harness _h = new();
    private readonly ActivityBoard _activity;
    private readonly InternBoard _interns;

    public WorldTests()
    {
        _activity = new ActivityBoard(_h.Publisher);
        _interns = new InternBoard(_h.Publisher);
    }

    private WorldQueries World() => new(_h.Offices, _h.Tasks, _h.Routines, _activity, _interns, new NeedsYouQueries(_h.Offices, _h.Tasks, _h.Routines, new InMemoryVault(), _h.Clock));

    private Routine AddRoutine(string name, bool enabled = true)
    {
        var routine = Routine.Create(_h.Office, name, "job-offers", RoutineSchedule.Daily(new TimeOnly(8, 0)), "{}", _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        if (!enabled)
        {
            routine.TurnOn(false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        }

        _h.Routines.Add(routine);
        return routine;
    }

    [Fact]
    public async Task Every_desk_is_a_character_with_its_traits_free_when_it_has_no_work()
    {
        var world = await World().GetAsync(default);

        var office = Assert.Single(world.Offices);
        Assert.Equal("E Corp", office.Name);
        var desk = Assert.Single(office.Characters);
        Assert.Equal((CharacterKind.Desk, "Frontend", CharacterState.Free), (desk.Kind, desk.Name, desk.State));
        Assert.Equal(_h.Desk.Id.Value, desk.Id);
        Assert.NotEmpty(desk.Traits);
        Assert.Empty(world.Waiting);
    }

    [Fact]
    public async Task A_desk_at_work_says_what_it_does_and_the_task_it_is_on()
    {
        var task = _h.Enqueue("Add a logout button");
        task.Start(_h.Clock.GetUtcNow());
        await _activity.UpdateAsync(new DeskActivity(_h.Office.Id.Value, _h.Desk.Id.Value, task.Id.Value, ActivityKind.Working, "Write the button", "Editing header.php", null, _h.Clock.GetUtcNow()), default);

        var desk = Assert.Single((await World().GetAsync(default)).Offices[0].Characters);

        Assert.Equal((CharacterState.Working, "Editing header.php", task.Id.Value, "Add a logout button"), (desk.State, desk.Doing, desk.TaskId, desk.TaskTitle));
    }

    [Fact]
    public async Task A_desk_with_a_question_waits_and_brings_it_to_Shiori()
    {
        var task = _h.Enqueue();
        task.Start(_h.Clock.GetUtcNow());
        await _activity.UpdateAsync(new DeskActivity(_h.Office.Id.Value, _h.Desk.Id.Value, task.Id.Value, ActivityKind.Waiting, "Deploy", "Asks to run", "perm-1", _h.Clock.GetUtcNow(), "npm run deploy"), default);

        var world = await World().GetAsync(default);

        Assert.Equal(CharacterState.Waiting, world.Offices[0].Characters[0].State);
        var question = Assert.Single(world.Waiting);
        Assert.Equal(("Question", _h.Desk.Id.Value, "npm run deploy"), (question.Kind, question.CharacterId, question.Title));
    }

    [Fact]
    public async Task A_task_to_review_or_a_failed_one_queues_its_desk_at_Shiori()
    {
        var review = _h.Enqueue("Add a logout button");
        review.Start(_h.Clock.GetUtcNow());
        review.SubmitForReview(_h.Clock.GetUtcNow());

        var world = await World().GetAsync(default);
        Assert.Equal(CharacterState.Waiting, world.Offices[0].Characters[0].State);
        var waiting = Assert.Single(world.Waiting);
        Assert.Equal((nameof(NeedsYouKind.TaskToReview), _h.Desk.Id.Value, review.Id.Value), (waiting.Kind, waiting.CharacterId, waiting.Id));

        review.SendBack(NoteAuthor.Human("elliot"), "Not like that.", _h.Clock.GetUtcNow());
        review.Start(_h.Clock.GetUtcNow());
        review.Fail("The agent stopped.", _h.Clock.GetUtcNow());
        world = await World().GetAsync(default);
        Assert.Equal(CharacterState.Failed, world.Offices[0].Characters[0].State);
        Assert.Equal(nameof(NeedsYouKind.TaskFailed), Assert.Single(world.Waiting).Kind);
    }

    [Fact]
    public async Task Each_routine_is_a_character_of_its_office_working_failed_or_off()
    {
        var offers = AddRoutine("Job offers");
        var run = RoutineRun.Start(offers, false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        _h.Routines.AddRun(run);
        var paper = AddRoutine("Paper");
        var failed = RoutineRun.Start(paper, false, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        failed.Fail("The board could not be read.", _h.Clock.GetUtcNow());
        _h.Routines.AddRun(failed);
        AddRoutine("Old", enabled: false);

        var world = await World().GetAsync(default);

        var routines = world.Offices[0].Characters.Where(c => c.Kind == CharacterKind.Routine).ToDictionary(c => c.Name, c => c.State);
        Assert.Equal(CharacterState.Working, routines["Job offers"]);
        Assert.Equal(CharacterState.Failed, routines["Paper"]);
        Assert.Equal(CharacterState.Off, routines["Old"]);
        var waiting = Assert.Single(world.Waiting);
        Assert.Equal((nameof(NeedsYouKind.RoutineFailed), paper.Id.Value), (waiting.Kind, waiting.CharacterId));
    }

    [Fact]
    public async Task Interns_come_in_for_a_desk_and_work_beside_it()
    {
        var task = _h.Enqueue();
        await _interns.ArriveAsync(_h.Office.Id.Value, _h.Desk.Id.Value, task.Id.Value, "call-1", "Find the login form", _h.Clock.GetUtcNow(), default);

        var characters = (await World().GetAsync(default)).Offices[0].Characters;

        var intern = Assert.Single(characters, c => c.Kind == CharacterKind.Intern);
        Assert.Equal((CharacterState.Working, "Find the login form", (Guid?)_h.Desk.Id.Value), (intern.State, intern.Doing, intern.ForDesk));
        var again = (await World().GetAsync(default)).Offices[0].Characters.Single(c => c.Kind == CharacterKind.Intern);
        Assert.Equal(intern.Id, again.Id);
    }
}
