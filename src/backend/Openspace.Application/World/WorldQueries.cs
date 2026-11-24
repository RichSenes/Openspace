using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.Attention;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.World;

/// <summary>Who a character of the world is: a desk's agent, a routine, or an intern helping a desk.</summary>
public enum CharacterKind
{
    Desk,
    Routine,
    Intern,
}

/// <summary>What a character is doing now, as the world shows it.</summary>
public enum CharacterState
{
    /// <summary>No work: free for the lounge, the coffee, a chat.</summary>
    Free,

    /// <summary>At work: at the desk, or at the racks for a routine.</summary>
    Working,

    /// <summary>Waiting for the person (a question, a permission, a review, an approval): queues at Shiori.</summary>
    Waiting,

    /// <summary>Failed or blocked: queues at Shiori too, downcast.</summary>
    Failed,

    /// <summary>Not working at all: a routine switched off.</summary>
    Off,
}

/// <param name="Doing">What it is on now, in a line: the agent's step, the intern's errand, the routine's last word.</param>
/// <param name="TaskId">The task it works on or waits about, if any.</param>
/// <param name="ForDesk">For an intern: the desk it helps.</param>
/// <param name="Traits">A desk's character traits, for its social life.</param>
public sealed record WorldCharacter(
    Guid Id,
    CharacterKind Kind,
    string Name,
    CharacterState State,
    string? Doing,
    Guid? TaskId,
    string? TaskTitle,
    Guid? ForDesk,
    IReadOnlyList<string> Traits);

public sealed record WorldOffice(Guid Id, string Name, IReadOnlyList<WorldCharacter> Characters);

/// <summary>Something waiting for the person, and who brings it to Shiori (null when it is hers).</summary>
/// <param name="Kind">The kind of need, as the needs-you list names it, or "Question" for an agent's live question.</param>
public sealed record WorldWaiting(string Kind, Guid OfficeId, Guid? CharacterId, Guid Id, string Title, string Detail, DateTimeOffset Since);

public sealed record WorldView(IReadOnlyList<WorldOffice> Offices, IReadOnlyList<WorldWaiting> Waiting);

/// <summary>
/// The world as it is now, in one look, for the lab to draw itself from: every office with its
/// characters (one per desk, one per routine, the interns helping a desk) and what each is
/// doing; and the queue at Shiori, everything that waits for the person with who brings it.
/// </summary>
public sealed class WorldQueries(
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IRoutineRepository routines,
    ActivityBoard activity,
    InternBoard interns,
    NeedsYouQueries needs)
{
    public async Task<WorldView> GetAsync(CancellationToken cancellationToken)
    {
        var all = await offices.ListAsync(cancellationToken).ConfigureAwait(false);
        var live = activity.Snapshot().ToDictionary(a => a.DeskId);
        // The tasks that place a desk somewhere: at work, waiting for a review or an approval, failed.
        var byDesk = new Dictionary<DeskId, WorkTask>();
        foreach (var status in new[] { WorkTaskStatus.Failed, WorkTaskStatus.Proposed, WorkTaskStatus.Review, WorkTaskStatus.Running })
        {
            foreach (var task in await tasks.ListAsync(null, status, cancellationToken).ConfigureAwait(false))
            {
                if (task.DeskId is { } desk && task.MergedInto is null)
                {
                    // Later statuses in the list win: a desk at work is at work, whatever else waits.
                    byDesk[desk] = task;
                }
            }
        }

        var routineOf = new Dictionary<Guid, Guid>();
        var result = new List<WorldOffice>();
        foreach (var office in all.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var characters = new List<WorldCharacter>();
            foreach (var room in office.Rooms.Where(r => r.Kind == RoomKind.Work))
            {
                foreach (var desk in room.Desks)
                {
                    characters.Add(DeskCharacter(desk, live.GetValueOrDefault(desk.Id.Value), byDesk.GetValueOrDefault(desk.Id)));
                }
            }

            foreach (var helper in interns.Snapshot(office.Id.Value))
            {
                foreach (var intern in helper.Interns)
                {
                    characters.Add(new WorldCharacter(InternId(helper.DeskId, intern.Id), CharacterKind.Intern, "Intern", CharacterState.Working, intern.What, helper.TaskId, null, helper.DeskId, []));
                }
            }

            foreach (var routine in await routines.ListAsync(office.Id, cancellationToken).ConfigureAwait(false))
            {
                var runs = await routines.ListRunsAsync(routine.Id, 1, cancellationToken).ConfigureAwait(false);
                var last = runs.Count > 0 ? runs[0] : null;
                var state = !routine.Enabled
                    ? CharacterState.Off
                    : last?.Status switch
                    {
                        RoutineRunStatus.Running => CharacterState.Working,
                        RoutineRunStatus.Failed => CharacterState.Failed,
                        _ => CharacterState.Free,
                    };
                characters.Add(new WorldCharacter(routine.Id.Value, CharacterKind.Routine, routine.Name, state, last?.Summary is { Length: > 0 } said ? Short(said) : null, null, null, null, []));
                routineOf[routine.Id.Value] = routine.Id.Value;
            }

            result.Add(new WorldOffice(office.Id.Value, office.Name, characters));
        }

        // The queue at Shiori: the needs-you list, each with who brings it, and the agents' live questions.
        var waiting = new List<WorldWaiting>();
        var taskDesk = byDesk.ToDictionary(kv => kv.Value.Id.Value, kv => kv.Key.Value);
        var proposalRoutine = new Dictionary<Guid, Guid>();
        foreach (var officeId in await routines.OfficesWithRoutinesAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var proposal in await routines.ListProposalsAsync(officeId, ProposalStatus.Pending, cancellationToken).ConfigureAwait(false))
            {
                proposalRoutine[proposal.Id.Value] = proposal.RoutineId.Value;
            }
        }

        foreach (var item in await needs.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid? who = item.Kind switch
            {
                NeedsYouKind.TaskToApprove or NeedsYouKind.TaskToReview or NeedsYouKind.TaskFailed => taskDesk.TryGetValue(item.Id, out var d) ? d : null,
                NeedsYouKind.Proposal => proposalRoutine.TryGetValue(item.Id, out var r) ? r : null,
                NeedsYouKind.RoutineFailed => routineOf.ContainsKey(item.Id) ? item.Id : null,
                _ => null,
            };
            waiting.Add(new WorldWaiting(item.Kind.ToString(), item.OfficeId, who, item.Id, item.Title, item.Detail, item.Since));
        }

        foreach (var question in live.Values.Where(a => a.Kind == ActivityKind.Waiting))
        {
            waiting.Add(new WorldWaiting("Question", question.OfficeId, question.DeskId, question.TaskId, question.Detail ?? question.Doing ?? "A question", question.Step ?? string.Empty, question.At));
        }

        return new WorldView(result, [.. waiting.OrderBy(w => w.Since)]);
    }

    private static WorldCharacter DeskCharacter(Desk desk, DeskActivity? live, WorkTask? task)
    {
        var traits = desk.Traits.Select(t => t.ToString()).ToList();
        if (live is { Kind: ActivityKind.Waiting })
        {
            return new WorldCharacter(desk.Id.Value, CharacterKind.Desk, desk.Name, CharacterState.Waiting, live.Detail ?? live.Doing, live.TaskId, task?.Title, null, traits);
        }

        if (live is { Kind: ActivityKind.Working })
        {
            return new WorldCharacter(desk.Id.Value, CharacterKind.Desk, desk.Name, CharacterState.Working, live.Doing ?? live.Step, live.TaskId, task?.Title, null, traits);
        }

        var state = task?.Status switch
        {
            WorkTaskStatus.Running => CharacterState.Working,
            WorkTaskStatus.Review or WorkTaskStatus.Proposed => CharacterState.Waiting,
            WorkTaskStatus.Failed => CharacterState.Failed,
            _ => CharacterState.Free,
        };
        return new WorldCharacter(desk.Id.Value, CharacterKind.Desk, desk.Name, state, null, task?.Id.Value, task?.Title, null, traits);
    }

    /// <summary>An intern's id in the world: stable for the same hand-off at the same desk.</summary>
    private static Guid InternId(Guid desk, string intern)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{desk}:{intern}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string Short(string text) => text.Length <= 120 ? text : string.Concat(text.AsSpan(0, 117), "...");
}
