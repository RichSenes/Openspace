using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application;
using Openspace.Application.Functions;
using Openspace.Application.Shiori;
using Openspace.Infrastructure.Shiori;

namespace Openspace.Infrastructure.Tests;

/// <summary>Shiori's glue: its sessions in SQLite, its command line and how its answer is read. Claude Code is never started.</summary>
public sealed class ShioriTests : IAsyncLifetime
{
    private readonly SqliteFixture _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    private static ShioriTurn Turn(ShioriMode mode = ShioriMode.Decide, bool resume = false, ModelEngine engine = ModelEngine.ClaudeCode, string? model = "opus") => new(
        Guid.NewGuid(), "0f8fad5b-d9cb-469f-a165-70867728950e", resume, mode, engine, model, "You are Shiori.", "[Mode: decide] Hello", "s3cret", ShioriTools.For(mode));

    private static ShioriSession Session(string token = "s3cret") => new()
    {
        Id = Guid.CreateVersion7(),
        Title = "Merge order",
        Person = "elliot",
        Mode = ShioriMode.Decide,
        RuntimeSessionId = "0f8fad5b-d9cb-469f-a165-70867728950e",
        Token = token,
        NotePath = "Sessions/2015-06-24-merge-order.md",
        CreatedAt = new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task A_session_comes_back_whole_from_the_database_by_id_and_by_secret()
    {
        await using var scope = _db.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IShioriSessionStore>();
        var session = Session();
        await store.AddAsync(session, default);

        session.Transcript.Add(new TranscriptEntry(TranscriptRole.Person, "How do we merge?", ShioriMode.Decide, session.CreatedAt));
        session.Board.Add(new WhiteboardItem("w1", WhiteboardKind.Decision, "Merge in approval order", Done: true, Office: "E Corp", Note: "Decisions/x.md"));
        session.NextItem = 2;
        session.RuntimeStarted = true;
        session.EndedAt = session.CreatedAt.AddHours(1);
        await store.SaveAsync(session, default);

        var byId = (await store.FindAsync(session.Id, default))!;
        var byToken = (await store.FindByTokenAsync("s3cret", default))!;
        Assert.Equal(session.Id, byToken.Id);
        Assert.Equal((ShioriMode.Decide, true, 2, session.EndedAt), (byId.Mode, byId.RuntimeStarted, byId.NextItem, byId.EndedAt));
        Assert.Equal(session.Transcript, byId.Transcript);
        Assert.Equal(session.Board, byId.Board);
        Assert.Null(await store.FindByTokenAsync("other", default));
    }

    [Fact]
    public async Task Sessions_are_listed_the_most_recently_changed_first()
    {
        await using var scope = _db.Scope();
        var store = scope.ServiceProvider.GetRequiredService<IShioriSessionStore>();
        var older = Session("a");
        var newer = Session("b");
        newer.UpdatedAt = older.UpdatedAt.AddMinutes(1);
        await store.AddAsync(older, default);
        await store.AddAsync(newer, default);

        Assert.Equal([newer.Id, older.Id], (await store.ListAsync(default)).Select(s => s.Id));
    }

    [Fact]
    public void Shiori_runs_with_no_tools_of_Claude_Code_and_only_its_modes_tools_of_Openspace_named_by_its_secret()
    {
        var args = ClaudeShioriRuntime.Arguments(Turn(ShioriMode.Brainstorm), new Uri("http://127.0.0.1:5145/mcp"));

        Assert.Equal(string.Empty, args[args.IndexOf("--tools") + 1]);
        var allowed = args.SkipWhile(a => a != "--allowedTools").Skip(1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        Assert.Equal(["mcp__openspace__search_brain", "mcp__openspace__read_note", "mcp__openspace__list_offices", "mcp__openspace__whiteboard"], allowed);
        Assert.Contains("--include-partial-messages", args);
        Assert.Equal("none", args[args.IndexOf("--permission-prompts") + 1]);
        using var mcp = JsonDocument.Parse(args[args.IndexOf("--mcp-config") + 1]);
        var server = mcp.RootElement.GetProperty("mcpServers").GetProperty("openspace");
        Assert.Equal("http://127.0.0.1:5145/mcp", server.GetProperty("url").GetString());
        Assert.Equal("s3cret", server.GetProperty("headers").GetProperty(ShioriTools.Header).GetString());
        Assert.Equal(("opus", "You are Shiori."), (args[args.IndexOf("--model") + 1], args[args.IndexOf("--append-system-prompt") + 1]));
    }

    [Fact]
    public void The_first_answer_names_the_conversation_and_later_ones_resume_it()
    {
        var first = ClaudeShioriRuntime.Arguments(Turn(resume: false, model: null), new Uri("http://127.0.0.1:5145/mcp"));
        var later = ClaudeShioriRuntime.Arguments(Turn(resume: true), new Uri("http://127.0.0.1:5145/mcp"));

        Assert.Equal(["--session-id", "0f8fad5b-d9cb-469f-a165-70867728950e"], first[^2..]);
        Assert.Equal(["--resume", "0f8fad5b-d9cb-469f-a165-70867728950e"], later[^2..]);
        Assert.DoesNotContain("--model", first);
        Assert.Contains("mcp__openspace__propose_decision", later);
    }

    [Fact]
    public async Task Shiori_refuses_at_once_when_its_engine_is_not_Claude_Code_or_Claude_Code_is_off()
    {
        await using var scope = _db.Scope();
        var runtime = scope.ServiceProvider.GetRequiredService<IShioriRuntime>();

        var openCode = await Assert.ThrowsAsync<RejectedException>(() => runtime.AnswerAsync(Turn(engine: ModelEngine.OpenCode), _ => Task.CompletedTask, default));
        var off = await Assert.ThrowsAsync<RejectedException>(() => runtime.AnswerAsync(Turn(), _ => Task.CompletedTask, default));

        Assert.Equal(("shiori-engine", "shiori-engine"), (openCode.Code, off.Code));
        Assert.Contains("Settings › Models", openCode.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_answer_streams_piece_by_piece_and_the_whole_message_after_it_is_not_counted_twice()
    {
        var stream = new ShioriStream();
        string[] lines =
        [
            """{"type":"system","subtype":"init"}""",
            """{"type":"stream_event","event":{"type":"message_start"}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"Let me "}}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"look."}}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Let me look."},{"type":"tool_use","id":"t1","name":"mcp__openspace__search_brain","input":{}}]}}""",
            """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"..."}]}}""",
            """{"type":"stream_event","event":{"type":"message_start"}}""",
            """{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"See [[Decisions/x]]."}}}""",
            """{"type":"assistant","message":{"content":[{"type":"text","text":"See [[Decisions/x]]."}]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"See [[Decisions/x]]."}""",
        ];

        var pieces = lines.SelectMany(stream.Read).ToList();

        Assert.Equal(["Let me ", "look.", "\n\nSee [[Decisions/x]]."], pieces);
        Assert.Equal("Let me look.\n\nSee [[Decisions/x]].", stream.Answer);
        Assert.True(stream.Ended);
        Assert.Null(stream.Error);
    }

    [Fact]
    public void Without_partial_messages_each_whole_message_is_one_piece_and_an_error_says_why()
    {
        var stream = new ShioriStream();

        string[] lines =
        [
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Hello."}]}}""",
            "not json at all",
            """{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Usage limit reached"}""",
        ];

        var pieces = lines.SelectMany(stream.Read).ToList();

        Assert.Equal(["Hello."], pieces);
        Assert.Equal("Claude Code stopped: Usage limit reached", stream.Error);
    }
}
