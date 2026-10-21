using System.Net;
using System.Text;
using System.Text.Json;
using Openspace.Domain.Offices;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Infrastructure.Tests;

public class OpenCodeRuntimeTests
{
    /// <summary>A pretend OpenCode server that records requests and answers from a script.</summary>
    private sealed class FakeServer(Func<HttpRequestMessage, (HttpStatusCode, string)> answer) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string PathAndQuery, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            var (status, json) = answer(request);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json"), RequestMessage = request };
        }
    }

    private static (OpenCodeRuntime Runtime, FakeServer Server) Create(Func<HttpRequestMessage, (HttpStatusCode, string)> answer)
    {
        var server = new FakeServer(answer);
        var http = new HttpClient(server) { BaseAddress = new Uri("http://opencode.test/") };
        return (new OpenCodeRuntime(http), server);
    }

    private const string Workspace = "/srv/worktrees/abc";

    [Fact]
    public async Task Prompts_name_the_folder_the_agent_and_the_split_model()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.NoContent, ""));

        await runtime.PromptAsync("ses_1", Workspace, AgentProfile.Create("build", "opencode/deepseek-v4.1-flash-free"), "Do it", default);

        var (method, path, body) = Assert.Single(server.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/session/ses_1/prompt_async?directory=%2Fsrv%2Fworktrees%2Fabc", path);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("build", json.RootElement.GetProperty("agent").GetString());
        Assert.Equal("opencode", json.RootElement.GetProperty("model").GetProperty("providerID").GetString());
        Assert.Equal("deepseek-v4.1-flash-free", json.RootElement.GetProperty("model").GetProperty("modelID").GetString());
        Assert.Equal("Do it", json.RootElement.GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Without_a_model_the_agent_default_is_used()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.NoContent, ""));

        await runtime.PromptAsync("ses_1", Workspace, AgentProfile.Create("build"), "Do it", default);

        Assert.DoesNotContain("\"model\"", server.Requests[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_are_created_in_the_workspace()
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, """{"id":"ses_fsociety","title":"Logout"}"""));

        var id = await runtime.CreateSessionAsync(Workspace, "Logout", default);

        Assert.Equal("ses_fsociety", id);
        Assert.StartsWith("/session?directory=", server.Requests[0].PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agents_are_looked_up_by_name()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.OK, """[{"name":"build","mode":"primary"},{"name":"plan","mode":"primary"}]"""));

        Assert.True(await runtime.HasAgentAsync("build", Workspace, default));
        Assert.False(await runtime.HasAgentAsync("frontend-dev", Workspace, default));
    }

    [Fact]
    public async Task The_last_reply_is_the_last_assistant_text()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.OK, """
            [{"info":{"role":"user"},"parts":[{"type":"text","text":"Do it"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"text","text":"Looking around."},{"type":"tool","tool":"read"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"tool","tool":"edit"}]},
             {"info":{"role":"assistant"},"parts":[{"type":"text","text":"  Added the logout button.  "}]}]
            """));

        Assert.Equal("Added the logout button.", await runtime.GetLastReplyAsync("ses_1", Workspace, default));
    }

    [Fact]
    public async Task Server_errors_explain_themselves()
    {
        var (runtime, _) = Create(_ => (HttpStatusCode.BadRequest, """{"error":"agent not found"}"""));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            runtime.PromptAsync("ses_1", Workspace, AgentProfile.Create("build"), "Do it", default));

        Assert.Contains("400", error.Message, StringComparison.Ordinal);
        Assert.Contains("agent not found", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "once")]
    [InlineData(false, "reject")]
    public async Task Permission_replies_are_once_or_reject(bool allow, string reply)
    {
        var (runtime, server) = Create(_ => (HttpStatusCode.OK, "true"));

        await runtime.ReplyToPermissionAsync("per_1", Workspace, allow, default);

        Assert.Contains($"\"reply\":\"{reply}\"", server.Requests[0].Body, StringComparison.Ordinal);
    }
}
