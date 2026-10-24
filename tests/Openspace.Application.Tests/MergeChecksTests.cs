using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class MergeChecksTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> MergedAsync()
    {
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().FinishedAsync("ses_1", default);
        await _h.Commands().AcceptAsync(task.Id, default);
        await _h.Commands().MergeAsync(task.Id, "main", default);
        return task;
    }

    [Fact]
    public async Task After_a_merge_the_rooms_of_the_merged_work_are_checked_in_a_throwaway_copy()
    {
        _h.Office.SetRoomCheck(_h.Room.Id, "npm ci && npm test", Harness.Start);
        var other = _h.Office.OpenRoom("Docs", Harness.Start);
        _h.Office.SetRoomCheck(other.Id, "npm run lint:docs", Harness.Start);
        var task = await MergedAsync();
        _h.Checks.Answers["npm ci && npm test"] = new CheckRun(1, "2 failing", TimeSpan.FromSeconds(40));

        var outcomes = await _h.MergeChecks().RunAsync(_h.Office.Id, "main", [task.Id], default);

        var outcome = Assert.Single(outcomes);
        Assert.Equal((_h.Room.Name, false, (int?)1, "2 failing"), (outcome.Room, outcome.Passed, outcome.ExitCode, outcome.Output));
        Assert.Equal(("/srv/worktrees/check-1", "npm ci && npm test"), Assert.Single(_h.Checks.Runs));
        Assert.Equal(_h.Workspaces.Scratches, _h.Workspaces.ClosedScratches);
        Assert.Equal($"Checks on main after merging: {_h.Room.Name} FAILED (exit 1).", task.Notes[^1].Text);
    }

    [Fact]
    public async Task Checking_a_branch_by_hand_runs_every_room_s_check_and_leaves_no_notes()
    {
        _h.Office.SetRoomCheck(_h.Room.Id, "dotnet test", Harness.Start);
        var docs = _h.Office.OpenRoom("Docs", Harness.Start);
        _h.Office.SetRoomCheck(docs.Id, "npm run lint:docs", Harness.Start);
        _h.Office.OpenRoom("Without a check", Harness.Start);

        var outcomes = await _h.MergeChecks().RunAsync(_h.Office.Id, "develop", null, default);

        Assert.Equal(["dotnet test", "npm run lint:docs"], outcomes.Select(o => o.Command));
        Assert.All(outcomes, o => Assert.True(o.Passed));
        Assert.Equal(0, _h.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Without_checks_nothing_runs()
    {
        var task = await MergedAsync();

        Assert.Empty(await _h.MergeChecks().RunAsync(_h.Office.Id, "main", [task.Id], default));
        Assert.Empty(_h.Workspaces.Scratches);
    }

    [Fact]
    public void A_check_command_is_trimmed_and_blank_removes_it()
    {
        _h.Office.SetRoomCheck(_h.Room.Id, "  dotnet test  ", Harness.Start);
        Assert.Equal("dotnet test", _h.Room.Check);

        _h.Office.SetRoomCheck(_h.Room.Id, " ", Harness.Start);
        Assert.Null(_h.Room.Check);
        Assert.Throws<Domain.Common.DomainException>(() => _h.Office.SetRoomCheck(_h.Room.Id, new string('x', 1001), Harness.Start));
    }
}
