using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class ReserveModelTests
{
    private readonly Harness _h = new();

    private async Task<WorkTask> PausedOnClaudeAsync(params string[] reserves)
    {
        await _h.OfficeCommands().ChangeDeskAgentAsync(_h.Office.Id, _h.Desk.Id, AgentEngine.ClaudeCode, "build", "sonnet", default);
        await _h.OfficeCommands().SetReserveModelsAsync(_h.Office.Id, reserves, default);
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().OutOfLimitsAsync(task.Run!.SessionId, EngineLimits.ClaudeCode, Harness.Start.AddHours(3), "The Claude account is over its usage limits.", default);
        return task;
    }

    [Fact]
    public async Task A_task_whose_account_is_over_its_limits_goes_on_with_the_office_s_reserve()
    {
        var task = await PausedOnClaudeAsync("nvidia/qwen3-coder");

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal((WorkTaskStatus.Running, "nvidia/qwen3-coder"), (task.Status, task.ReserveModel));
        Assert.Contains("reserve model nvidia/qwen3-coder goes on", task.Notes[^1].Text, StringComparison.Ordinal);
        var (_, setup, _) = _h.Runtime.Prompts[^1];
        Assert.Equal((AgentEngine.OpenCode, AgentSetups.ReserveAgent, "nvidia/qwen3-coder"), (setup.Engine, setup.AgentName, setup.Model));
    }

    [Fact]
    public async Task Without_a_reserve_the_task_waits_for_its_account()
    {
        var task = await PausedOnClaudeAsync();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal((WorkTaskStatus.Queued, (string?)null), (task.Status, task.ReserveModel));
    }

    [Fact]
    public async Task A_reserve_over_its_own_limits_hands_over_to_the_next_one()
    {
        var task = await PausedOnClaudeAsync("nvidia/qwen3-coder", "mistral/devstral");
        await _h.Runner().RunQueueAsync(default);

        await _h.Signals().OutOfLimitsAsync(task.Run!.SessionId, EngineLimits.OpenCodeProvider("nvidia"), Harness.Start.AddHours(1), "NVIDIA: rate limited.", default);
        await _h.Runner().RunQueueAsync(default);

        Assert.Equal((WorkTaskStatus.Running, "mistral/devstral"), (task.Status, task.ReserveModel));
        Assert.Equal("mistral/devstral", _h.Runtime.Prompts[^1].Setup.Model);
    }

    [Fact]
    public async Task Back_in_the_queue_after_the_reset_the_task_returns_to_its_desk()
    {
        var task = await PausedOnClaudeAsync("nvidia/qwen3-coder");
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().OutOfLimitsAsync(task.Run!.SessionId, EngineLimits.OpenCodeProvider("nvidia"), Harness.Start.AddHours(1), "NVIDIA: rate limited.", default);

        _h.Clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(1));
        await _h.Runner().RunQueueAsync(default);

        Assert.Equal((WorkTaskStatus.Running, (string?)null), (task.Status, task.ReserveModel));
        Assert.Contains(task.Notes, n => n.Text.Contains("own account takes work again", StringComparison.Ordinal));
        var (_, setup, _) = _h.Runtime.Prompts[^1];
        Assert.Equal((AgentEngine.ClaudeCode, "sonnet"), (setup.Engine, setup.Model));
    }

    [Fact]
    public async Task An_opencode_desk_keeps_its_agent_and_skips_reserves_on_its_own_paused_provider()
    {
        await _h.OfficeCommands().ChangeDeskAgentAsync(_h.Office.Id, _h.Desk.Id, AgentEngine.OpenCode, "frontend-dev", "opencode/deepseek-v4.1-flash-free", default);
        await _h.OfficeCommands().SetReserveModelsAsync(_h.Office.Id, ["opencode/big-pickle", "nvidia/qwen3-coder"], default);
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        await _h.Signals().OutOfLimitsAsync(task.Run!.SessionId, EngineLimits.OpenCodeProvider("opencode"), Harness.Start.AddHours(15), "OpenCode: Free limit reached.", default);

        await _h.Runner().RunQueueAsync(default);

        var (_, setup, _) = _h.Runtime.Prompts[^1];
        Assert.Equal(("frontend-dev", "nvidia/qwen3-coder"), (setup.AgentName, setup.Model));
    }
}
