using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Openspace.Application;
using Openspace.Application.Agents;
using Openspace.Domain.Work;
using Openspace.Infrastructure.ClaudeCode;

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

    [McpServerTool(Name = "complete_step")]
    [Description("Report a step of your task's required procedure as done. Openspace does not send your work to review until every step is reported.")]
    public Task<string> CompleteStepAsync(
        [Description("The step number, as listed in your brief.")] int step,
        [Description("What you did to satisfy it: files, commands and their results.")] string evidence,
        CancellationToken cancellationToken) =>
        RunAsync(async task =>
        {
            await signals.CompleteStepAsync(task, step, evidence, cancellationToken);
            return $"Step {step} recorded.";
        });

    [McpServerTool(Name = "add_manual_check")]
    [Description("List one thing a person must try by hand before your work is merged, because no automated check covers it. Call it once per check, before your final summary.")]
    public Task<string> AddManualCheckAsync(
        [Description("What to verify, in one sentence, e.g. \"The logout button signs you out on a phone\".")] string what,
        [Description("How to try it: where to go, what to run or tap, what should happen.")] string how,
        CancellationToken cancellationToken) =>
        RunAsync(async task => $"Check {await signals.AddManualCheckAsync(task, what, how, cancellationToken)} listed.");

    [McpServerTool(Name = "wait_for_person")]
    [Description("Wait until the person does or decides something you cannot, such as closing an app that must be closed: say exactly what they must do. You wait here, spending nothing, until they answer; then go on as the answer says.")]
    public Task<string> WaitForPersonAsync(
        [Description("What the person must do or decide, in one or two clear sentences, e.g. \"Atelier is open on this PC: close it, then press Done so I can install the new version.\"")] string question,
        CancellationToken cancellationToken) =>
        RunAsync(async task => await signals.WaitForPersonAsync(task, question, cancellationToken)
            ? "The person answered: done, go on."
            : "The person said stop: do not go on. Finish now with a short summary of where things stand.");

    /// <summary>
    /// Claude Code calls this before a tool the agent's role does not allow outright, and
    /// waits: the person answers in Openspace. Not meant for agents to call themselves.
    /// </summary>
    [McpServerTool(Name = "permission")]
    [Description("Asks the person in Openspace whether a tool call may run. Called by Claude Code itself; do not call it.")]
    public async Task<string> PermissionAsync(
        [Description("The tool that wants to run.")] string tool_name,
        [Description("The tool's input.")] JsonElement input,
        CancellationToken cancellationToken,
        [Description("The id of the tool call.")] string? tool_use_id = null)
    {
        try
        {
            var allowed = await RunAsync(async task =>
                (await signals.RequestPermissionAsync(task, ClaudeCodePermissions.Action(tool_name, input), cancellationToken)).ToString());
            return ClaudeCodePermissions.Reply(allowed == bool.TrueString, input);
        }
        catch (McpException)
        {
            // No task to ask about: nobody can allow it.
            return ClaudeCodePermissions.Reply(false, input);
        }
    }

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
