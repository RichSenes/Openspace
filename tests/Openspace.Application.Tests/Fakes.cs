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

    public List<(string SessionId, AgentProfile Agent, string Text)> Prompts { get; } = [];

    public List<string> Aborted { get; } = [];

    public List<(string PermissionId, bool Allow)> PermissionReplies { get; } = [];

    public string? LastReply { get; set; } = "Added the button.";

    public Exception? FailOnPrompt { get; set; }

    private int _sessions;

    public Task<bool> HasAgentAsync(string agentName, string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult(Agents.Contains(agentName));

    public Task<string> CreateSessionAsync(string workspacePath, string title, CancellationToken cancellationToken) =>
        Task.FromResult($"ses_{++_sessions}");

    public Task PromptAsync(string sessionId, string workspacePath, AgentProfile agent, string text, CancellationToken cancellationToken)
    {
        if (FailOnPrompt is not null)
        {
            throw FailOnPrompt;
        }

        Prompts.Add((sessionId, agent, text));
        return Task.CompletedTask;
    }

    public Task AbortAsync(string sessionId, string workspacePath, CancellationToken cancellationToken)
    {
        Aborted.Add(sessionId);
        return Task.CompletedTask;
    }

    public Task<string?> GetLastReplyAsync(string sessionId, string workspacePath, CancellationToken cancellationToken) =>
        Task.FromResult(LastReply);

    public Task ReplyToPermissionAsync(string permissionId, string workspacePath, bool allow, CancellationToken cancellationToken)
    {
        PermissionReplies.Add((permissionId, allow));
        return Task.CompletedTask;
    }
}

internal sealed class FakeWorkspaces : IWorkspaceManager
{
    public List<Workspace> Prepared { get; } = [];

    public List<(string Workspace, string Message)> Commits { get; } = [];

    public List<Workspace> Discarded { get; } = [];

    public Task<Workspace> PrepareAsync(string repositoryPath, string taskKey, string title, CancellationToken cancellationToken)
    {
        var workspace = new Workspace($"/srv/worktrees/{taskKey}", $"openspace/{taskKey}");
        Prepared.Add(workspace);
        return Task.FromResult(workspace);
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
}
