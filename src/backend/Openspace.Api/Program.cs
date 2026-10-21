using Openspace.Application;
using Openspace.Application.Queue;
using Openspace.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Openspace")
        ?? throw new InvalidOperationException("Missing connection string 'Openspace'."));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));

var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Hello, friend.
app.MapGet("/", () => "Openspace is running.");

await app.RunAsync();

/// <summary>Exposed for architecture and integration tests.</summary>
public partial class Program;
