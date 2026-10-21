using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Work;

/// <summary>
/// Turns a larger request into small tasks, one desk each, proposed for a person to
/// approve. The model reads the repository but cannot change it.
/// </summary>
public sealed class Supervisor(
    IOfficeRepository offices,
    IRoleRepository roles,
    IEpicRepository epics,
    IWorkTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    TimeProvider clock)
{
    /// <summary>The supervisor never proposes more than this; larger plans are cut.</summary>
    public const int MaxTasks = 12;

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "title": { "type": "string", "description": "A short title for the whole request" },
            "tasks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string", "description": "What to do, as a short imperative sentence" },
                  "description": { "type": "string", "description": "Everything the desk needs to do it alone: files, rules, what done means" },
                  "desk": { "type": "string", "description": "The exact name of the desk that should do it" }
                },
                "required": ["title", "description", "desk"]
              }
            }
          },
          "required": ["title", "tasks"]
        }
        """;

    private const string Instructions = """
        You are the supervisor of an office of AI agents working on one software project.
        Read the repository as much as you need, then split the request into small tasks.
        Each task goes to exactly one desk from the list, chosen by what that desk is for, and must be doable by it alone in one sitting.
        Respect each desk's limits: never give code changes to a desk whose role only reviews, tests or audits.
        Order the tasks in the order they should be done. Prefer few, well described tasks over many tiny ones.
        Write titles and descriptions in the language of the request. Do not change anything.
        """;

    public async Task<PlanView> PlanAsync(OfficeId officeId, string request, string requestedBy, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request))
        {
            throw new RejectedException("empty-text", "Write what you need first.");
        }

        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        if (office.RepositoryPath is null)
        {
            throw new RejectedException("no-repository", $"Office '{office.Name}' has no repository yet: set it in Rooms so the supervisor can read the project.");
        }

        var desks = await DescribeDesksAsync(office, cancellationToken).ConfigureAwait(false);
        if (desks.Count == 0)
        {
            throw new RejectedException("no-desks", $"Office '{office.Name}' has no desks yet: add some in Rooms first.");
        }

        string json;
        try
        {
            json = await runtime.PlanAsync(
                office.RepositoryPath, Instructions, Prompt(office, desks, request.Trim()), Schema, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new RejectedException("planning-failed", $"The supervisor could not plan this: {ex.Message}");
        }

        var (title, planned) = Read(json);
        if (planned.Count == 0)
        {
            throw new RejectedException("planning-failed", "The supervisor did not propose any task. Try describing the request in more detail.");
        }

        var now = clock.GetUtcNow();
        var epic = Epic.Open(office, title, request.Trim(), requestedBy, now);
        epics.Add(epic);

        var created = new List<WorkTask>();
        foreach (var item in planned.Take(MaxTasks))
        {
            var task = WorkTask.Propose(office, item.Title, item.Description, requestedBy, now, epic);
            if (Match(desks, item.Desk) is { } desk)
            {
                task.AssignTo(office, desk);
            }

            task.AddNote(NoteAuthor.System, $"Proposed by the supervisor as part of \"{epic.Title}\".", now);
            tasks.Add(task);
            created.Add(task);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PlanView(EpicView.From(epic), [.. created.Select(WorkTaskView.From)]);
    }

    /// <summary>Queues every proposed task of the epic that has a desk.</summary>
    public async Task<int> ApproveAllAsync(EpicId epicId, CancellationToken cancellationToken)
    {
        var epic = await epics.GetAsync(epicId, cancellationToken).ConfigureAwait(false);
        var proposed = (await tasks.ListByEpicAsync(epic.Id, cancellationToken).ConfigureAwait(false))
            .Where(t => t.Status == WorkTaskStatus.Proposed && t.DeskId is not null)
            .ToList();
        var now = clock.GetUtcNow();
        proposed.ForEach(t => t.Approve(now));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return proposed.Count;
    }

    private sealed record DeskInfo(DeskId Id, string Name, string Room, string? Role, string About, string RoomRules);

    private sealed record Planned(string Title, string Description, string Desk);

    private async Task<List<DeskInfo>> DescribeDesksAsync(Office office, CancellationToken cancellationToken)
    {
        var allRoles = (await roles.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id);
        return [.. office.Rooms.SelectMany(room => room.Desks.Select(desk =>
        {
            var role = desk.RoleId is { } id && allRoles.TryGetValue(id, out var r) ? r : null;
            var about = role is null
                ? $"general agent ({desk.Agent.AgentName})"
                : $"{role.Name}{(role.Description.Length > 0 ? $": {role.Description}" : string.Empty)}{Limits(role.Permissions)}";
            return new DeskInfo(desk.Id, desk.Name, room.Name, role?.Name, about, room.Context);
        }))];
    }

    private static string Limits(Domain.Roles.RolePermissions permissions) =>
        permissions.Edit == Domain.Roles.PermissionLevel.Deny ? " (may not change files)" : string.Empty;

    private static string Prompt(Office office, List<DeskInfo> desks, string request)
    {
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Office: {office.Name}")
            .AppendLine()
            .AppendLine("Desks (use these exact names):");
        foreach (var desk in desks)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {desk.Name} (room {desk.Room}): {desk.About}");
            if (desk.RoomRules.Length > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  Room rules: {desk.RoomRules.ReplaceLineEndings(" ")}");
            }
        }

        return text.AppendLine().AppendLine("Request:").Append(request).ToString();
    }

    private static (string Title, List<Planned> Tasks) Read(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } s ? s.Trim() : "Request";
            var items = root.TryGetProperty("tasks", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                    .Select(i => new Planned(Text(i, "title"), Text(i, "description"), Text(i, "desk")))
                    .Where(p => p.Title.Length > 0)
                    .ToList()
                : [];
            return (title.Length > 200 ? title[..200] : title, items);
        }
        catch (JsonException ex)
        {
            throw new RejectedException("planning-failed", $"The supervisor answered something that is not a plan: {ex.Message}");
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;

    /// <summary>
    /// Finds the desk the model meant: by desk name first (forgiving case and "Room / Desk"
    /// or "Desk (room …)" forms), else by role or room name when only one desk fits.
    /// </summary>
    private static DeskId? Match(List<DeskInfo> desks, string name)
    {
        var parts = name.Split('(')[0].Split('/').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        bool Same(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        DeskId? Only(Func<DeskInfo, bool> fits) => desks.Where(fits).ToList() is [var one] ? one.Id : null;

        var wanted = parts[^1];
        return desks.FirstOrDefault(d => Same(d.Name, wanted))?.Id
            ?? Only(d => Same(d.Role, wanted))
            ?? Only(d => Same(d.Room, wanted))
            ?? Only(d => parts.Any(p => Same(d.Room, p)) && parts.Any(p => Same(d.Role, p)));
    }
}
