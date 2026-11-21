using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Openspace.Application;
using Openspace.Application.Shiori;

namespace Openspace.Api.Mcp;

/// <summary>
/// Shiori's own tools. A session names itself with its secret in the <c>X-Openspace-Shiori</c>
/// header, which Openspace sets when it starts the session's engine; the server shows these tools to
/// Shiori's sessions only, and the desks' tools only to desks (see <see cref="McpToolSets"/>).
/// </summary>
[McpServerToolType]
internal sealed class ShioriMcpTools(ShioriActions actions, IHttpContextAccessor httpContext)
{
    [McpServerTool(Name = ShioriTools.SearchBrain, ReadOnly = true, Idempotent = true)]
    [Description("Search the shared brain: the constitution, decisions, plans, studies, office notes, journals and past sessions. Accepted notes come before proposed ones, superseded last.")]
    public Task<string> SearchAsync(
        [Description("A few words to look for, e.g. \"merge order\".")] string query,
        CancellationToken cancellationToken,
        [Description("Rank notes about this office first.")] string? office = null,
        [Description("Only this kind: decision, plan, study, note, journal, rule, session.")] string? kind = null) =>
        RunAsync(token => actions.SearchAsync(token, query, office, kind, cancellationToken));

    [McpServerTool(Name = ShioriTools.ReadNote, ReadOnly = true, Idempotent = true)]
    [Description("Read one note of the brain in full, with its front matter. Cite it in your answer as [[path]] without .md.")]
    public Task<string> ReadAsync(
        [Description("The note's path in the brain, e.g. \"Decisions/2026-11-20-merge-order.md\".")] string note,
        CancellationToken cancellationToken) =>
        RunAsync(token => actions.ReadAsync(token, note, cancellationToken));

    [McpServerTool(Name = ShioriTools.ListOffices, ReadOnly = true, Idempotent = true)]
    [Description("Every office with its rooms and desks, and whether its supervisor can plan there.")]
    public Task<string> OfficesAsync(CancellationToken cancellationToken) =>
        RunAsync(token => actions.OfficesAsync(token, cancellationToken));

    [McpServerTool(Name = ShioriTools.Whiteboard)]
    [Description("Change the session's whiteboard, shown beside the chat: add an idea, a doubt, a decision taken or a task; move an item to another column or place; mark it done; remove it. Keep items to one short line.")]
    public Task<string> WhiteboardAsync(
        [Description("add, move, done or remove.")] string action,
        CancellationToken cancellationToken,
        [Description("The item to change, e.g. \"w3\"; for move, done and remove.")] string? item = null,
        [Description("idea, doubt, decision or task: the column, for add, or the new column, for move.")] string? kind = null,
        [Description("The item's text, for add; a new text, for move.")] string? text = null,
        [Description("For move: the place in its column, 1 the top.")] int? position = null,
        [Description("The office the item is about, if one: a decision is then proposed for that office.")] string? office = null) =>
        RunAsync(token => actions.WhiteboardAsync(token, new WhiteboardChange(Parse<WhiteboardAction>(action, "action"), item, kind is null ? null : Parse<WhiteboardKind>(kind, "kind"), text, position, office), cancellationToken));

    [McpServerTool(Name = ShioriTools.ProposeDecision)]
    [Description("Decide mode only. Propose a decision others must follow, or a rule for the constitution, with its reasons and scope. It is saved as proposed in Decisions/; accept_decision accepts it when the rules let you, a rule always waits for the person.")]
    public Task<string> ProposeAsync(
        [Description("A short title saying the choice, e.g. \"Merge in the order tasks were approved\".")] string title,
        [Description("What was chosen and what was left out, in Markdown.")] string body,
        [Description("Why: what it solves and the options weighed.")] string reasons,
        CancellationToken cancellationToken,
        [Description("decision (the default) or rule.")] string? kind = null,
        [Description("The offices it applies to; every office when left out.")] string[]? offices = null,
        [Description("The rooms it applies to; every room when left out.")] string[]? rooms = null,
        [Description("Globs of the files it is about, e.g. \"src/web/**\".")] string[]? paths = null,
        [Description("The whiteboard item it comes from, e.g. \"w4\".")] string? item = null) =>
        RunAsync(token => actions.ProposeAsync(
            token, new ShioriProposals.Draft(kind ?? "decision", title, body, reasons, offices ?? [], rooms ?? [], paths ?? []), item, cancellationToken));

    [McpServerTool(Name = ShioriTools.AcceptDecision)]
    [Description("Decide mode only. Accept a proposal, yours or a desk's. Refused when it reaches past one office, changes the constitution, replaces a decision or may contradict one: those are the person's to say.")]
    public Task<string> AcceptAsync(
        [Description("The proposal's path, e.g. \"Decisions/2026-11-21-merge-order.md\".")] string note,
        CancellationToken cancellationToken) =>
        RunAsync(token => actions.AcceptAsync(token, note, cancellationToken));

    [McpServerTool(Name = ShioriTools.PlanWork)]
    [Description("Organise mode only. Ask one office's supervisor to plan a piece of work: it reads the project and proposes tasks for its desks, under an epic the person approves. Call it once per office concerned.")]
    public Task<string> PlanAsync(
        [Description("The office's exact name, as list_offices gives it.")] string office,
        [Description("What the office should do, standing on its own: the supervisor does not see this conversation.")] string request,
        CancellationToken cancellationToken) =>
        RunAsync(token => actions.PlanAsync(token, office, request, cancellationToken));

    private static T Parse<T>(string value, string what)
        where T : struct, Enum =>
        Enum.TryParse<T>(value?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new RejectedException("whiteboard-" + what, $"\"{value}\" is not a {what}: use one of {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");

    private async Task<string> RunAsync(Func<string, Task<string>> action)
    {
        var token = httpContext.HttpContext?.Request.Headers[ShioriTools.Header].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new McpException($"Openspace does not know which Shiori session this is: the {ShioriTools.Header} header is missing.");
        }

        try
        {
            return await action(token);
        }
        catch (Exception ex) when (ex is RejectedException or NotFoundException or Domain.Common.DomainException)
        {
            // Turned into an error result Shiori can read and act on.
            throw new McpException(ex.Message);
        }
    }
}
