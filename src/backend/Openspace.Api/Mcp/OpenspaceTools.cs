using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Openspace.Application;
using Openspace.Application.Agents;
using Openspace.Domain.Work;

namespace Openspace.Api.Mcp;

/// <summary>
/// Tools OpenCode agents call while they work. Each call names its task in the
/// <c>X-Openspace-Task</c> header, which Openspace sets when it connects the agent.
/// </summary>
[McpServerToolType]
internal sealed class OpenspaceTools(AgentSignals signals, IHttpContextAccessor httpContext)
{
    public const string TaskHeader = "X-Openspace-Task";

    [McpServerTool(Name = "get_task", ReadOnly = true, Idempotent = true)]
    [Description("Re-read your current task: the room rules, the request and the team's latest notes.")]
    public Task<string> GetTaskAsync(CancellationToken cancellationToken) =>
        RunAsync(task => signals.DescribeAsync(task, cancellationToken));

    [McpServerTool(Name = "add_task_note")]
    [Description("Leave a note on your task for the team: a decision you took, something you found, or an open question. Use it for what is worth keeping, not for progress.")]
    public Task<string> AddTaskNoteAsync(
        [Description("The note, in a few clear sentences.")] string text,
        CancellationToken cancellationToken) =>
        RunAsync(async task =>
        {
            await signals.NoteAsync(task, text, cancellationToken);
            return "Note added to the task.";
        });

    [McpServerTool(Name = "announce", Idempotent = true)]
    [Description("Tell the team what you are doing right now, in one short sentence such as \"Adding the logout button to the header\". It appears above your desk.")]
    public Task<string> AnnounceAsync(
        [Description("One short sentence about what you are doing and why.")] string text,
        CancellationToken cancellationToken) =>
        RunAsync(async task =>
        {
            await signals.AnnounceAsync(task, text, cancellationToken);
            return "Announced.";
        });

    private async Task<string> RunAsync(Func<WorkTaskId, Task<string>> action)
    {
        var header = httpContext.HttpContext?.Request.Headers[TaskHeader].ToString();
        if (!Guid.TryParse(header, out var taskId))
        {
            throw new McpException($"Openspace does not know which task this is: the {TaskHeader} header is missing.");
        }

        try
        {
            return await action(new WorkTaskId(taskId));
        }
        catch (Exception ex) when (ex is RejectedException or NotFoundException or Domain.Common.DomainException)
        {
            // Turned into an error result the agent can read and act on.
            throw new McpException(ex.Message);
        }
    }
}
