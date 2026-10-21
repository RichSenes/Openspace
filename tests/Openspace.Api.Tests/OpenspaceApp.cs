using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Openspace.Application.Abstractions;

namespace Openspace.Api.Tests;

/// <summary>The real API host on a private in-memory database.</summary>
public sealed class OpenspaceApp(IAgentRuntime? runtime = null) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private SqliteConnection? _keepAlive;

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
        builder.UseSetting("ConnectionStrings:Openspace", _connectionString);

        // Tests must never reach a real OpenCode: port 9 (discard) refuses at once.
        builder.UseSetting("OpenCode:BaseUrl", "http://127.0.0.1:9");
        if (runtime is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton(runtime));
        }
        builder.UseSetting("Operator:Name", "elliot");
        builder.UseSetting("Queue:Enabled", "false");
        builder.UseSetting("OpenCode:ListenToEvents", "false");
        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _keepAlive?.Dispose();
        }
    }
}
