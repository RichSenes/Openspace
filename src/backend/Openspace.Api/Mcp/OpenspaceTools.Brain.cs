using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Openspace.Api.Mcp;

/// <summary>
/// The shared brain, for agents: what was decided and why, their own journal, and proposals for
/// choices others must follow. Agents write only their journal and proposals; nothing they write
/// is in force until Shiori or the person accepts it.
/// </summary>
internal sealed partial class OpenspaceTools
{
    [McpServerTool(Name = "brain_search", ReadOnly = true, Idempotent = true)]
    [Description("Search the shared brain (decisions, plans, studies, office notes) before guessing how something is done or why it is so. Notes about your office and room come first; accepted ones before proposed, superseded last.")]
    public Task<string> BrainSearchAsync(
        [Description("A few words to look for, e.g. \"merge order\" or \"logout\".")] string query,
        CancellationToken cancellationToken,
        [Description("Rank notes about this office first; yours when left out.")] string? office = null,
        [Description("Rank notes about this room first; yours when left out.")] string? room = null,
        [Description("A file you work on, e.g. \"src/web/cart.ts\": notes about it come first.")] string? path = null,
        [Description("Only this kind: decision, plan, study, note, journal, rule.")] string? kind = null) =>
        RunAsync(task => brain.SearchAsync(task, query, office, room, path, kind, cancellationToken));

    [McpServerTool(Name = "brain_read", ReadOnly = true, Idempotent = true)]
    [Description("Read one note of the brain in full, with its front matter, e.g. a decision named in your brief or found with brain_search.")]
    public Task<string> BrainReadAsync(
        [Description("The note's path in the brain, e.g. \"Decisions/2026-11-20-merge-order.md\".")] string note,
        CancellationToken cancellationToken) =>
        RunAsync(task => brain.ReadAsync(task, note, cancellationToken));

    [McpServerTool(Name = "brain_journal")]
    [Description("Write a line in your own journal: what you did, learnt or left half done. Do it once before you finish a task; it is dated and kept for the next one.")]
    public Task<string> BrainJournalAsync(
        [Description("One or two sentences, e.g. \"Added the logout button; the header is shared with admin, so I left its layout alone.\"")] string text,
        CancellationToken cancellationToken) =>
        RunAsync(async task => $"Written in {await brain.JournalAsync(task, text, cancellationToken)}.");

    [McpServerTool(Name = "brain_propose")]
    [Description("Propose a lasting choice others must follow (a decision), or a plan, a study or an office note. It is saved as proposed: Shiori or the person accepts it or sends it back. Not for progress: use add_task_note for that.")]
    public Task<string> BrainProposeAsync(
        [Description("decision, plan, study or note.")] string kind,
        [Description("A short title saying the choice, e.g. \"Merge in the order tasks were approved\".")] string title,
        [Description("What was chosen and what was left out, in Markdown.")] string body,
        [Description("Why: what it solves and the options weighed.")] string reasons,
        CancellationToken cancellationToken,
        [Description("The offices it applies to; yours when left out.")] string[]? offices = null,
        [Description("The rooms it applies to; every room when left out.")] string[]? rooms = null,
        [Description("Globs of the files it is about, e.g. \"src/web/**\"; every file when left out.")] string[]? paths = null) =>
        RunAsync(async task =>
        {
            var path = await brain.ProposeAsync(task, kind, title, body, offices, rooms, paths, reasons, cancellationToken);
            return $"Proposed as {path}. It is not in force until it is accepted.";
        });

    [McpServerTool(Name = "brain_now", ReadOnly = true)]
    [Description("Who else in your office is working right now, and which files each is changing: check it before touching files others may be changing.")]
    public Task<string> BrainNowAsync(CancellationToken cancellationToken) =>
        RunAsync(task => brain.NowAsync(task, cancellationToken));
}
