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

    private static readonly string[] TaskFields = ["desk", "title", "description"];
    private static readonly string[] AssignmentFields = ["desks"];
    private static readonly string[] PlanFields = ["title", "tasks"];

    /// <summary>
    /// The answer's shape. Desk names are listed as the only allowed values; OpenCode does
    /// not enforce schemas strictly, so the plan is checked again on arrival.
    /// </summary>
    private static string Schema(IEnumerable<string> deskNames) => JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "A short title for the whole request" },
            tasks = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    // Desk first: small models tend to drop a field that comes after a long description.
                    properties = new
                    {
                        desk = new { type = "string", description = "The exact name of the desk that should do it", @enum = deskNames.ToArray() },
                        title = new { type = "string", description = "What to do, as a short imperative sentence" },
                        description = new { type = "string", description = "Everything the desk needs to do it alone: files, rules, what done means" },
                    },
                    required = TaskFields,
                },
            },
        },
        required = PlanFields,
    });

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

        var prompt = Prompt(office, desks, request.Trim());
        var (title, planned) = await AskAsync(office.RepositoryPath, prompt, desks, cancellationToken).ConfigureAwait(false);

        // Small models sometimes drop or misspell the desk. The plan itself is fine, so
        // instead of planning again ask one short question: which desk for each task.
        if (desks.Count > 1 && planned.Any(p => Match(desks, p.Desk) is null))
        {
            planned = await AssignDesksAsync(office.RepositoryPath, planned, desks, cancellationToken).ConfigureAwait(false);
        }

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
            var desk = desks.Count == 1 ? desks[0].Id : Match(desks, item.Desk);
            if (desk is { } chosen)
            {
                task.AssignTo(office, chosen);
            }

            var why = desk is not null
                ? string.Empty
                : item.Desk.Length == 0 ? " It did not name a desk." : $" It suggested a desk named \"{item.Desk}\", which does not exist.";
            task.AddNote(NoteAuthor.System, $"Proposed by the supervisor as part of \"{epic.Title}\".{why}", now);
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

    /// <summary>Fills in the desk of tasks that have none, with a short answer limited to real desk names.</summary>
    private async Task<List<Planned>> AssignDesksAsync(
        string folder, List<Planned> planned, List<DeskInfo> desks, CancellationToken cancellationToken)
    {
        var names = desks.Select(d => d.Name).ToArray();
        var schema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                desks = new
                {
                    type = "array",
                    description = "One desk name per task, in the same order as the tasks",
                    items = new { type = "string", @enum = names },
                    minItems = planned.Count,
                    maxItems = planned.Count,
                },
            },
            required = AssignmentFields,
        });
        var question = new StringBuilder().AppendLine("Desks:");
        foreach (var desk in desks)
        {
            question.AppendLine(CultureInfo.InvariantCulture, $"- {desk.Name} (room {desk.Room}): {desk.About}");
        }

        question.AppendLine().AppendLine("Tasks:");
        for (var i = 0; i < planned.Count; i++)
        {
            var about = planned[i].Description.Length > 300 ? planned[i].Description[..300] : planned[i].Description;
            question.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}. {planned[i].Title}: {about}");
        }

        question.Append(CultureInfo.InvariantCulture, $"Answer with exactly {planned.Count} desk names, one per task, in order, chosen only from: {string.Join(", ", names)}.");

        try
        {
            var json = await runtime
                .PlanAsync(folder, "You assign tasks to the desks of a team of AI agents, by what each desk is for.", question.ToString(), schema, cancellationToken)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var chosen = doc.RootElement.TryGetProperty("desks", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : string.Empty).ToList()
                : [];
            // Only a real desk replaces the original suggestion, which notes keep otherwise.
            return [.. planned.Select((p, i) =>
                Match(desks, p.Desk) is null && i < chosen.Count && Match(desks, chosen[i]) is not null ? p with { Desk = chosen[i] } : p)];
        }
        catch (Exception ex) when (ex is RejectedException or JsonException)
        {
            // Keep the plan; the tasks without a desk say so in their notes.
            return planned;
        }
    }

    private async Task<(string Title, List<Planned> Tasks)> AskAsync(
        string folder, string prompt, List<DeskInfo> desks, CancellationToken cancellationToken)
    {
        try
        {
            var json = await runtime
                .PlanAsync(folder, Instructions, prompt, Schema(desks.Select(d => d.Name)), cancellationToken)
                .ConfigureAwait(false);
            return Read(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new RejectedException("planning-failed", $"The supervisor could not plan this: {ex.Message}");
        }
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
