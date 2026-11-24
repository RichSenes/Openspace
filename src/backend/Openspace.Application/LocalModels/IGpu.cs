namespace Openspace.Application.LocalModels;

public sealed record GpuMemory(int UsedMiB, int TotalMiB)
{
    public int FreeMiB => TotalMiB - UsedMiB;
}

/// <summary>
/// What the graphics card is doing, told without looking inside anybody's work: only the
/// memory in use and which programs are running.
/// </summary>
public interface IGpuProbe
{
    /// <summary>Null when the card cannot be read (no driver tool on this machine).</summary>
    Task<GpuMemory?> MemoryAsync(CancellationToken cancellationToken);

    /// <summary>The names of the holders running now.</summary>
    IReadOnlyList<string> RunningHolders(IReadOnlyList<GpuHolder> holders);

    /// <summary>The programs with memory on the card now, by name, largest first; empty when it cannot be read.</summary>
    Task<IReadOnlyList<GpuUser>> UsersAsync(CancellationToken cancellationToken);
}

/// <summary>A program using graphics memory: <paramref name="Openspace"/> when it is one of Openspace's own (her voice, her ears).</summary>
public sealed record GpuUser(string Name, int MiB, bool Openspace);

/// <summary>
/// One of Openspace's own programs on the card that can give its memory back when a model needs
/// it, such as Shiori's voice (it loads again by itself when she next speaks).
/// </summary>
public interface IGpuGuest
{
    /// <summary>The name <see cref="IGpuProbe.UsersAsync"/> gives it.</summary>
    string Name { get; }

    /// <summary>Frees its memory when it has been unused for <paramref name="quietFor"/>; true when it is not on the card any more.</summary>
    Task<bool> MakeRoomAsync(TimeSpan quietFor, CancellationToken cancellationToken);
}

/// <summary>The one model server Openspace starts on this machine.</summary>
public interface ILocalModelServer
{
    /// <summary>The model whose server Openspace started and is still running; null otherwise.</summary>
    string? Running { get; }

    Task StartAsync(LocalModel model, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Why the last server Openspace started stopped by itself, in a few words; null when it did not.</summary>
    string? LastFailure { get; }

    /// <summary>Whether a server answers at the address, started by Openspace or not.</summary>
    Task<bool> IsAnsweringAsync(CancellationToken cancellationToken);
}
