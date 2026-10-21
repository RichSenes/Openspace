using Openspace.Application.Agents;

namespace Openspace.Application.Tests;

public class ActivityBoardTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Desk = Guid.NewGuid();
    private static readonly Guid Task1 = Guid.NewGuid();
    private readonly RecordingPublisher _publisher = new();

    private static DeskActivity Working(Guid task, string? step, string? doing, int second = 0) =>
        new(Office, Desk, task, ActivityKind.Working, step, doing, null, Harness.Start.AddSeconds(second));

    [Fact]
    public async Task Repeating_the_same_activity_publishes_once()
    {
        var board = new ActivityBoard(_publisher);

        await board.UpdateAsync(Working(Task1, "Step", null, 1), default);
        await board.UpdateAsync(Working(Task1, "Step", null, 2), default);

        Assert.Single(_publisher.Published);
    }

    [Fact]
    public async Task A_new_task_on_the_desk_starts_from_a_clean_bubble()
    {
        var board = new ActivityBoard(_publisher);
        await board.UpdateAsync(Working(Task1, "Old step", "Old action"), default);

        await board.UpdateAsync(Working(Guid.NewGuid(), null, "Reading the brief"), default);

        var current = Assert.Single(board.Snapshot());
        Assert.Null(current.Step);
        Assert.Equal("Reading the brief", current.Headline);
    }

    [Fact]
    public async Task Snapshot_filters_by_office()
    {
        var board = new ActivityBoard(_publisher);
        await board.UpdateAsync(Working(Task1, "Step", null), default);

        Assert.Single(board.Snapshot(Office));
        Assert.Empty(board.Snapshot(Guid.NewGuid()));
    }
}
