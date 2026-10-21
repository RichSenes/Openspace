using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Openspace.Application;

namespace Openspace.Infrastructure.Tests;

/// <summary>
/// A private in-memory SQLite database per test, built through the real migrations.
/// Each <see cref="ScopeAsync"/> is a fresh DbContext, like a separate HTTP request.
/// </summary>
public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _connectionString = $"Data Source=openspace-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepAlive;
    private ServiceProvider? _services;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2015, 6, 24, 9, 0, 0, TimeSpan.Zero));

    public async Task InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(_connectionString)
            .AddAgents()
            .AddSingleton<TimeProvider>(Clock);
        services.Configure<Application.Queue.QueueOptions>(o =>
        {
            o.Seats = 2;
            o.Enabled = false;
        });
        services.Configure<OpenCode.OpenCodeOptions>(o =>
        {
            o.ListenToEvents = false;

            // Never reach a real OpenCode from tests: port 9 refuses at once.
            o.BaseUrl = new Uri("http://127.0.0.1:9");
        });
        _services = services.BuildServiceProvider();

        await _services.MigrateDatabaseAsync();
    }

    public AsyncServiceScope Scope() => _services!.CreateAsyncScope();

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }
}
