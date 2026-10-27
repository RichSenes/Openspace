using Microsoft.Extensions.Options;

namespace Openspace.Application.LocalModels;

public enum GpuState
{
    /// <summary>Local models are not set up.</summary>
    Off,

    /// <summary>Nothing of Openspace's on the card.</summary>
    Free,

    Loading,

    /// <summary>A model is loaded and takes local tasks.</summary>
    Serving,

    /// <summary>A desk waits for a model, but the card has too little memory free.</summary>
    WaitingForMemory,

    /// <summary>A program asked for the card: local tasks are finishing before the model is unloaded.</summary>
    Yielding,

    /// <summary>The card is a program's: local tasks wait.</summary>
    Lent,

    /// <summary>The model did not load; Openspace tries again later.</summary>
    Failed,
}

/// <param name="Model">The local model loaded, loading or waited for, as <c>provider/id</c>.</param>
/// <param name="Holders">The programs the card is lent to, or being freed for.</param>
/// <param name="Detail">A sentence for people, when there is more to say than the state.</param>
public sealed record GpuView(GpuState State, string? Model, string? ModelName, IReadOnlyList<string> Holders, string? Detail);

/// <summary>
/// Who has the graphics card. Programs listed as holders, and those that claim it through
/// the API, always come first; local models take it only when it is free and give it back
/// when nobody uses them. Decisions are taken by <see cref="GpuShift"/>; the queue asks here
/// whether a local task may start.
/// </summary>
public sealed class GpuArbiter(IOptionsMonitor<LocalModelOptions> options, TimeProvider clock)
{
    /// <summary>A desk that stopped asking for a model for this long no longer wants it.</summary>
    private static readonly TimeSpan WantedFor = TimeSpan.FromSeconds(30);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _claims = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTimeOffset First, DateTimeOffset Last)> _wanted = new(StringComparer.Ordinal);
    private GpuView _view = new(GpuState.Off, null, null, [], null);
    private bool _busy;
    private int _leases;

    public bool Enabled => options.CurrentValue.Enabled;

    /// <summary>When the model being loaded was started.</summary>
    internal DateTimeOffset? LoadingSince { get; set; }

    /// <summary>When a local task last ran or asked for the loaded model.</summary>
    internal DateTimeOffset LastUsed { get; set; }

    /// <summary>When a program first asked for the card, while it still has it.</summary>
    internal DateTimeOffset? YieldingSince { get; set; }

    internal DateTimeOffset? RetryAt { get; set; }

    /// <summary>Whether the model runs on this machine, through Openspace's provider.</summary>
    public bool IsLocal(string? model) =>
        Enabled && model is not null && model.StartsWith(options.CurrentValue.Provider + "/", StringComparison.Ordinal);

    /// <summary>
    /// Asked by the queue for a task on a local model: true when the model is loaded and free
    /// to take it. Either way the request is remembered, so the model gets loaded.
    /// </summary>
    public bool Admit(string model)
    {
        var id = model[(model.IndexOf('/', StringComparison.Ordinal) + 1)..];
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            _wanted[id] = (_wanted.TryGetValue(id, out var seen) ? seen.First : now, now);
            if (_busy || _leases > 0 || _view.State != GpuState.Serving || _view.Model != model)
            {
                return false;
            }

            // One local task at a time: the server answers one request after the other anyway.
            // Desks with more work for this model ask again on the next round.
            _busy = true;
            _wanted.Remove(id);
            return true;
        }
    }

    /// <summary>
    /// A routine asks to use the loaded model now. Null while the model is not loaded or a
    /// task or another routine uses it; the request is remembered either way, so the model
    /// gets loaded. Dispose the lease when done.
    /// </summary>
    public IDisposable? TryLease(string model)
    {
        var id = model[(model.IndexOf('/', StringComparison.Ordinal) + 1)..];
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            _wanted[id] = (_wanted.TryGetValue(id, out var seen) ? seen.First : now, now);
            if (_busy || _leases > 0 || _view.State != GpuState.Serving || _view.Model != model)
            {
                return null;
            }

            _leases++;
            _wanted.Remove(id);
            return new Lease(this);
        }
    }

    /// <summary>Routines using the model right now.</summary>
    internal int Leases
    {
        get
        {
            lock (_gate)
            {
                return _leases;
            }
        }
    }

    private void EndLease()
    {
        lock (_gate)
        {
            _leases = Math.Max(0, _leases - 1);
        }
    }

    private sealed class Lease(GpuArbiter arbiter) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
            {
                arbiter.EndLease();
            }
        }
    }

    /// <summary>A program takes the card, or renews its claim.</summary>
    public GpuView Claim(string holder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder);
        lock (_gate)
        {
            _claims[holder.Trim()] = clock.GetUtcNow() + options.CurrentValue.ClaimLease;
            return _view;
        }
    }

    public GpuView Release(string holder)
    {
        lock (_gate)
        {
            _claims.Remove(holder.Trim());
            return _view;
        }
    }

    public IReadOnlyList<string> Claims()
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var lapsed in _claims.Where(c => c.Value <= now).Select(c => c.Key).ToList())
            {
                _claims.Remove(lapsed);
            }

            return [.. _claims.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        }
    }

    /// <summary>The model desks have waited for the longest, among those still asking.</summary>
    internal string? Wanted()
    {
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var stale in _wanted.Where(w => now - w.Value.Last > WantedFor).Select(w => w.Key).ToList())
            {
                _wanted.Remove(stale);
            }

            return _wanted.OrderBy(w => w.Value.First).Select(w => w.Key).FirstOrDefault();
        }
    }

    internal void Forget(string id)
    {
        lock (_gate)
        {
            _wanted.Remove(id);
        }
    }

    internal void Report(GpuView view, bool busy)
    {
        lock (_gate)
        {
            _view = view;
            _busy = busy;
        }
    }

    public GpuView View()
    {
        lock (_gate)
        {
            return Enabled ? _view : new GpuView(GpuState.Off, null, null, [], null);
        }
    }
}
