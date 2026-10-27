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

    /// <summary>Answers for successive plans; when empty, <see cref="Plan"/> is used.</summary>
    public Queue<string> Plans { get; } = new();

    public List<string> PlanSchemas { get; } = [];

    public Exception? FailOnPlan { get; set; }

    public List<(string Folder, string Text)> PlanRequests { get; } = [];

    public List<IReadOnlyList<string>> PrivatePathsSeen { get; } = [];

    public Task<string> PlanAsync(string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken)
    {
        PrivatePathsSeen.Add(privatePaths);
        if (FailOnPlan is not null)
        {
            throw FailOnPlan;
        }

        PlanRequests.Add((folder, text));
        PlanSchemas.Add(jsonSchema);
        return Task.FromResult(Plans.Count > 0 ? Plans.Dequeue() : Plan);
    }

    public Exception? FailOnDraft { get; set; }

    public Task<string> DraftAsync(string instructions, string text, CancellationToken cancellationToken)
    {
        DraftRequests.Add((instructions, text));
        return FailOnDraft is null ? Task.FromResult(Draft) : throw FailOnDraft;
    }

    public List<string> Aborted { get; } = [];

    public List<(string PermissionId, bool Allow)> PermissionReplies { get; } = [];

    public string? LastReply { get; set; } = "Added the button.";

    public Exception? FailOnPrompt { get; set; }

    private int _sessions;

    private readonly Dictionary<string, AgentEngine> _engines = [];

    public Task<bool> HasAgentAsync(AgentProfile agent, string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult(agent.Engine == AgentEngine.ClaudeCode ? ClaudeCode : Agents.Contains(agent.AgentName));

    /// <summary>Whether Claude Code is there; no reviewer when it is not.</summary>
    public bool ClaudeCode { get; set; } = true;

    /// <summary>The reviewer's answer; null for no reviewer.</summary>
    public string? Review { get; set; }

    public Exception? FailOnReview { get; set; }

    public List<(string Folder, string Text)> ReviewRequests { get; } = [];

    public Task<string?> ReviewAsync(string folder, string instructions, string text, string jsonSchema, IReadOnlyList<string> privatePaths, CancellationToken cancellationToken)
    {
        PrivatePathsSeen.Add(privatePaths);
        if (FailOnReview is not null)
        {
            throw FailOnReview;
        }

        ReviewRequests.Add((folder, text));
        return Task.FromResult(Review);
    }

    public Task<string> CreateSessionAsync(string workspacePath, string title, AgentSetup setup, CancellationToken cancellationToken)
    {
        SessionSetups.Add(setup);
        var id = $"ses_{++_sessions}";
        _engines[id] = setup.Engine;
        return Task.FromResult(id);
    }

    public AgentEngine EngineOf(string sessionId) => _engines.GetValueOrDefault(sessionId, AgentEngine.OpenCode);

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

    /// <summary>Task branches that conflict with whatever they are merged into.</summary>
    public HashSet<string> Conflicting { get; } = [];

    public List<(Workspace Workspace, string Target)> CatchUps { get; } = [];

    public Task MergeAsync(string repositoryPath, string sourceBranch, string targetBranch, string message, CancellationToken cancellationToken)
    {
        if (MergeProblem is not null)
        {
            throw MergeProblem;
        }

        if (Conflicting.Contains(sourceBranch))
        {
            throw new RejectedException("merge-conflict", $"The work conflicts with {targetBranch} in src/cart.ts.");
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

    public Task<IReadOnlyList<string>> StartCatchUpAsync(
        string repositoryPath, Workspace workspace, string targetBranch, CancellationToken cancellationToken)
    {
        CatchUps.Add((workspace, targetBranch));
        return Task.FromResult<IReadOnlyList<string>>(["src/cart.ts"]);
    }

    public List<string> Scratches { get; } = [];

    public List<string> ClosedScratches { get; } = [];

    public Task<string> OpenScratchAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        var path = $"/srv/worktrees/check-{Scratches.Count + 1}";
        Scratches.Add(path);
        return Task.FromResult(path);
    }

    public Task CloseScratchAsync(string repositoryPath, string path, CancellationToken cancellationToken)
    {
        ClosedScratches.Add(path);
        return Task.CompletedTask;
    }

    public List<string> Pushed { get; } = [];

    public List<string> Updated { get; } = [];

    public Task<string> PushAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        if (MergeProblem is not null)
        {
            throw MergeProblem;
        }

        Pushed.Add(branch);
        return Task.FromResult($"origin/{branch}");
    }

    public Task UpdateFromRemoteAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        Updated.Add(branch);
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

    public Task<IReadOnlyList<WorkTaskId>> TasksActiveBetweenAsync(DateTimeOffset since, DateTimeOffset until, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WorkTaskId>>([.. Items.Where(e => e.At >= since && e.At < until).Select(e => e.TaskId).Distinct()]);

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

internal sealed class InMemoryReports : IReportRepository
{
    public List<Domain.Reports.Report> Items { get; } = [];

    public Task<Domain.Reports.Report?> FindAsync(Domain.Reports.ReportId id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Find(r => r.Id == id));

    public Task<IReadOnlyList<Domain.Reports.Report>> ListAsync(OfficeId officeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Domain.Reports.Report>>([.. Items.Where(r => r.OfficeId == officeId)]);

    public void Add(Domain.Reports.Report report) => Items.Add(report);
}

/// <summary>Options that read their value when asked, as a monitor of live settings does.</summary>
internal sealed class FakeGpuProbe : LocalModels.IGpuProbe
{
    public LocalModels.GpuMemory? Memory { get; set; } = new(500, 12288);

    /// <summary>The command lines of the programs running now.</summary>
    public List<string> Programs { get; } = [];

    public Task<LocalModels.GpuMemory?> MemoryAsync(CancellationToken cancellationToken) => Task.FromResult(Memory);

    public IReadOnlyList<string> RunningHolders(IReadOnlyList<LocalModels.GpuHolder> holders) =>
        [.. holders.Where(h => Programs.Exists(p => p.Contains(h.Match, StringComparison.OrdinalIgnoreCase))).Select(h => h.Name)];
}

internal sealed class FakeModelServer : LocalModels.ILocalModelServer
{
    public string? Running { get; set; }

    /// <summary>Whether the started model answers yet; something else may answer too.</summary>
    public bool Answering { get; set; }

    public List<string> Started { get; } = [];

    public int Stops { get; private set; }

    public string? LastFailure { get; set; }

    public Task StartAsync(LocalModels.LocalModel model, CancellationToken cancellationToken)
    {
        Started.Add(model.Id);
        Running = model.Id;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Running = null;
        Answering = false;
        Stops++;
        return Task.CompletedTask;
    }

    public Task<bool> IsAnsweringAsync(CancellationToken cancellationToken) => Task.FromResult(Answering);
}

internal sealed class LiveOptions<T>(Func<T> read) : Microsoft.Extensions.Options.IOptionsMonitor<T>
{
    public T CurrentValue => read();

    public T Get(string? name) => read();

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

internal static class Fakes
{
    public static Agents.AgentAction Run(string command) => new(Agents.ActionKind.Run, command);
}

internal sealed class FakeCheckRunner : ICheckRunner
{
    /// <summary>What each command answers; anything else passes.</summary>
    public Dictionary<string, CheckRun> Answers { get; } = [];

    public List<(string Folder, string Command)> Runs { get; } = [];

    public Task<CheckRun> RunAsync(string folder, string command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Runs.Add((folder, command));
        return Task.FromResult(Answers.GetValueOrDefault(command, new CheckRun(0, "ok", TimeSpan.FromSeconds(2))));
    }
}

internal sealed class FakePullRequests : IPullRequests
{
    public bool Available { get; set; } = true;

    public PullRequestState State { get; set; } = PullRequestState.Open;

    public List<(string Branch, string Target, string Title, string Body)> Opened { get; } = [];

    public Task<bool> AvailableAsync(string repositoryPath, CancellationToken cancellationToken) => Task.FromResult(Available);

    public Task<string> OpenAsync(string repositoryPath, string branch, string target, string title, string body, CancellationToken cancellationToken)
    {
        Opened.Add((branch, target, title, body));
        return Task.FromResult($"https://github.com/e-corp/shop/pull/{Opened.Count}");
    }

    public Task<PullRequestState> StateAsync(string repositoryPath, string url, CancellationToken cancellationToken) => Task.FromResult(State);
}
