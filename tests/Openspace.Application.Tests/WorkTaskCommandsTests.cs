using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Work;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class WorkTaskCommandsTests
{
    private static readonly DateTimeOffset Start = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _clock = new(Start);
    private readonly InMemoryOffices _offices = new();
    private readonly InMemoryTasks _tasks = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly Office _office = Office.Open("E Corp", Start);
    private readonly Desk _desk;
    private readonly WorkTaskCommands _commands;

    public WorkTaskCommandsTests()
    {
        var room = _office.OpenRoom("Web", _clock.GetUtcNow());
        _desk = _office.AddDesk(room.Id, "Frontend", AgentProfile.Create("frontend-dev"), _clock.GetUtcNow());
        _offices.Add(_office);
        _commands = new WorkTaskCommands(_offices, _tasks, _unitOfWork, _clock);
    }

    [Fact]
    public async Task Assigning_stores_a_queued_task_and_saves()
    {
        var id = await _commands.AssignAsync(_office.Id, _desk.Id, "Fix the footer", null, "elliot", default);

        var task = Assert.Single(_tasks.Items);
        Assert.Equal(id, task.Id);
        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(_clock.GetUtcNow(), task.CreatedAt);
        Assert.Equal(1, _unitOfWork.Saves);
    }

    [Fact]
    public async Task Approving_can_pick_the_desk()
    {
        var id = await _commands.ProposeAsync(_office.Id, "Update the privacy page", null, "elliot", default);

        await _commands.ApproveAsync(id, _desk.Id, default);

        Assert.Equal((WorkTaskStatus.Queued, (DeskId?)_desk.Id), (_tasks.Items[0].Status, _tasks.Items[0].DeskId));
    }

    [Fact]
    public async Task Approving_without_any_desk_is_refused()
    {
        var id = await _commands.ProposeAsync(_office.Id, "Update the privacy page", null, "elliot", default);

        await Assert.ThrowsAsync<DomainException>(() => _commands.ApproveAsync(id, null, default));
    }

    [Fact]
    public async Task Sending_back_records_who_reviewed()
    {
        var id = await _commands.AssignAsync(_office.Id, _desk.Id, "Fix the footer", null, "elliot", default);
        _tasks.Items[0].Start(_clock.GetUtcNow());
        _tasks.Items[0].SubmitForReview(_clock.GetUtcNow());

        await _commands.SendBackAsync(id, "elliot", "Wrong colour.", default);

        var note = Assert.Single(_tasks.Items[0].Notes);
        Assert.Equal("elliot", note.Author.Name);
        Assert.Equal(WorkTaskStatus.Queued, _tasks.Items[0].Status);
    }

    [Fact]
    public async Task Unknown_office_or_task_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _commands.AssignAsync(OfficeId.New(), _desk.Id, "Anything", null, "elliot", default));
        await Assert.ThrowsAsync<NotFoundException>(() => _commands.DiscardAsync(WorkTaskId.New(), default));
    }
}
