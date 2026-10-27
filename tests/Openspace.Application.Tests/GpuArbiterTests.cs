using Openspace.Application.LocalModels;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

public class GpuArbiterTests
{
    private readonly Harness _h = new();

    public GpuArbiterTests()
    {
        _h.LocalModels.Enabled = true;
        _h.LocalModels.Models.Add(new LocalModel { Id = "gemma", Name = "Gemma", Command = "/opt/models/start-gemma.sh", MemoryMiB = 11000 });
        _h.LocalModels.Models.Add(new LocalModel { Id = "tiny", Name = "Tiny", Command = "/opt/models/start-tiny.sh", MemoryMiB = 4000 });
        _h.LocalModels.Holders.Add(new GpuHolder { Name = "Image studio", Match = "image-studio" });
    }

    private async Task<WorkTask> LocalTaskAsync(string model = "local/gemma")
    {
        await _h.OfficeCommands().ChangeDeskAgentAsync(_h.Office.Id, _h.Desk.Id, AgentEngine.OpenCode, "frontend-dev", model, default);
        var task = _h.Enqueue();
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    /// <summary>The model loads and answers, and the queue hands it the task.</summary>
    private async Task<WorkTask> RunningLocalTaskAsync()
    {
        var task = await LocalTaskAsync();
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
        await _h.Runner().RunQueueAsync(default);
        return task;
    }

    [Fact]
    public async Task A_task_on_a_local_model_waits_for_it_to_load_then_starts()
    {
        var task = await LocalTaskAsync();
        Assert.Equal(WorkTaskStatus.Queued, task.Status);

        await _h.Shift().TickAsync(default);
        Assert.Equal(["gemma"], _h.ModelServer.Started);
        Assert.Equal(GpuState.Loading, _h.Gpu.View().State);

        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Equal((GpuState.Serving, "local/gemma", "Gemma"), (_h.Gpu.View().State, _h.Gpu.View().Model, _h.Gpu.View().ModelName));
    }

    [Fact]
    public async Task Desks_on_cloud_models_never_wait_for_the_card()
    {
        _h.GpuProbe.Programs.Add("/usr/bin/image-studio --serve");
        var task = _h.Enqueue();

        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
    }

    [Fact]
    public async Task The_model_waits_while_the_card_has_too_little_memory_free()
    {
        _h.GpuProbe.Memory = new GpuMemory(4000, 12288);
        await LocalTaskAsync();

        await _h.Shift().TickAsync(default);

        Assert.Empty(_h.ModelServer.Started);
        Assert.Equal(GpuState.WaitingForMemory, _h.Gpu.View().State);
        Assert.Contains("8288 MiB free", _h.Gpu.View().Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_a_holder_runs_the_model_is_not_loaded()
    {
        _h.GpuProbe.Programs.Add("dotnet /apps/image-studio/ImageStudio.dll");
        var task = await LocalTaskAsync();

        await _h.Shift().TickAsync(default);
        await _h.Runner().RunQueueAsync(default);

        Assert.Empty(_h.ModelServer.Started);
        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal(GpuState.Lent, _h.Gpu.View().State);
        Assert.Equal(["Image studio"], _h.Gpu.View().Holders);
    }

    [Fact]
    public async Task A_running_local_task_may_finish_for_a_while_then_goes_back_to_the_queue()
    {
        var task = await RunningLocalTaskAsync();
        Assert.Equal(WorkTaskStatus.Running, task.Status);

        _h.GpuProbe.Programs.Add("dotnet /apps/image-studio/ImageStudio.dll");
        await _h.Shift().TickAsync(default);
        Assert.Equal((GpuState.Yielding, WorkTaskStatus.Running, 0), (_h.Gpu.View().State, task.Status, _h.ModelServer.Stops));

        _h.Clock.Advance(TimeSpan.FromMinutes(3));
        await _h.Shift().TickAsync(default);

        Assert.Equal(WorkTaskStatus.Queued, task.Status);
        Assert.Equal([task.Run!.SessionId], _h.Runtime.Aborted);
        Assert.Contains("graphics card went to Image studio", task.Notes[^1].Text, StringComparison.Ordinal);
        Assert.Equal((GpuState.Lent, (string?)null), (_h.Gpu.View().State, _h.ModelServer.Running));
    }

    [Fact]
    public async Task A_task_that_ends_within_the_grace_lets_the_model_go_at_once()
    {
        var task = await RunningLocalTaskAsync();
        _h.GpuProbe.Programs.Add("image-studio");
        await _h.Shift().TickAsync(default);

        task.SubmitForReview(_h.Clock.GetUtcNow());
        await _h.Shift().TickAsync(default);

        Assert.Equal((GpuState.Lent, 1), (_h.Gpu.View().State, _h.ModelServer.Stops));
        Assert.Empty(_h.Runtime.Aborted);
    }

    [Fact]
    public async Task A_claim_through_the_api_takes_the_card_and_lapses_unless_renewed()
    {
        await RunningLocalTaskAsync();
        _h.Gpu.Claim("Trainer");
        await _h.Shift().TickAsync(default);
        _h.Clock.Advance(TimeSpan.FromMinutes(4));
        await _h.Shift().TickAsync(default);
        Assert.Equal(GpuState.Lent, _h.Gpu.View().State);
        Assert.Equal(["Trainer"], _h.Gpu.View().Holders);

        _h.Clock.Advance(TimeSpan.FromMinutes(7));
        Assert.Empty(_h.Gpu.Claims());
    }

    [Fact]
    public async Task Released_card_goes_back_to_the_waiting_desks()
    {
        var task = await RunningLocalTaskAsync();
        _h.Gpu.Claim("Trainer");
        await _h.Shift().TickAsync(default);
        _h.Clock.Advance(TimeSpan.FromMinutes(4));
        await _h.Shift().TickAsync(default);

        _h.Gpu.Release("trainer");
        await _h.Runner().RunQueueAsync(default);
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
        await _h.Runner().RunQueueAsync(default);

        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Equal(["gemma", "gemma"], _h.ModelServer.Started);
    }

    [Fact]
    public async Task A_model_nobody_uses_is_unloaded_after_a_while()
    {
        var task = await RunningLocalTaskAsync();
        task.SubmitForReview(_h.Clock.GetUtcNow());

        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await _h.Shift().TickAsync(default);
        Assert.Equal(GpuState.Serving, _h.Gpu.View().State);

        _h.Clock.Advance(TimeSpan.FromMinutes(6));
        await _h.Shift().TickAsync(default);

        Assert.Equal((GpuState.Free, (string?)null), (_h.Gpu.View().State, _h.ModelServer.Running));
        Assert.Contains("unused for 10 minutes", _h.Gpu.View().Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_one_local_task_runs_at_a_time()
    {
        var first = await RunningLocalTaskAsync();
        var desk = _h.Office.AddDesk(_h.Room.Id, "Backend", AgentProfile.Create("frontend-dev", "local/gemma"), _h.Clock.GetUtcNow());
        var second = WorkTask.Assign(_h.Office, desk.Id, "Add a login page", null, "elliot", _h.Clock.GetUtcNow());
        _h.Tasks.Add(second);

        await _h.Runner(seats: 2).RunQueueAsync(default);

        Assert.Equal((WorkTaskStatus.Running, WorkTaskStatus.Queued), (first.Status, second.Status));
    }

    [Fact]
    public async Task A_model_that_does_not_load_is_tried_again_later()
    {
        await LocalTaskAsync();
        await _h.Shift().TickAsync(default);

        _h.Clock.Advance(TimeSpan.FromMinutes(4));
        await _h.Runner().RunQueueAsync(default);
        await _h.Shift().TickAsync(default);
        Assert.Equal((GpuState.Failed, 1), (_h.Gpu.View().State, _h.ModelServer.Stops));

        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        await _h.Runner().RunQueueAsync(default);
        await _h.Shift().TickAsync(default);

        Assert.Equal(["gemma", "gemma"], _h.ModelServer.Started);
    }

    [Fact]
    public async Task A_failed_load_says_why_and_can_be_tried_again_at_once()
    {
        await LocalTaskAsync();
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Running = null;
        _h.ModelServer.LastFailure = "not enough graphics memory";
        await _h.Shift().TickAsync(default);

        Assert.Equal(GpuState.Failed, _h.Gpu.View().State);
        Assert.Contains("not enough graphics memory", _h.Gpu.View().Detail, StringComparison.Ordinal);

        _h.Gpu.RetryNow();
        await _h.Runner().RunQueueAsync(default);
        await _h.Shift().TickAsync(default);

        Assert.Equal(["gemma", "gemma"], _h.ModelServer.Started);
    }

    [Fact]
    public async Task A_server_somebody_else_started_is_left_alone()
    {
        _h.ModelServer.Answering = true;
        await LocalTaskAsync();

        await _h.Shift().TickAsync(default);

        Assert.Empty(_h.ModelServer.Started);
        Assert.Contains("Another program already answers", _h.Gpu.View().Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_desk_waiting_for_another_model_gets_it_once_the_card_is_idle()
    {
        var task = await RunningLocalTaskAsync();
        task.SubmitForReview(_h.Clock.GetUtcNow());
        var desk = _h.Office.AddDesk(_h.Room.Id, "Notes", AgentProfile.Create("frontend-dev", "local/tiny"), _h.Clock.GetUtcNow());
        _h.Tasks.Add(WorkTask.Assign(_h.Office, desk.Id, "Sort the notes", null, "elliot", _h.Clock.GetUtcNow()));

        await _h.Runner().RunQueueAsync(default);
        await _h.Shift().TickAsync(default);
        await _h.Shift().TickAsync(default);

        Assert.Equal(["gemma", "tiny"], _h.ModelServer.Started);
    }

    [Fact]
    public async Task Turned_off_it_unloads_what_it_started()
    {
        await RunningLocalTaskAsync();

        _h.LocalModels.Enabled = false;
        await _h.Shift().TickAsync(default);

        Assert.Equal((GpuState.Off, (string?)null), (_h.Gpu.View().State, _h.ModelServer.Running));
    }

    private sealed class FakeModelApi : ILocalModelApi
    {
        public List<LocalAsk> Asked { get; } = [];

        public Task<string> CompleteAsync(LocalAsk ask, CancellationToken cancellationToken)
        {
            Asked.Add(ask);
            return Task.FromResult("Buongiorno.");
        }
    }

    private async Task ServingAsync()
    {
        _h.Gpu.TryLease("local/gemma")?.Dispose();
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
    }

    [Fact]
    public async Task A_routine_leases_the_loaded_model_and_holds_off_tasks_meanwhile()
    {
        Assert.Null(_h.Gpu.TryLease("local/gemma"));
        await ServingAsync();

        using (var lease = _h.Gpu.TryLease("local/gemma"))
        {
            Assert.NotNull(lease);
            Assert.Null(_h.Gpu.TryLease("local/gemma"));
            var task = await LocalTaskAsync();
            Assert.Equal(WorkTaskStatus.Queued, task.Status);

            // A long ask keeps the model loaded past the idle time.
            _h.Clock.Advance(TimeSpan.FromMinutes(15));
            await _h.Shift().TickAsync(default);
            Assert.Equal(GpuState.Serving, _h.Gpu.View().State);
        }

        Assert.NotNull(_h.Gpu.TryLease("local/gemma"));
    }

    [Fact]
    public async Task A_routine_asking_gives_way_to_a_holder_after_the_grace()
    {
        await ServingAsync();
        using var lease = _h.Gpu.TryLease("local/gemma");

        _h.GpuProbe.Programs.Add("image-studio");
        await _h.Shift().TickAsync(default);
        Assert.Equal(GpuState.Yielding, _h.Gpu.View().State);

        _h.Clock.Advance(TimeSpan.FromMinutes(3));
        await _h.Shift().TickAsync(default);
        Assert.Equal((GpuState.Lent, (string?)null), (_h.Gpu.View().State, _h.ModelServer.Running));
    }

    [Fact]
    public async Task Local_chat_waits_for_the_model_then_asks_it()
    {
        var api = new FakeModelApi();
        var chat = new LocalChat(_h.Gpu, api, new LiveOptions<LocalModelOptions>(() => _h.LocalModels), _h.Clock);

        var answer = chat.AskAsync(new LocalAsk("local/gemma", "You write a paper.", "Say good morning."), default);
        Assert.False(answer.IsCompleted);
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
        _h.Clock.Advance(LocalChat.Poll);

        Assert.Equal("Buongiorno.", await answer);
        Assert.Equal("Say good morning.", Assert.Single(api.Asked).User);
        Assert.Equal(["gemma"], _h.ModelServer.Started);
    }

    [Fact]
    public async Task While_it_waits_for_the_card_local_chat_says_who_has_it_once_a_minute_then_that_it_is_free()
    {
        _h.GpuProbe.Programs.Add("image-studio");
        await _h.Shift().TickAsync(default);
        var said = new List<string>();
        var chat = new LocalChat(_h.Gpu, new FakeModelApi(), new LiveOptions<LocalModelOptions>(() => _h.LocalModels), _h.Clock);

        var answer = chat.AskAsync(new LocalAsk("local/gemma", "s", "u", Waiting: said.Add), default);
        for (var i = 0; i < 25; i++)
        {
            _h.Clock.Advance(LocalChat.Poll);
            await Task.Yield();
        }

        Assert.Equal(["Waiting for the graphics card: Image studio is using it", "Waiting for the graphics card: Image studio is using it"], said);

        _h.GpuProbe.Programs.Clear();
        await _h.Shift().TickAsync(default);
        _h.ModelServer.Answering = true;
        await _h.Shift().TickAsync(default);
        _h.Clock.Advance(LocalChat.Poll);

        Assert.Equal("Buongiorno.", await answer);
        Assert.Equal("The graphics card is free: the local model is writing", said[^1]);
    }

    [Fact]
    public async Task Local_chat_gives_up_when_the_card_stays_taken_and_says_who_has_it()
    {
        _h.LocalModels.RoutineWait = TimeSpan.FromSeconds(5);
        _h.GpuProbe.Programs.Add("image-studio");
        await _h.Shift().TickAsync(default);
        var chat = new LocalChat(_h.Gpu, new FakeModelApi(), new LiveOptions<LocalModelOptions>(() => _h.LocalModels), _h.Clock);

        var answer = chat.AskAsync(new LocalAsk("local/gemma", "s", "u"), default);
        _h.Clock.Advance(LocalChat.Poll);
        _h.Clock.Advance(LocalChat.Poll);

        var error = await Assert.ThrowsAsync<TimeoutException>(() => answer);
        Assert.Contains("the card is with Image studio", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chat.AskAsync(new LocalAsk("nvidia/big", "s", "u"), default));
    }
}
