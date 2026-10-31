using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application.Guests;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.Guests;

namespace Openspace.Infrastructure.Tests;

public sealed class ClaudeSessionLogsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2015, 6, 24, 9, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"openspace-sessions-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string Line(string type, string cwd = "/srv/ecorp/api", string entrypoint = "cli", string at = "2015-06-24T08:59:50Z", string? message = null) =>
        $$"""{"type":"{{type}}","cwd":"{{cwd}}","gitBranch":"main","entrypoint":"{{entrypoint}}","timestamp":"{{at}}"{{(message is null ? "" : $",\"message\":{message}")}}}""";

    private static SessionTrace? Trace(params string[] lines) => ClaudeSessionLogs.Trace("s1", lines, Now, Now, "/var/openspace/worktrees");

    [Fact]
    public void A_reply_using_a_tool_works_or_reads_and_one_ending_its_turn_waits()
    {
        Assert.Equal(GuestState.Working, Trace(Line("assistant", message: """{"content":[{"type":"tool_use","name":"Bash"}]}"""))!.State);
        Assert.Equal(GuestState.Reading, Trace(Line("assistant", message: """{"content":[{"type":"tool_use","name":"Read"}]}"""))!.State);
        Assert.Equal(GuestState.Idle, Trace(Line("assistant", message: """{"stop_reason":"end_turn","content":[]}"""))!.State);
        Assert.Equal(GuestState.Working, Trace(Line("user"))!.State);
    }

    [Fact]
    public void The_folder_is_where_the_last_line_was_written()
    {
        var trace = Trace(Line("user", cwd: "/srv/ecorp"), Line("user", cwd: "/srv/ecorp/web/"), """{"type":"mode"}""")!;

        Assert.Equal(("/srv/ecorp/web", "main"), (trace.Folder, trace.Branch));
    }

    [Fact]
    public void Sessions_started_by_openspace_are_not_guests()
    {
        Assert.Null(Trace(Line("user", entrypoint: "sdk-cli")));
        Assert.Null(Trace(Line("user", cwd: "/var/openspace/worktrees/0123")));
    }

    [Fact]
    public void A_quiet_session_waits_for_its_person()
    {
        Assert.Equal(GuestState.Idle, Trace(Line("user", at: "2015-06-24T08:50:00Z"))!.State);
    }

    [Fact]
    public async Task Only_sessions_written_lately_are_read()
    {
        var project = Directory.CreateDirectory(Path.Combine(_folder, "-srv-ecorp")).FullName;
        var at = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await File.WriteAllLinesAsync(Path.Combine(project, "fresh.jsonl"), [Line("user", at: at)]);
        var old = Path.Combine(project, "old.jsonl");
        await File.WriteAllLinesAsync(old, [Line("user", at: at)]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
        var logs = new ClaudeSessionLogs(
            Options.Create(new GuestOptions { Folder = _folder }), Options.Create(new WorkspaceOptions { Root = "/var/openspace/worktrees" }), new FakeTimeProvider(DateTimeOffset.UtcNow));

        var traces = await logs.RecentAsync(TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(["fresh"], traces.Select(t => t.SessionId));
    }
}
