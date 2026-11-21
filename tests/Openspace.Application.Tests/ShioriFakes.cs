using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Abstractions;
using Openspace.Application.Functions;
using Openspace.Application.Shiori;
using Openspace.Application.Work;

namespace Openspace.Application.Tests;

/// <summary>
/// Sessions kept as copies, the way a database keeps them: a change counts only once it is saved,
/// so a service that forgets to save is caught.
/// </summary>
internal sealed class InMemoryShioriStore : IShioriSessionStore
{
    private readonly Dictionary<Guid, ShioriSession> _sessions = [];

    public int Saves { get; private set; }

    public Task<ShioriSession?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.TryGetValue(id, out var s) ? Copy(s) : null);

    public Task<ShioriSession?> FindByTokenAsync(string token, CancellationToken cancellationToken) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(s => s.Token == token) is { } s ? Copy(s) : null);

    public Task<IReadOnlyList<ShioriSession>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ShioriSession>>([.. _sessions.Values.OrderByDescending(s => s.UpdatedAt).Select(Copy)]);

    public Task AddAsync(ShioriSession session, CancellationToken cancellationToken)
    {
        _sessions.Add(session.Id, Copy(session));
        return Task.CompletedTask;
    }

    public Task SaveAsync(ShioriSession session, CancellationToken cancellationToken)
    {
        _sessions[session.Id] = Copy(session);
        Saves++;
        return Task.CompletedTask;
    }

    private static ShioriSession Copy(ShioriSession s) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Person = s.Person,
        Mode = s.Mode,
        RuntimeSessionId = s.RuntimeSessionId,
        RuntimeStarted = s.RuntimeStarted,
        Token = s.Token,
        NotePath = s.NotePath,
        Transcript = [.. s.Transcript],
        Board = [.. s.Board],
        NextItem = s.NextItem,
        CreatedAt = s.CreatedAt,
        UpdatedAt = s.UpdatedAt,
        EndedAt = s.EndedAt,
    };
}

/// <summary>Shiori's engine as a test sets it: answers in pieces, can be held, can fail, can call tools meanwhile.</summary>
internal sealed class FakeShioriRuntime : IShioriRuntime
{
    public List<ShioriTurn> Turns { get; } = [];

    /// <summary>The pieces of the next answers; "Noted." when none is queued.</summary>
    public Queue<string[]> Answers { get; } = new();

    /// <summary>When set, answers wait for it before ending.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public Exception? Fail { get; set; }

    /// <summary>Called while the answer is being written, as an engine calling tools would.</summary>
    public Func<ShioriTurn, Task>? During { get; set; }

    public List<string> Stopped { get; } = [];

    public async Task<string> AnswerAsync(ShioriTurn turn, Func<string, Task> onText, CancellationToken cancellationToken)
    {
        Turns.Add(turn);
        if (During is not null)
        {
            await During(turn);
        }

        if (Hold is not null)
        {
            await Hold.Task;
        }

        if (Fail is not null)
        {
            throw Fail;
        }

        var pieces = Answers.Count > 0 ? Answers.Dequeue() : ["Noted."];
        foreach (var piece in pieces)
        {
            await onText(piece);
        }

        return string.Concat(pieces);
    }

    public Task StopAsync(string runtimeSessionId)
    {
        Stopped.Add(runtimeSessionId);
        Hold?.TrySetResult();
        return Task.CompletedTask;
    }
}

internal sealed class RecordingShioriPublisher : IShioriPublisher
{
    public List<(Guid Session, string Chunk)> Texts { get; } = [];

    public List<(Guid Session, TranscriptEntry Entry)> Messages { get; } = [];

    public List<(Guid Session, IReadOnlyList<WhiteboardItem> Board)> Boards { get; } = [];

    public List<ShioriState> States { get; } = [];

    public Task TextAsync(Guid sessionId, string chunk, CancellationToken cancellationToken)
    {
        Texts.Add((sessionId, chunk));
        return Task.CompletedTask;
    }

    public Task MessageAsync(Guid sessionId, TranscriptEntry entry, CancellationToken cancellationToken)
    {
        Messages.Add((sessionId, entry));
        return Task.CompletedTask;
    }

    public Task BoardAsync(Guid sessionId, IReadOnlyList<WhiteboardItem> board, CancellationToken cancellationToken)
    {
        Boards.Add((sessionId, board));
        return Task.CompletedTask;
    }

    public Task StateAsync(ShioriState state, CancellationToken cancellationToken)
    {
        States.Add(state);
        return Task.CompletedTask;
    }
}

/// <summary>A scope that gives out the few services Shiori asks for in the background.</summary>
internal sealed class FewServices(Dictionary<Type, Func<object>> services) : IServiceScopeFactory, IServiceScope, IServiceProvider
{
    public IServiceProvider ServiceProvider => this;

    public IServiceScope CreateScope() => this;

    public object? GetService(Type serviceType) => services.TryGetValue(serviceType, out var make) ? make() : null;

    public void Dispose()
    {
    }
}

/// <summary>Shiori over the E Corp harness: its sessions, its tools and every fake they use.</summary>
internal sealed class ShioriHarness
{
    public Harness H { get; } = new();

    public InMemoryShioriStore Store { get; } = new();

    public FakeShioriRuntime Runtime { get; } = new();

    public RecordingShioriPublisher Publisher { get; } = new();

    public FunctionModelOptions Models { get; } = new();

    public SupervisorJobs Jobs { get; }

    private readonly FewServices _services;

    public ShioriHarness()
    {
        _services = new FewServices(new()
        {
            [typeof(IOfficeRepository)] = () => H.Offices,
            [typeof(Supervisor)] = () => H.Supervisor(),
        });
        Jobs = new SupervisorJobs(_services, H.Publisher, H.Clock, NullLogger<SupervisorJobs>.Instance);
    }

    /// <summary>A new service over the same store, as after a restart of Openspace.</summary>
    public ShioriSessions Sessions() =>
        new(Store, Runtime, H.Vault, Publisher, new LiveOptions<FunctionModelOptions>(() => Models), _services, H.Clock, NullLogger<ShioriSessions>.Instance);

    public ShioriActions Actions(ShioriSessions sessions) =>
        new(sessions, H.Vault, new Brain.BrainSearch(new InMemoryBrainIndex(H.Vault)), H.Offices, Jobs, H.Supervisor(), H.Clock);

    /// <summary>The secret a session's tools carry, as the engine would get it.</summary>
    public async Task<string> TokenAsync(Guid session) => (await Store.FindAsync(session, default))!.Token;
}
