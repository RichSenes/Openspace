using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Openspace.Application.Shiori;

namespace Openspace.Api.Tests;

/// <summary>Shiori's sessions through the real API, hub and MCP server, with a made-up engine in place of Claude Code.</summary>
public sealed class ShioriApiTests : IAsyncLifetime
{
    private readonly ScriptedShiori _engine = new();
    private readonly OpenspaceApp _app;

    public ShioriApiTests() => _app = new OpenspaceApp(shiori: _engine);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Answers in two pieces; may call Openspace's tools first, as Claude Code would.</summary>
    private sealed class ScriptedShiori : IShioriRuntime
    {
        public List<ShioriTurn> Turns { get; } = [];

        public Func<ShioriTurn, Task>? During { get; set; }

        public async Task<string> AnswerAsync(ShioriTurn turn, Func<string, Task> onText, CancellationToken cancellationToken)
        {
            lock (Turns)
            {
                Turns.Add(turn);
            }

            if (During is not null)
            {
                await During(turn);
            }

            await onText("Merging in approval order, ");
            await onText("as [[Decisions/2026-11-20-merge-order]] says.");
            return "Merging in approval order, as [[Decisions/2026-11-20-merge-order]] says.";
        }

        public Task StopAsync(string runtimeSessionId) => Task.CompletedTask;
    }

    private async Task<McpClient> ConnectAsync(string? token)
    {
        var headers = new Dictionary<string, string>();
        if (token is not null)
        {
            headers[ShioriTools.Header] = token;
        }

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_app.Server.BaseAddress, "mcp"),
                AdditionalHeaders = headers,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            _app.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Waits until the session's last line is Shiori's or a failure: the answer was saved.</summary>
    private static async Task<JsonElement> AnsweredAsync(HttpClient http, Guid id)
    {
        for (var i = 0; i < 100; i++)
        {
            var session = await http.GetFromJsonAsync<JsonElement>($"/api/shiori/sessions/{id}");
            if (!session.GetProperty("answering").GetBoolean())
            {
                return session;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Shiori never finished answering.");
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] values) => values.ToDictionary(v => v.Key, v => v.Value);

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task A_session_is_opened_talked_in_switched_and_ended_and_its_answer_streams_over_the_hub()
    {
        using var http = _app.CreateClient();
        await using var hub = _app.CreateHubConnection();
        var pieces = new List<string>();
        var saved = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new List<JsonElement>();
        hub.On<Guid, string>("ShioriText", (_, chunk) =>
        {
            lock (pieces)
            {
                pieces.Add(chunk);
            }
        });
        hub.On<Guid, JsonElement>("ShioriMessage", (_, entry) =>
        {
            if (entry.GetProperty("role").GetString() == "Shiori")
            {
                saved.TrySetResult(entry);
            }
        });
        hub.On<JsonElement>("ShioriState", state =>
        {
            lock (states)
            {
                states.Add(state);
            }
        });
        await hub.StartAsync();

        var created = await http.PostAsJsonAsync("/api/shiori/sessions", new { title = "Merge order", mode = "brainstorm" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var session = await ReadAsync(created);
        var id = session.GetProperty("id").GetGuid();
        Assert.Equal(("Brainstorm", "Sessions/"), (session.GetProperty("mode").GetString(), session.GetProperty("note").GetString()![..9]));
        Assert.False(session.TryGetProperty("token", out _));

        var sent = await http.PostAsJsonAsync($"/api/shiori/sessions/{id}/messages", new { text = "How do we merge?" });
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
        Assert.Equal("Person", (await ReadAsync(sent)).GetProperty("role").GetString());
        var answer = await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Merging in approval order, as [[Decisions/2026-11-20-merge-order]] says.", answer.GetProperty("text").GetString());
        lock (pieces)
        {
            Assert.Equal(["Merging in approval order, ", "as [[Decisions/2026-11-20-merge-order]] says."], pieces);
        }

        await AnsweredAsync(http, id);
        var switched = await ReadAsync(await http.PutAsJsonAsync($"/api/shiori/sessions/{id}/mode", new { mode = "decide" }));
        Assert.Equal("Decide", switched.GetProperty("mode").GetString());
        Assert.Equal("Mode", switched.GetProperty("transcript")[2].GetProperty("role").GetString());
        await http.PostAsJsonAsync($"/api/shiori/sessions/{id}/messages", new { text = "Settle it." });
        await AnsweredAsync(http, id);
        Assert.Equal((ShioriMode.Brainstorm, ShioriMode.Decide), (_engine.Turns[0].Mode, _engine.Turns[1].Mode));
        Assert.True(_engine.Turns[1].Resume);

        var ended = await ReadAsync(await http.PostAsync($"/api/shiori/sessions/{id}/end", null));
        Assert.NotEqual(JsonValueKind.Null, ended.GetProperty("session").GetProperty("endedAt").ValueKind);
        var late = await http.PostAsJsonAsync($"/api/shiori/sessions/{id}/messages", new { text = "One more thing" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, late.StatusCode);
        Assert.Equal(ShioriSessions.EndedCode, (await ReadAsync(late)).GetProperty("code").GetString());

        var list = await http.GetFromJsonAsync<JsonElement>("/api/shiori/sessions");
        Assert.Equal((id, 4), (list[0].GetProperty("id").GetGuid(), list[0].GetProperty("messages").GetInt32()));
        var note = await File.ReadAllTextAsync(Path.Combine(_app.BrainFolder, session.GetProperty("note").GetString()!));
        Assert.Contains("kind: session", note, StringComparison.Ordinal);
        Assert.Contains("as [[Decisions/2026-11-20-merge-order]] says.", note, StringComparison.Ordinal);
        Assert.Contains("*Mode: decide*", note, StringComparison.Ordinal);
        lock (states)
        {
            Assert.True(states[^1].GetProperty("ended").GetBoolean());
        }
    }

    [Fact]
    public async Task An_unknown_session_is_not_found()
    {
        using var http = _app.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/shiori/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Shiori_sees_only_its_tools_and_desks_only_theirs_whichever_asks_first()
    {
        await using var shiori = await ConnectAsync("any-secret");
        await using var desk = await ConnectAsync(null);

        var shioriTools = (await shiori.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        var deskTools = (await desk.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        var shioriAgain = (await shiori.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        Assert.True(shioriTools.SetEquals(ShioriTools.All));
        Assert.True(shioriAgain.SetEquals(ShioriTools.All));
        Assert.Contains("add_task_note", deskTools);
        Assert.DoesNotContain(ShioriTools.Whiteboard, deskTools);
    }

    [Fact]
    public async Task During_an_answer_Shiori_fills_the_whiteboard_through_the_tools_and_the_end_proposes_its_decisions()
    {
        using var http = _app.CreateClient();
        await using var hub = _app.CreateHubConnection();
        var board = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<Guid, JsonElement>("ShioriBoard", (_, items) => board.TrySetResult(items));
        await hub.StartAsync();
        string? refused = null;
        _engine.During = async turn =>
        {
            await using var tools = await ConnectAsync(turn.ToolsToken);
            await tools.CallToolAsync(ShioriTools.Whiteboard, Args(("action", "add"), ("kind", "decision"), ("text", "Merge in approval order"), ("office", "E Corp")));
            var propose = await tools.CallToolAsync(ShioriTools.ProposeDecision, Args(("title", "Merge in approval order"), ("body", "Approval order."), ("reasons", "Fewer conflicts.")));
            refused = propose.IsError == true ? Text(propose) : null;
        };
        var id = (await ReadAsync(await http.PostAsJsonAsync("/api/shiori/sessions", new { title = "Merge order" }))).GetProperty("id").GetGuid();

        await http.PostAsJsonAsync($"/api/shiori/sessions/{id}/messages", new { text = "Let us gather ideas." });
        var pushed = await board.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var session = await AnsweredAsync(http, id);
        var ending = await ReadAsync(await http.PostAsync($"/api/shiori/sessions/{id}/end", null));

        Assert.Equal(("w1", "Decision"), (pushed[0].GetProperty("id").GetString(), pushed[0].GetProperty("kind").GetString()));
        Assert.Equal("Merge in approval order", session.GetProperty("board")[0].GetProperty("text").GetString());
        Assert.Contains("brainstorm mode", refused, StringComparison.Ordinal);
        var path = ending.GetProperty("proposed")[0].GetString()!;
        var proposal = await http.GetFromJsonAsync<JsonElement>($"/api/brain/notes/{path}");
        Assert.Equal(
            ("decision", "proposed", "Shiori", $"session {id}"),
            (proposal.GetProperty("kind").GetString(), proposal.GetProperty("status").GetString(), proposal.GetProperty("author").GetString(), proposal.GetProperty("from").GetString()));
        Assert.Equal("E Corp", proposal.GetProperty("offices")[0].GetString());
    }

    [Fact]
    public async Task A_tool_call_with_a_secret_no_session_has_is_an_error()
    {
        await using var tools = await ConnectAsync("not-a-session");

        var result = await tools.CallToolAsync(ShioriTools.ListOffices, Args());

        Assert.True(result.IsError);
        Assert.Contains("does not know this Shiori session", Text(result), StringComparison.Ordinal);
    }
}
