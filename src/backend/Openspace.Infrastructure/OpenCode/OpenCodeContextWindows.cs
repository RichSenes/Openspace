namespace Openspace.Infrastructure.OpenCode;

/// <summary>
/// Remembers how much each OpenCode model holds, so every answer does not ask the server
/// again. The list is read again now and then, and when a model is not on it yet.
/// </summary>
internal sealed class OpenCodeContextWindows(TimeProvider clock) : IDisposable
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _reading = new(1, 1);
    private IReadOnlyDictionary<string, long> _windows = new Dictionary<string, long>();
    private DateTimeOffset _readAt = DateTimeOffset.MinValue;

    /// <summary>The window of <paramref name="model"/>, or null when OpenCode does not say or cannot be reached.</summary>
    public async Task<long?> ForAsync(string model, OpenCodeRuntime runtime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var age = clock.GetUtcNow() - _readAt;
        if (_windows.TryGetValue(model, out var known) && age < Fresh)
        {
            return known;
        }

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (clock.GetUtcNow() - _readAt >= RetryAfter)
            {
                _readAt = clock.GetUtcNow();
                try
                {
                    _windows = await runtime.ContextWindowsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
                {
                    // Unknown for now: the bar stays hidden until the next try.
                }
            }
        }
        finally
        {
            _reading.Release();
        }

        return _windows.TryGetValue(model, out var window) ? window : null;
    }

    public void Dispose() => _reading.Dispose();
}
