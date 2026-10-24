using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Agents;

namespace Openspace.Api.Tests;

public sealed class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public ApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private sealed record Created(Guid Id);

    private sealed record Desk(Guid Id, string Name, string AgentName, string? Model);

    private sealed record Room(Guid Id, string Name, string Context, List<Desk> Desks);

    private sealed record Office(Guid Id, string Name, string? RepositoryPath, List<Room> Rooms);

    private sealed record Note(string AuthorKind, string? AuthorName, string Text);

    private sealed record WorkTask(Guid Id, Guid? DeskId, string Title, string RequestedBy, string Status, List<Note> Notes);

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var response = await _http.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<T> GetAsync<T>(string url) => (await _http.GetFromJsonAsync<T>(url, Json))!;

    private async Task<(Guid Office, Guid Room, Guid Desk)> FurnishAsync()
    {
        var office = await PostAsync<Created>("/api/offices", new { name = "E Corp" });
        var room = await PostAsync<Created>($"/api/offices/{office.Id}/rooms", new { name = "Web" });
        var desk = await PostAsync<Created>(
            $"/api/offices/{office.Id}/rooms/{room.Id}/desks",
            new { name = "Frontend", agentName = "frontend-dev", model = (string?)null });
        return (office.Id, room.Id, desk.Id);
    }

    [Fact]
    public async Task Health_answers_ok()
    {
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task An_office_can_be_furnished_and_read_back()
    {
        var (officeId, roomId, deskId) = await FurnishAsync();
        var put = await _http.PutAsJsonAsync($"/api/offices/{officeId}/rooms/{roomId}/context", new { context = "No inline styles." });
        put.EnsureSuccessStatusCode();

        var office = await GetAsync<Office>($"/api/offices/{officeId}");
        var all = await GetAsync<List<Office>>("/api/offices");

        var room = Assert.Single(office.Rooms);
        Assert.Equal("No inline styles.", room.Context);
        Assert.Equal(deskId, Assert.Single(room.Desks).Id);
        Assert.Single(all);
    }

    private sealed record Spot(int X, int Y);

    private sealed record PlacedDesk(Guid Id, Spot? Spot);

    private sealed record Piece(string Name, int X, int Y);

    private sealed record ArrangedRoom(int? Width, int? Depth, List<PlacedDesk> Desks, List<Piece> Furniture);

    private sealed record ArrangedOffice(List<ArrangedRoom> Rooms);

    [Fact]
    public async Task A_room_takes_its_own_mood_through_the_api()
    {
        var (officeId, roomId, _) = await FurnishAsync();

        (await _http.PutAsJsonAsync($"/api/offices/{officeId}/rooms/{roomId}/mood", new { mood = "townHall" })).EnsureSuccessStatusCode();
        var bad = await _http.PutAsJsonAsync($"/api/offices/{officeId}/rooms/{roomId}/mood", new { mood = "town hall" });

        var office = await GetAsync<JsonElement>($"/api/offices/{officeId}");
        Assert.Equal("townHall", office.GetProperty("rooms")[0].GetProperty("mood").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
    }

    [Fact]
    public async Task A_room_can_be_arranged_by_hand_and_given_back_to_the_automatic_layout()
    {
        var (officeId, roomId, deskId) = await FurnishAsync();
        var url = $"/api/offices/{officeId}/rooms/{roomId}/layout";

        var put = await _http.PutAsJsonAsync(url, new
        {
            width = 10,
            depth = 4,
            desks = new[] { new { deskId, x = 40, y = 20 } },
            furniture = new[] { new { name = "plant", x = 130, y = 66 } },
        });
        var arranged = Assert.Single((await GetAsync<ArrangedOffice>($"/api/offices/{officeId}")).Rooms);
        var outside = await _http.PutAsJsonAsync(url, new { width = 10, depth = 4, furniture = new[] { new { name = "plant", x = 500, y = 0 } } });
        var twice = await _http.PutAsJsonAsync(url, new { width = 10, depth = 4, desks = new[] { new { deskId, x = 0, y = 0 }, new { deskId, x = 40, y = 0 } } });
        (await _http.DeleteAsync(url)).EnsureSuccessStatusCode();
        var cleared = Assert.Single((await GetAsync<ArrangedOffice>($"/api/offices/{officeId}")).Rooms);

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal((10, 4), (arranged.Width, arranged.Depth));
        Assert.Equal(new Spot(40, 20), Assert.Single(arranged.Desks).Spot);
        Assert.Equal(new Piece("plant", 130, 66), Assert.Single(arranged.Furniture));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, twice.StatusCode);
        Assert.Equal(((int?)null, (Spot?)null), (cleared.Width, Assert.Single(cleared.Desks).Spot));
        Assert.Empty(cleared.Furniture);
    }

    private sealed record Door(string Wall, int Offset);

    private sealed record PlannedRoom(int? Left, int? Top, Door? Door, List<int> Windows);

    private sealed record Cut(int Left, int Top, int Width, int Height);

    private sealed record PlannedOffice(int? FloorWidth, int? FloorDepth, List<Piece> Furniture, List<PlannedRoom> Rooms, List<Cut> Cuts);

    [Fact]
    public async Task An_office_floor_can_be_drawn_by_hand_and_given_back()
    {
        var (officeId, roomId, deskId) = await FurnishAsync();
        var url = $"/api/offices/{officeId}/plan";
        object Plan(int left) => new
        {
            width = 20,
            depth = 12,
            furniture = new[] { new { name = "waterCooler", x = 200, y = 150 } },
            cuts = new[] { new { left = 14, top = 6, width = 6, height = 6 } },
            rooms = new[]
            {
                new
                {
                    roomId,
                    left,
                    top = 1,
                    layout = new { width = 8, depth = 4, desks = new[] { new { deskId, x = 20, y = 20 } }, door = new { wall = "Back", offset = 3 }, windows = new[] { 16 } },
                },
            },
        };

        var put = await _http.PutAsJsonAsync(url, Plan(2));
        var planned = await GetAsync<PlannedOffice>($"/api/offices/{officeId}");
        var outside = await _http.PutAsJsonAsync(url, Plan(15));
        (await _http.DeleteAsync(url)).EnsureSuccessStatusCode();
        var cleared = await GetAsync<PlannedOffice>($"/api/offices/{officeId}");

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal((20, 12), (planned.FloorWidth, planned.FloorDepth));
        Assert.Equal(new Piece("waterCooler", 200, 150), Assert.Single(planned.Furniture));
        var room = Assert.Single(planned.Rooms);
        Assert.Equal((2, 1, new Door("Back", 3)), (room.Left, room.Top, room.Door));
        Assert.Equal([16], room.Windows);
        Assert.Equal(new Cut(14, 6, 6, 6), Assert.Single(planned.Cuts));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outside.StatusCode);
        Assert.Equal(((int?)null, (int?)null), (cleared.FloorWidth, Assert.Single(cleared.Rooms).Left));
        Assert.Empty(cleared.Cuts);
    }

    private sealed record Look(string Skin, string Hair, string HairStyle, string Cloth, string? Accessory, string? Outfit, string? Costume);

    private sealed record DressedDesk(Guid Id, Look? Look);

    private sealed record DressedRoom(List<DressedDesk> Desks);

    private sealed record DressedOffice(List<DressedRoom> Rooms);

    private sealed record Engines(bool OpenCode, List<JsonElement> OpenCodeModels, bool ClaudeCode, string Supervisor, string? Reviewer);

    private sealed record EngineDesk(Guid Id, string Engine, string? Model);

    private sealed record EngineRoom(List<EngineDesk> Desks);

    private sealed record EngineOffice(List<EngineRoom> Rooms);

    [Fact]
    public async Task Engines_say_what_can_run_agents_here()
    {
        var engines = await GetAsync<Engines>("/api/engines");

        // In tests OpenCode is unreachable and Claude Code switched off.
        Assert.Equal((false, false, "OpenCode", (string?)null), (engines.OpenCode, engines.ClaudeCode, engines.Supervisor, engines.Reviewer));
        Assert.Empty(engines.OpenCodeModels);
    }

    [Fact]
    public async Task A_desk_can_move_to_Claude_Code_and_back()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var url = $"/api/offices/{officeId}/desks/{deskId}/agent";

        var toClaude = await _http.PutAsJsonAsync(url, new { engine = "ClaudeCode", agentName = "build", model = "sonnet" });
        var claude = Assert.Single(Assert.Single((await GetAsync<EngineOffice>($"/api/offices/{officeId}")).Rooms).Desks);
        var badModel = await _http.PutAsJsonAsync(url, new { engine = "OpenCode", agentName = "build", model = "sonnet" });
        var back = await _http.PutAsJsonAsync(url, new { engine = "OpenCode", agentName = "build", model = "opencode/big-pickle" });
        var openCode = Assert.Single(Assert.Single((await GetAsync<EngineOffice>($"/api/offices/{officeId}")).Rooms).Desks);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.UnprocessableEntity, HttpStatusCode.NoContent), (toClaude.StatusCode, badModel.StatusCode, back.StatusCode));
        Assert.Equal(("ClaudeCode", "sonnet"), (claude.Engine, claude.Model));
        Assert.Equal(("OpenCode", "opencode/big-pickle"), (openCode.Engine, openCode.Model));
    }

    [Fact]
    public async Task A_desk_can_be_dressed_and_undressed()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var url = $"/api/offices/{officeId}/desks/{deskId}/look";

        var put = await _http.PutAsJsonAsync(url, new { skin = "deep", hair = "auburn", hairStyle = "long", cloth = "purple", outfit = "coat" });
        var dressed = Assert.Single(Assert.Single((await GetAsync<DressedOffice>($"/api/offices/{officeId}")).Rooms).Desks).Look;
        var bad = await _http.PutAsJsonAsync(url, new { skin = "deep", hair = "auburn", hairStyle = "long", cloth = "two words" });
        (await _http.DeleteAsync(url)).EnsureSuccessStatusCode();
        var undressed = Assert.Single(Assert.Single((await GetAsync<DressedOffice>($"/api/offices/{officeId}")).Rooms).Desks).Look;

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(new Look("deep", "auburn", "long", "purple", null, "coat", null), dressed);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bad.StatusCode);
        Assert.Null(undressed);
    }

    [Fact]
    public async Task A_task_goes_from_queue_to_discarded_with_notes_by_the_operator()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var task = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Add a logout button" });

        (await _http.PostAsJsonAsync($"/api/tasks/{task.Id}/notes", new { text = "Top right." })).EnsureSuccessStatusCode();
        (await _http.PostAsync($"/api/tasks/{task.Id}/discard", null)).EnsureSuccessStatusCode();

        var read = await GetAsync<WorkTask>($"/api/tasks/{task.Id}");
        Assert.Equal(("Discarded", "elliot"), (read.Status, read.RequestedBy));
        Assert.Equal("elliot", Assert.Single(read.Notes).AuthorName);
    }

    [Fact]
    public async Task A_task_without_desk_is_proposed_and_approved_later()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var task = await PostAsync<Created>("/api/tasks", new { officeId, title = "Rotate the certificates" });

        var proposed = await GetAsync<List<WorkTask>>($"/api/tasks?officeId={officeId}&status=Proposed");
        (await _http.PostAsJsonAsync($"/api/tasks/{task.Id}/approve", new { deskId })).EnsureSuccessStatusCode();

        Assert.Equal(task.Id, Assert.Single(proposed).Id);
        Assert.Equal("Queued", (await GetAsync<WorkTask>($"/api/tasks/{task.Id}")).Status);
    }

    [Fact]
    public async Task Unknown_ids_answer_not_found()
    {
        var response = await _http.GetAsync($"/api/offices/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Broken_rules_answer_unprocessable_with_the_reason()
    {
        var (officeId, _, _) = await FurnishAsync();

        var response = await _http.PostAsJsonAsync($"/api/offices/{officeId}/rooms", new { name = "web" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("already has a room named", problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Browsers_are_told_when_offices_and_tasks_change()
    {
        await using var hub = _app.CreateHubConnection();
        var officeChanged = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var taskChanged = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<Guid>("OfficeChanged", id => officeChanged.TrySetResult(id));
        hub.On<Guid>("TaskChanged", id => taskChanged.TrySetResult(id));
        await hub.StartAsync();

        var (officeId, _, deskId) = await FurnishAsync();
        var task = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Patch the VPN" });

        Assert.Equal(officeId, await officeChanged.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(task.Id, await taskChanged.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task An_office_points_to_a_repository_and_can_have_one_initialized()
    {
        var (officeId, _, _) = await FurnishAsync();
        var folder = Directory.CreateTempSubdirectory("openspace-api-").FullName;
        try
        {
            var relative = await _http.PutAsJsonAsync($"/api/offices/{officeId}/repository", new { path = "repos/e-corp" });
            var plain = await _http.PutAsJsonAsync($"/api/offices/{officeId}/repository", new { path = folder });
            var initialized = await _http.PutAsJsonAsync($"/api/offices/{officeId}/repository", new { path = folder, initialize = true });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, relative.StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, plain.StatusCode);
            var problem = await plain.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("not-a-repository", problem.GetProperty("code").GetString());
            initialized.EnsureSuccessStatusCode();
            Assert.Equal(folder, (await GetAsync<Office>($"/api/offices/{officeId}")).RepositoryPath);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Activity_starts_empty_and_a_task_without_agent_has_no_diff()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var task = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Patch the VPN" });

        var activity = await GetAsync<List<JsonElement>>($"/api/activity?officeId={officeId}");
        var diff = await _http.GetStringAsync($"/api/tasks/{task.Id}/diff");
        var permission = await _http.PostAsJsonAsync($"/api/tasks/{task.Id}/permission", new { allow = true });

        Assert.Empty(activity);
        Assert.Equal(string.Empty, diff);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, permission.StatusCode);
    }

    [Fact]
    public async Task Desk_activity_reaches_browsers_with_readable_kinds()
    {
        await using var hub = _app.CreateHubConnection();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("DeskActivity", a => received.TrySetResult(a));
        await hub.StartAsync();
        var board = _app.Services.GetRequiredService<ActivityBoard>();

        await board.UpdateAsync(
            new DeskActivity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ActivityKind.Stopped, null, "Done: waiting for review", null, DateTimeOffset.UnixEpoch),
            default);

        var activity = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Stopped", activity.GetProperty("kind").GetString());
        Assert.Equal("Done: waiting for review", activity.GetProperty("headline").GetString());
    }

    [Fact]
    public async Task A_task_has_a_log_and_a_downloadable_report()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var task = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Patch the VPN", description = "Before Monday." });

        var log = await GetAsync<List<JsonElement>>($"/api/tasks/{task.Id}/log");
        var report = await _http.GetAsync($"/api/tasks/{task.Id}/report");

        Assert.Equal("Lifecycle", log[0].GetProperty("kind").GetString());
        Assert.Equal("Task created", log[0].GetProperty("summary").GetString());
        Assert.Equal("text/markdown", report.Content.Headers.ContentType?.MediaType);
        Assert.Contains("attachment", report.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        Assert.Contains("# Patch the VPN", await report.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Roles_are_managed_over_http_and_given_to_desks()
    {
        var (officeId, _, deskId) = await FurnishAsync();

        var created = await PostAsync<Created>("/api/roles", new
        {
            name = "Auditor", description = "Reviews changes", instructions = "Never change code.", edit = "Ask", bash = "Deny", web = "Deny",
        });
        var duplicate = await _http.PostAsJsonAsync("/api/roles", new { name = "auditor" });
        (await _http.PutAsJsonAsync($"/api/offices/{officeId}/desks/{deskId}/role", new { roleId = created.Id })).EnsureSuccessStatusCode();
        var inUse = await _http.DeleteAsync($"/api/roles/{created.Id}");

        var roles = await GetAsync<List<JsonElement>>("/api/roles");
        var office = await GetAsync<JsonElement>($"/api/offices/{officeId}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inUse.StatusCode);
        Assert.Equal(("Auditor", "Ask", "Deny"), (roles[0].GetProperty("name").GetString(), roles[0].GetProperty("edit").GetString(), roles[0].GetProperty("bash").GetString()));
        Assert.Equal(created.Id, office.GetProperty("rooms")[0].GetProperty("desks")[0].GetProperty("roleId").GetGuid());
    }

    private static readonly string[] ProcedureSteps = ["Back up first", " ", "Log the change"];

    [Fact]
    public async Task A_room_procedure_is_set_over_http()
    {
        var (officeId, roomId, _) = await FurnishAsync();

        var put = await _http.PutAsJsonAsync($"/api/offices/{officeId}/rooms/{roomId}/procedure", new { steps = ProcedureSteps });
        var office = await GetAsync<JsonElement>($"/api/offices/{officeId}");

        put.EnsureSuccessStatusCode();
        Assert.Equal(["Back up first", "Log the change"], office.GetProperty("rooms")[0].GetProperty("procedure").EnumerateArray().Select(s => s.GetString()));
    }

    private sealed record Settings(string Autonomy, int Seats, bool ClaudeSupervises, string? SupervisorModel, bool ClaudeReviews, string? ReviewModel, string? DraftModel);

    [Fact]
    public async Task Settings_changed_in_Openspace_apply_at_once_and_are_kept_in_a_file()
    {
        var before = await GetAsync<Settings>("/api/settings");

        var put = await _http.PutAsJsonAsync("/api/settings", before with { Autonomy = "Ask", Seats = 3, SupervisorModel = " opus ", DraftModel = "" });
        put.EnsureSuccessStatusCode();
        var after = await GetAsync<Settings>("/api/settings");

        Assert.Equal("Auto", before.Autonomy);
        Assert.Equal(("Ask", 3, "opus", (string?)null), (after.Autonomy, after.Seats, after.SupervisorModel, after.DraftModel));
        Assert.Contains("\"Seats\": 3", await File.ReadAllTextAsync(_app.SettingsPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(13, null)]
    [InlineData(2, "not a model!")]
    public async Task Settings_out_of_bounds_are_refused(int seats, string? model)
    {
        var before = await GetAsync<Settings>("/api/settings");

        var put = await _http.PutAsJsonAsync("/api/settings", before with { Seats = seats, ReviewModel = model });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.False(File.Exists(_app.SettingsPath));
    }

    [Fact]
    public async Task Checks_to_try_by_hand_are_ticked_through_the_api()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var created = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Fix the footer" });
        using (var scope = _app.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetRequiredService<Application.Abstractions.IWorkTaskRepository>();
            var task = (await tasks.FindAsync(new Domain.Work.WorkTaskId(created.Id), default))!;
            task.Start(DateTimeOffset.UtcNow);
            task.AddManualCheck("The footer fits a phone", "Open the site at 360px.", DateTimeOffset.UtcNow);
            task.SubmitForReview(DateTimeOffset.UtcNow);
            await scope.ServiceProvider.GetRequiredService<Application.Abstractions.IUnitOfWork>().SaveChangesAsync(default);
        }

        (await _http.PutAsJsonAsync($"/api/tasks/{created.Id}/checks/1", new { done = true })).EnsureSuccessStatusCode();
        var missing = await _http.PutAsJsonAsync($"/api/tasks/{created.Id}/checks/7", new { done = true });
        var withoutReview = await _http.PostAsJsonAsync($"/api/tasks/{created.Id}/send-back", new { withReview = true });

        var view = await GetAsync<JsonElement>($"/api/tasks/{created.Id}");
        Assert.Equal(JsonValueKind.String, view.GetProperty("manualChecks")[0].GetProperty("checkedAt").ValueKind);
        Assert.Equal((HttpStatusCode.UnprocessableEntity, HttpStatusCode.UnprocessableEntity), (missing.StatusCode, withoutReview.StatusCode));
    }

    [Fact]
    public async Task Desk_usage_starts_empty()
    {
        Assert.Empty(await GetAsync<List<JsonElement>>("/api/usage/desks"));
    }

    [Fact]
    public async Task Merging_everything_with_nothing_accepted_merges_nothing()
    {
        var (officeId, _, _) = await FurnishAsync();

        var outcomes = await PostAsync<List<JsonElement>>($"/api/offices/{officeId}/merge", new { target = "main" });

        Assert.Empty(outcomes);
    }

    [Fact]
    public async Task A_changelog_without_accepted_work_is_refused_with_the_reason()
    {
        var (officeId, _, _) = await FurnishAsync();

        var response = await _http.PostAsJsonAsync($"/api/offices/{officeId}/changelog", new { language = "Italian" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("No work was accepted yet", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_that_is_not_accepted_cannot_catch_up()
    {
        var (officeId, _, deskId) = await FurnishAsync();
        var created = await PostAsync<Created>("/api/tasks", new { officeId, deskId, title = "Fix the footer" });

        var response = await _http.PostAsJsonAsync($"/api/tasks/{created.Id}/catch-up", new { target = "main" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
