using Openspace.Application.Agents;

namespace Openspace.Application.Tests;

public class InternBoardTests
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly Guid Desk = Guid.NewGuid();
    private static readonly Guid Task1 = Guid.NewGuid();
    private readonly RecordingPublisher _publisher = new();
    private readonly InternBoard _board;

    public InternBoardTests() => _board = new InternBoard(_publisher);

    private Task ArriveAsync(string id, string what = "Find the login form", Guid? task = null) =>
        _board.ArriveAsync(Office, Desk, task ?? Task1, id, what, Harness.Start, default);

    [Fact]
    public async Task An_intern_arrives_once_per_hand_off_and_leaves_when_its_helper_is_back()
    {
        await ArriveAsync("call_1");
        await ArriveAsync("call_1");
        await ArriveAsync("call_2", "Read the tests");

        Assert.Equal(["Find the login form", "Read the tests"], _board.AtDesk(Desk).Select(i => i.What));

        await _board.LeaveAsync(Desk, "call_1", default);
        await _board.LeaveAsync(Desk, "call_unknown", default);

        Assert.Equal(["call_2"], _board.AtDesk(Desk).Select(i => i.Id));
        Assert.Equal([1, 2, 1], _publisher.Interns.Select(d => d.Interns.Count));
    }

    [Fact]
    public async Task Every_intern_leaves_when_the_agent_stops_and_a_new_task_starts_without_them()
    {
        await ArriveAsync("call_1");
        await ArriveAsync("call_2");

        await _board.DismissAsync(Desk, default);
        Assert.Empty(_board.AtDesk(Desk));
        Assert.Empty(_board.Snapshot(Office));

        await ArriveAsync("call_3");
        await ArriveAsync("call_4", task: Guid.NewGuid());
        Assert.Equal(["call_4"], _board.AtDesk(Desk).Select(i => i.Id));
    }
}
