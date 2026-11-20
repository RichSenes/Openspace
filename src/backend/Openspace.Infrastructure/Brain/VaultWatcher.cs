using Microsoft.Extensions.Logging;

namespace Openspace.Infrastructure.Brain;

/// <summary>
/// Watches the vault's folder for notes changed outside Openspace (in Obsidian, say) and reports
/// them once the edits settle: editors save in bursts, and one read per burst is enough. When the
/// watcher loses track (too many changes at once, a folder moved in or out) it asks for everything
/// to be read again.
/// </summary>
internal sealed partial class VaultWatcher : IDisposable
{
    private readonly string _folder;
    private readonly TimeSpan _delay;
    private readonly Func<IReadOnlyCollection<string>?, Task> _changed;
    private readonly ILogger _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _one = new(1, 1);
    private HashSet<string> _paths = new(StringComparer.Ordinal);
    private bool _everything;

    /// <param name="changed">Called with the notes' vault paths, or with null when everything must be read again.</param>
    public VaultWatcher(string folder, TimeSpan delay, Func<IReadOnlyCollection<string>?, Task> changed, ILogger logger)
    {
        _folder = folder;
        _delay = delay;
        _changed = changed;
        _logger = logger;
        _timer = new Timer(_ => _ = FlushAsync(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Saw(e.FullPath);
        _watcher.Changed += (_, e) => Saw(e.FullPath);
        _watcher.Deleted += (_, e) => Saw(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Saw(e.OldFullPath);
            Saw(e.FullPath);
        };
        _watcher.Error += (_, _) => SawEverything();
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
        _one.Dispose();
    }

    private void Saw(string fullPath)
    {
        var path = Path.GetRelativePath(_folder, fullPath).Replace('\\', '/');
        var segments = path.Split('/');
        if (path.StartsWith("..", StringComparison.Ordinal) || segments.Any(s => s.StartsWith('.')))
        {
            // Git's own files, Obsidian's settings and Openspace's half-written notes.
            return;
        }

        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            lock (_gate)
            {
                _paths.Add(path);
            }
        }
        else if (Path.GetExtension(path).Length == 0)
        {
            // Most likely a folder: whatever it held has come or gone with it.
            SawEverything();
            return;
        }
        else
        {
            return;
        }

        _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    private void SawEverything()
    {
        lock (_gate)
        {
            _everything = true;
        }

        _timer.Change(_delay, Timeout.InfiniteTimeSpan);
    }

    private async Task FlushAsync()
    {
        // A burst that lands while the last one is being read waits for it, then goes on its own.
        try
        {
            await _one.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Openspace is stopping.
            return;
        }

        try
        {
            HashSet<string> paths;
            bool everything;
            lock (_gate)
            {
                (paths, everything) = (_paths, _everything);
                (_paths, _everything) = (new(StringComparer.Ordinal), false);
            }

            if (everything || paths.Count > 0)
            {
                await _changed(everything ? null : paths).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogFailed(_logger, ex);
        }
        finally
        {
            _one.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notes changed in the vault could not be read again")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
