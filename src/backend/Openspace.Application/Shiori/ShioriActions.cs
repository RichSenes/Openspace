using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Abstractions;
using Openspace.Application.Attention;
using Openspace.Application.Brain;
using Openspace.Application.Work;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Shiori;

/// <summary>
/// What Shiori's tools do, for the session that calls them. Each call names its session with the
/// session's secret and is checked against the session's mode as it is now: brainstorm only reads
/// and writes the whiteboard, decide also proposes and accepts decisions, organise also asks each
/// office's supervisor for a plan of proposed tasks.
/// </summary>
public sealed class ShioriActions(
    ShioriSessions sessions,
    IVault vault,
    BrainSearch search,
    IOfficeRepository offices,
    SupervisorJobs jobs,
    Supervisor supervisor,
    TimeProvider clock,
    IWorkTaskRepository? tasks = null,
    IUnitOfWork? unitOfWork = null)
{
    public const string ModeCode = "shiori-mode";

    private const int SearchHits = 10;

    public async Task<string> SearchAsync(string token, string query, string? office, string? kind, CancellationToken cancellationToken)
    {
        await CallerAsync(token, ShioriTools.SearchBrain, cancellationToken).ConfigureAwait(false);
        var hits = await search.SearchAsync(new BrainQuery(query, Blank(office), null, null, Blank(kind), SearchHits), cancellationToken).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return $"No note matches \"{query}\". Try fewer or other words, or leave the kind out.";
        }

        var text = new StringBuilder();
        foreach (var hit in hits)
        {
            var what = string.Join(", ", new[] { hit.Kind, hit.Status }.Where(w => !string.IsNullOrEmpty(w)));
            var scope = string.Join(", ", hit.Offices.Concat(hit.Rooms).Concat(hit.Paths));
            text.AppendLine(CultureInfo.InvariantCulture, $"- {hit.Path} · {(what.Length > 0 ? what : "note")} · {hit.Title}{(scope.Length > 0 ? $" ({scope})" : string.Empty)}");
            if (hit.Excerpt.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {hit.Excerpt}");
            }
        }

        return text.Append("Read a whole note with read_note; cite it as [[path]] without .md.").ToString();
    }

    public async Task<string> ReadAsync(string token, string note, CancellationToken cancellationToken)
    {
        await CallerAsync(token, ShioriTools.ReadNote, cancellationToken).ConfigureAwait(false);
        var found = await vault.ReadAsync(note, cancellationToken).ConfigureAwait(false)
            ?? throw new RejectedException("brain-note-missing", $"There is no note \"{note}\". search_brain finds notes by their words.");
        return VaultFormat.Write(found);
    }

    /// <summary>Every office with its rooms and desks, and whether the supervisor can plan there.</summary>
    public async Task<string> OfficesAsync(string token, CancellationToken cancellationToken)
    {
        await CallerAsync(token, ShioriTools.ListOffices, cancellationToken).ConfigureAwait(false);
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.Count == 0)
        {
            return "There are no offices yet.";
        }

        var text = new StringBuilder();
        foreach (var office in all.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{office.Name}{(office.RepositoryPath is null ? " (no repository yet: plans cannot be made there)" : string.Empty)}");
            foreach (var room in office.Rooms)
            {
                var desks = room.Desks.Count == 0 ? "no desks" : string.Join(", ", room.Desks.Select(d => d.Name));
                text.AppendLine(CultureInfo.InvariantCulture, $"- room {room.Name}: {desks}");
            }
        }

        return text.ToString().TrimEnd();
    }

    public async Task<string> WhiteboardAsync(string token, WhiteboardChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        var session = await CallerAsync(token, ShioriTools.Whiteboard, cancellationToken).ConfigureAwait(false);
        var item = await sessions.ChangeBoardAsync(session.Id, change, cancellationToken).ConfigureAwait(false);
        var what = item.Kind.ToString().ToLowerInvariant();
        return change.Action switch
        {
            WhiteboardAction.Add => $"Added {item.Id} ({what}): {item.Text}",
            WhiteboardAction.Remove => $"Removed {item.Id}.",
            WhiteboardAction.Done => $"Marked {item.Id} done.",
            _ => $"Moved {item.Id} to {what}.",
        };
    }

    /// <param name="item">The whiteboard item it comes from, so ending the session does not propose it again.</param>
    public async Task<string> ProposeAsync(string token, ShioriProposals.Draft draft, string? item, CancellationToken cancellationToken)
    {
        var session = await CallerAsync(token, ShioriTools.ProposeDecision, cancellationToken).ConfigureAwait(false);
        var path = await ShioriProposals.WriteAsync(vault, session, draft, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), clock.LocalTimeZone), cancellationToken)
            .ConfigureAwait(false);
        if (Blank(item) is { } linked)
        {
            await sessions.LinkProposalAsync(session.Id, linked, path, cancellationToken).ConfigureAwait(false);
        }

        return $"Proposed as {path}. It is not in force until it is accepted: accept_decision accepts it when the rules let you.";
    }

    /// <summary>
    /// Accepts a proposal the way the acceptor would: only within the narrow rules, so what reaches
    /// past one office, changes the constitution or may contradict a decision stays for the person.
    /// </summary>
    public async Task<string> AcceptAsync(string token, string note, CancellationToken cancellationToken)
    {
        var session = await CallerAsync(token, ShioriTools.AcceptDecision, cancellationToken).ConfigureAwait(false);
        var proposal = await vault.ReadAsync(note, cancellationToken).ConfigureAwait(false)
            ?? throw new RejectedException("brain-note-missing", $"There is no note \"{note}\".");
        if (!DecisionRules.IsProposed(proposal))
        {
            throw new RejectedException(DecisionCommands.WrongStatusCode, $"\"{proposal.Title}\" is {proposal.Status ?? "without a status"}: only proposals can be accepted.");
        }

        var accepted = (await vault.ListAsync(cancellationToken).ConfigureAwait(false)).Where(DecisionRules.IsAcceptedDecision);
        if (DecisionRules.WhyWaiting(proposal, accepted) is { } why)
        {
            throw new RejectedException("decision-waits", $"\"{proposal.Title}\" stays for the person: {why}");
        }

        var changed = DecisionRules.With(
            DecisionRules.With(proposal with { Status = NoteStatus.Accepted }, DecisionRules.AcceptedBy, DecisionRules.Shiori), DecisionRules.Reviewed, "false");
        await vault.WriteAsync(changed, DecisionRules.Shiori, cancellationToken, $"accepted {proposal.Path} in {ShioriProposals.From(session)}").ConfigureAwait(false);
        return $"Accepted {proposal.Path}. It reaches the person as accepted, to review.";
    }

    /// <summary>
    /// Asks one office's supervisor for a plan, in the background as the Board does: the tasks
    /// arrive as proposals under an epic, for the person to approve.
    /// </summary>
    public async Task<string> PlanAsync(string token, string office, string request, CancellationToken cancellationToken)
    {
        var session = await CallerAsync(token, ShioriTools.PlanWork, cancellationToken).ConfigureAwait(false);
        var what = request?.Trim();
        if (string.IsNullOrEmpty(what))
        {
            throw new RejectedException("empty-text", "Say what the office should plan.");
        }

        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        var target = all.FirstOrDefault(o => string.Equals(o.Name, office?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new RejectedException("shiori-office", $"There is no office \"{office}\". The offices are: {string.Join(", ", all.Select(o => o.Name))}.");
        await supervisor.CheckAsync(target.Id, cancellationToken).ConfigureAwait(false);

        var officeId = target.Id.Value;
        var job = jobs.Start(officeId, "shiori", what, $"shiori:{session.Id}:{officeId}:{what.GetHashCode(StringComparison.Ordinal)}", async (services, ct) =>
        {
            var plan = await services.GetRequiredService<Supervisor>().PlanAsync(new OfficeId(officeId), what, DecisionRules.Shiori, ct).ConfigureAwait(false);
            return new SupervisorOutcome(plan.Tasks.Count, plan.Epic.Id);
        });
        await sessions.ChangeBoardAsync(session.Id, new WhiteboardChange(WhiteboardAction.Add, Kind: WhiteboardKind.Task, Text: $"Plan asked of {target.Name}: {what}", Office: target.Name), cancellationToken)
            .ConfigureAwait(false);
        return $"The supervisor of {target.Name} is planning it (job {job.Id}). The tasks arrive as proposals under an epic, for the person to approve.";
    }

    /// <summary>
    /// Gives one task straight to a desk, as the office's supervisor would: the person asked for it
    /// in the session, so it is approved and goes in the queue at once, with no proposal to accept.
    /// Without a desk named, the desk of the office with the least work in hand takes it.
    /// </summary>
    public async Task<string> AssignAsync(string token, string office, string? desk, string title, string? description, CancellationToken cancellationToken)
    {
        var session = await CallerAsync(token, ShioriTools.AssignTask, cancellationToken).ConfigureAwait(false);
        if (tasks is null || unitOfWork is null)
        {
            throw new RejectedException("shiori-assign", "Tasks cannot be given from here.");
        }

        var what = title?.Trim();
        if (string.IsNullOrEmpty(what))
        {
            throw new RejectedException("empty-text", "Say what the task is, in a short title.");
        }

        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        var target = all.FirstOrDefault(o => string.Equals(o.Name, office?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new RejectedException("shiori-office", $"There is no office \"{office}\". The offices are: {string.Join(", ", all.Select(o => o.Name))}.");
        var desks = target.Rooms.Where(r => r.Kind == RoomKind.Work).SelectMany(r => r.Desks).ToList();
        if (desks.Count == 0)
        {
            throw new RejectedException("shiori-desk", $"{target.Name} has no desk to give work to.");
        }

        Desk chosen;
        if (!string.IsNullOrWhiteSpace(desk))
        {
            chosen = desks.FirstOrDefault(d => string.Equals(d.Name, desk.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new RejectedException("shiori-desk", $"{target.Name} has no desk \"{desk}\". Its desks are: {string.Join(", ", desks.Select(d => d.Name))}.");
        }
        else
        {
            // The desk with the least in hand: running, queued or waiting for a review.
            var busy = new Dictionary<DeskId, int>();
            foreach (var status in new[] { WorkTaskStatus.Running, WorkTaskStatus.Queued, WorkTaskStatus.Review })
            {
                foreach (var t in await tasks.ListAsync(target.Id, status, cancellationToken).ConfigureAwait(false))
                {
                    if (t.DeskId is { } d)
                    {
                        busy[d] = busy.GetValueOrDefault(d) + 1;
                    }
                }
            }

            chosen = desks.OrderBy(d => busy.GetValueOrDefault(d.Id)).First();
        }

        var task = WorkTask.Assign(target, chosen.Id, what, description?.Trim(), "Shiori", clock.GetUtcNow());
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await sessions.ChangeBoardAsync(session.Id, new WhiteboardChange(WhiteboardAction.Add, Kind: WhiteboardKind.Task, Text: $"{what} → {target.Name}, {chosen.Name}", Office: target.Name), cancellationToken)
            .ConfigureAwait(false);
        return $"Given to {chosen.Name} in {target.Name}: \"{what}\" is in the queue and starts as soon as a seat is free. No approval needed: the person asked for it.";
    }

    /// <summary>The calling session, when its mode allows the tool now.</summary>
    private async Task<ShioriSession> CallerAsync(string token, string tool, CancellationToken cancellationToken)
    {
        var session = await sessions.ByTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (!ShioriTools.Allows(session.Mode, tool))
        {
            var where = tool is ShioriTools.PlanWork ? "organise" : "decide";
            throw new RejectedException(
                ModeCode, $"The session is in {ShioriPrompt.Name(session.Mode)} mode: {tool} needs {where} mode. Ask the person to switch it if they want this.");
        }

        return session;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
