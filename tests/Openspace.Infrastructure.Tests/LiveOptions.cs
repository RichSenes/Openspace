using Microsoft.Extensions.Options;

namespace Openspace.Infrastructure.Tests;

/// <summary>A monitor of settings that never change, for code that reads live settings.</summary>
internal sealed class LiveOptions<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
