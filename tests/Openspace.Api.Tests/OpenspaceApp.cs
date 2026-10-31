using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Abstractions;
using Openspace.Application.Weather;

namespace Openspace.Api.Tests;

/// <summary>The real API host on a private in-memory database.</summary>
public sealed class OpenspaceApp(IAgentRuntime? runtime = null) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepAlive;

    /// <summary>Settings changed through the API land here, never next to the real database.</summary>
    public string SettingsPath { get; } = Path.Combine(Path.GetTempPath(), $"openspace-settings-{Guid.NewGuid():N}.json");

    /// <summary>Sounds uploaded through the API land here, never in the project folder.</summary>
    public string SoundsFolder { get; } = Path.Combine(Path.GetTempPath(), $"openspace-sounds-{Guid.NewGuid():N}");

    private SqliteConnection KeepAlive()
    {
        _keepAlive ??= new SqliteConnection(_connectionString);
        if (_keepAlive.State != System.Data.ConnectionState.Open)
        {
            _keepAlive.Open();
        }

        return _keepAlive;
    }

    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "hubs/building"), o => o.HttpMessageHandlerFactory = _ => Server.CreateHandler())
            .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        KeepAlive();
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Openspace"] = _connectionString,
            ["Settings:Path"] = SettingsPath,
            ["Sounds:Folder"] = SoundsFolder,

            // Tests must never reach a real OpenCode: port 9 (discard) refuses at once.
            ["OpenCode:BaseUrl"] = "http://127.0.0.1:9",
            ["OpenCode:ListenToEvents"] = "false",

            // Never the Claude Code of the machine running the tests: it would use a real account.
            ["ClaudeCode:Enabled"] = "false",

            // Never the graphics card of the machine running the tests: no model server is started.
            ["LocalModels:Enabled"] = "false",

            // Never the person's Telegram: routines in tests find no bots to write with.
            ["Telegram:Folder"] = Path.Combine(Path.GetTempPath(), $"openspace-no-telegram-{Guid.NewGuid():N}"),

            // Never the person's dictionaries: the kanji of the day finds none.
            ["Kanji:Folder"] = Path.Combine(Path.GetTempPath(), $"openspace-no-kanji-{Guid.NewGuid():N}"),
            ["Google:Folder"] = Path.Combine(Path.GetTempPath(), $"openspace-no-google-{Guid.NewGuid():N}"),
            ["Operator:Name"] = "elliot",
            ["Queue:Enabled"] = "false",
        };
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        // Added last, so the person's appsettings.Local.json, read by the API itself, never wins over them.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        // Never the real weather service: one made-up town, always raining.
        builder.ConfigureTestServices(services => services.AddSingleton<IWeatherSource, MadeUpWeather>());
        if (runtime is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(runtime));
        }

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive?.Dispose();
            File.Delete(SettingsPath);
            if (Directory.Exists(SoundsFolder))
            {
                Directory.Delete(SoundsFolder, recursive: true);
            }
        }
    }
}

/// <summary>A weather service that knows one town, where it always rains.</summary>
internal sealed class MadeUpWeather : IWeatherSource
{
    public Task<Place?> FindAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(name.StartsWith("Gotham", StringComparison.OrdinalIgnoreCase) ? new Place("Gotham, United States", 40.7, -74) : null);

    public Task<Reading> CurrentAsync(Place place, CancellationToken cancellationToken) => Task.FromResult(new Reading(Sky.Rain, 11));
}
