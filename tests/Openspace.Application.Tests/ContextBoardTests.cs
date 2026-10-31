using Openspace.Application.Agents;

namespace Openspace.Application.Tests;

public class ContextBoardTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Desk = Guid.NewGuid();
    private static readonly Guid Task1 = Guid.NewGuid();
    private readonly RecordingPublisher _publisher = new();
    private readonly ContextBoard _board;

    public ContextBoardTests() => _board = new ContextBoard(_publisher);

    private static DateTimeOffset At(int second) => Harness.Start.AddSeconds(second);

    private Task RecordAsync(Guid task, string session, long used, long? window = 100_000, int second = 0) =>
        _board.RecordAsync(Office, Desk, task, session, used, window, At(second), default);

    [Fact]
    public async Task The_energy_left_is_the_share_of_the_window_still_free()
    {
        await RecordAsync(Task1, "ses_1", 25_000);

        var context = Assert.Single(_board.Snapshot(Office));
        Assert.Equal(0.75, context.Left);
        Assert.Null(context.RefreshedAt);
    }

    [Fact]
    public async Task An_unknown_window_leaves_the_energy_unknown_and_a_full_one_never_goes_below_zero()
    {
        await RecordAsync(Task1, "ses_1", 25_000, window: null);
        Assert.Null(_board.ForDesk(Desk)!.Left);

        await RecordAsync(Task1, "ses_1", 150_000, window: 100_000, second: 1);
        Assert.Equal(0, _board.ForDesk(Desk)!.Left);
    }

    [Fact]
    public async Task A_compaction_gives_the_energy_back_and_is_counted()
    {
        await RecordAsync(Task1, "ses_1", 95_000);

        await _board.CompactedAsync(Desk, "ses_1", At(5), default);

        var context = _board.ForDesk(Desk)!;
        Assert.Equal((0L, 1, At(5)), (context.Used, context.Compactions, context.RefreshedAt));
        Assert.Equal(1, context.Left);
    }

    [Fact]
    public async Task A_fresh_conversation_on_the_same_task_gives_the_energy_back_but_a_new_task_just_starts_fresh()
    {
        await RecordAsync(Task1, "ses_1", 90_000);

        await RecordAsync(Task1, "ses_2", 8_000, window: null, second: 3);
        var refreshed = _board.ForDesk(Desk)!;
        Assert.Equal((At(3), 100_000L), (refreshed.RefreshedAt, refreshed.Window));

        await RecordAsync(Guid.NewGuid(), "ses_3", 5_000, second: 6);
        Assert.Null(_board.ForDesk(Desk)!.RefreshedAt);
    }

    [Fact]
    public async Task Only_changes_are_published_and_a_compaction_of_another_conversation_is_ignored()
    {
        await RecordAsync(Task1, "ses_1", 10_000, second: 1);
        await RecordAsync(Task1, "ses_1", 10_000, second: 2);
        await _board.CompactedAsync(Desk, "ses_old", At(3), default);
        await RecordAsync(Task1, "ses_1", 12_000, second: 4);

        Assert.Equal([10_000L, 12_000L], _publisher.Contexts.Select(c => c.Used));
    }
}
