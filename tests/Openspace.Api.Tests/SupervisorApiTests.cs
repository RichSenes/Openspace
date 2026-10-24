using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;

namespace Openspace.Api.Tests;

public sealed class SupervisorApiTests : IDisposable
{
    /// <summary>Answers every plan with the same two tasks; everything else is not needed here.</summary>
    internal sealed class PlanningRuntime : IAgentRuntime
    {
        public Task<string> PlanAsync(string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken) =>
            Task.FromResult("""
                {"title":"Contact page","tasks":[
                  {"title":"Build the contact form","description":"In the theme.","desk":"Frontend"},
                  {"title":"Check it on staging","description":"Manual check.","desk":"Somebody"}]}
                """);

        public Task<bool> HasAgentAsync(AgentProfile agent, string workspacePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReviewAsync(string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string> CreateSessionAsync(string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken) => throw new NotSupportedException();
        public AgentEngine EngineOf(string sessionId) => throw new NotSupportedException();
        public Task PromptAsync(string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ReplyToPermissionAsync(string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private readonly OpenspaceApp _app = new(new PlanningRuntime());
    private readonly HttpClient _http;

    public SupervisorApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private async Task<JsonElement> PostAsync(string url, object body)
    {
        var response = await _http.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task The_supervisor_plans_over_http_and_the_epic_can_be_approved_at_once()
    {
        var office = (await PostAsync("/api/offices", new { name = "E Corp" })).GetProperty("id").GetGuid();
        var noRepo = await _http.PostAsJsonAsync($"/api/offices/{office}/plan", new { request = "Add a contact page." });
        var folder = Directory.CreateTempSubdirectory("openspace-plan-").FullName;
        try
        {
            (await _http.PutAsJsonAsync($"/api/offices/{office}/repository", new { path = folder, initialize = true })).EnsureSuccessStatusCode();
            var room = (await PostAsync($"/api/offices/{office}/rooms", new { name = "Web" })).GetProperty("id").GetGuid();
            await PostAsync($"/api/offices/{office}/rooms/{room}/desks", new { name = "Frontend", agentName = "build" });
            await PostAsync($"/api/offices/{office}/rooms/{room}/desks", new { name = "Tester", agentName = "build" });

            var plan = await PostAsync($"/api/offices/{office}/plan", new { request = "Add a contact page." });
            var epicId = plan.GetProperty("epic").GetProperty("id").GetGuid();
            var approved = await PostAsync($"/api/epics/{epicId}/approve", new { });
            var epics = await _http.GetFromJsonAsync<JsonElement>($"/api/epics?officeId={office}");
            var tasks = await _http.GetFromJsonAsync<JsonElement>($"/api/tasks?officeId={office}");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, noRepo.StatusCode);
            Assert.Equal("Contact page", plan.GetProperty("epic").GetProperty("title").GetString());
            Assert.Equal(2, plan.GetProperty("tasks").GetArrayLength());
            Assert.Equal(1, approved.GetProperty("approved").GetInt32());
            Assert.Equal("Contact page", epics[0].GetProperty("title").GetString());
            Assert.Equal(["Queued", "Proposed"], tasks.EnumerateArray().Select(t => t.GetProperty("status").GetString()));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Reports_are_written_listed_and_downloaded_over_http()
    {
        var office = (await PostAsync("/api/offices", new { name = "E Corp" })).GetProperty("id").GetGuid();
        var now = DateTimeOffset.UtcNow;

        // This runtime cannot draft: the report falls back to the facts.
        var report = await PostAsync($"/api/offices/{office}/reports", new { from = now.AddHours(-1), to = now.AddHours(1), scope = "today", language = "Italian" });
        var listed = await _http.GetFromJsonAsync<JsonElement>($"/api/offices/{office}/reports");
        var download = await _http.GetAsync($"/api/reports/{report.GetProperty("id").GetGuid()}/download");

        Assert.Equal(("E Corp: today", false), (report.GetProperty("title").GetString(), report.GetProperty("writtenByAi").GetBoolean()));
        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal("text/markdown", download.Content.Headers.ContentType?.MediaType);
        Assert.Contains("No task was worked on", await download.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
