using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

internal sealed class InMemoryOffices : IOfficeRepository
{
    public List<Office> Items { get; } = [];

    public Task<Office?> FindAsync(OfficeId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(o => o.Id == id));

    public Task<IReadOnlyList<Office>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Office>>(Items);

    public void Add(Office office) => Items.Add(office);
}

internal sealed class InMemoryTasks : IWorkTaskRepository
{
    public List<WorkTask> Items { get; } = [];

    public Task<WorkTask?> FindAsync(WorkTaskId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(t => t.Id == id));

    public Task<IReadOnlyList<WorkTask>> ListAsync(
        OfficeId? officeId, WorkTaskStatus? status, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkTask>>(Items
            .Where(t => officeId is null || t.OfficeId == officeId)
            .Where(t => status is null || t.Status == status)
            .OrderBy(t => t.CreatedAt)
            .ToList());

    public Task<WorkTask?> FindBySessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(t => t.Run?.SessionId == sessionId));

    public Task<IReadOnlyList<WorkTask>> ListByEpicAsync(EpicId epicId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkTask>>([.. Items.Where(t => t.EpicId == epicId).OrderBy(t => t.CreatedAt)]);

    public void Add(WorkTask task) => Items.Add(task);
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>Records what Openspace asked of the agent runtime.</summary>
internal sealed class FakeRuntime : IAgentRuntime
{
    public HashSet<string> Agents { get; } = ["build", "frontend-dev"];

    public List<(string SessionId, AgentSetup Setup, string Text)> Prompts { get; } = [];

    public List<AgentSetup> SessionSetups { get; } = [];

    public string Draft { get; set; } = "- Write tests first.";

    public List<(string Instructions, string Text)> DraftRequests { get; } = [];

    public string Plan { get; set; } = "{}";

    public Exception? FailOnPlan { get; set; }

    public List<(string Folder, string Text)> PlanRequests { get; } = [];

    public Task<string> PlanAsync(string folder, string instructions, string text, string jsonSchema, CancellationToken cancellationToken)
    {
        if (FailOnPlan is not null)
        {
            throw FailOnPlan;
        }

        PlanRequests.Add((folder, text));
        return Task.FromResult(Plan);
    }

    public Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken)
    {
        DraftRequests.Add((instructions, text));
        return Task.FromResult(Draft);
    }

    public List<string> Aborted { get; } = [];

    public List<(string PermissionId, bool Allow)> PermissionReplies { get; } = [];

    public string? LastReply { get; set; } = "Added the button.";

    public Exception? FailOnPrompt { get; set; }

    private int _sessions;

    public Task<bool> HasAgentAsync(string agentName, string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult(Agents.Contains(agentName));

    public Task<string> CreateSessionAsync(string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken)
    {
        SessionSetups.Add(setup);
        return Task.FromResult($"ses_{++_sessions}");
    }

    public Task PromptAsync(string sessionId, string workspacePath, AgentSetup setup, string text, CancellationToken cancellationToken)
    {
        if (FailOnPrompt is not null)
        {
            throw FailOnPrompt;
        }

        Prompts.Add((sessionId, setup, text));
        return Task.CompletedTask;
    }

    public List<(string Workspace, Guid TaskId)> ToolConnections { get; } = [];

    public Exception? FailOnConnect { get; set; }

    public Task ConnectToolsAsync(string workspacePath, Guid taskId, CancellationToken cancellationToken)
    {
        if (FailOnConnect is not null)
        {
            throw FailOnConnect;
        }

        ToolConnections.Add((workspacePath, taskId));
        return Task.CompletedTask;
    }

    public Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken)
    {
        Aborted.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult(LastReply);

    public List<PermissionRequest> Pending { get; } = [];

    public Task<IReadOnlyList<PermissionRequest>> PendingPermissionsAsync(string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionRequest>>(Pending);

    public Task ReplyToPermissionAsync(string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken)
    {
        PermissionReplies.Add((permissionId, allow));
        return Task.CompletedTask;
    }
}

internal sealed class FakeWorkspaces : IWorkspaceManager
{
    public RepositoryProblem? Problem { get; set; }

    public List<string> Initialized { get; } = [];

    public Task<RepositoryProblem?> CheckRepositoryAsync(string repositoryPath, CancellationToken cancellationToken) =>
        Task.FromResult(Problem);

    public Task InitializeRepositoryAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        Initialized.Add(repositoryPath);
        Problem = null;
        return Task.CompletedTask;
    }

    public List<Workspace> Prepared { get; } = [];

    public List<(string Workspace, string Message)> Commits { get; } = [];

    public List<Workspace> Discarded { get; } = [];

    public Task<Workspace> PrepareAsync(string repositoryPath, string taskKey, string title, CancellationToken cancellationToken)
    {
        var workspace = new Workspace($"/srv/worktrees/{taskKey}", $"openspace/{taskKey}");
        Prepared.Add(workspace);
        return Task.FromResult(workspace);
    }

    public List<(string Source, string Target)> Merges { get; } = [];

    public RejectedException? MergeProblem { get; set; }

    public Task<BranchList> ListBranchesAsync(string repositoryPath, CancellationToken cancellationToken) =>
        Task.FromResult(new BranchList("main", ["develop", "main"]));

    public Task MergeAsync(string repositoryPath, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken)
    {
        if (MergeProblem is not null)
        {
            throw MergeProblem;
        }

        Merges.Add((sourceBranch, targetBranch));
        return Task.CompletedTask;
    }

    public Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult("+<button>Logout</button>");

    public Task CommitAsync(string repositoryPath, string workspacePath, string message, CancellationToken cancellationToken)
    {
        Commits.Add((workspacePath, message));
        return Task.CompletedTask;
    }

    public Task DiscardAsync(string repositoryPath, Workspace workspace, CancellationToken cancellationToken)
    {
        Discarded.Add(workspace);
        return Task.CompletedTask;
    }
}

internal sealed class RecordingPublisher : IActivityPublisher
{
    public List<DeskActivity> Published { get; } = [];

    public Task PublishAsync(DeskActivity activity, CancellationToken cancellationToken)
    {
        Published.Add(activity);
        return Task.CompletedTask;
    }

    public List<Guid> LogChanges { get; } = [];

    public Task LogChangedAsync(Guid taskId, CancellationToken cancellationToken)
    {
        LogChanges.Add(taskId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAuditLog : IAuditLog
{
    public List<AuditEntry> Items { get; } = [];

    public Task<IReadOnlyList<AuditEntry>> ForTaskAsync(WorkTaskId taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>([.. Items.Where(e => e.TaskId == taskId).OrderBy(e => e.At)]);

    public Task<bool> ContainsAsync(WorkTaskId taskId, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Exists(e => e.TaskId == taskId && e.ExternalId == externalId));

    public void Add(AuditEntry entry) => Items.Add(entry);
}

internal sealed class InMemoryRoles : IRoleRepository
{
    public List<Domain.Roles.Role> Items { get; } = [];

    public Task<Domain.Roles.Role?> FindAsync(Domain.Roles.RoleId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(r => r.Id == id));

    public Task<IReadOnlyList<Domain.Roles.Role>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Domain.Roles.Role>>(Items);

    public void Add(Domain.Roles.Role role) => Items.Add(role);

    public void Remove(Domain.Roles.Role role) => Items.Remove(role);
}

internal sealed class InMemoryEpics : IEpicRepository
{
    public List<Epic> Items { get; } = [];

    public Task<Epic?> FindAsync(EpicId id, CancellationToken cancellationToken) => Task.FromResult(Items.Find(e => e.Id == id));

    public Task<IReadOnlyList<Epic>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Epic>>([.. Items.Where(e => e.OfficeId == officeId)]);

    public void Add(Epic epic) => Items.Add(epic);
}
