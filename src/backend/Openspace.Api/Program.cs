using System.Text.Json.Serialization;
using Openspace.Api;
using Openspace.Api.Endpoints;
using Openspace.Api.Mcp;
using Openspace.Api.Realtime;
using Openspace.Application;
using Openspace.Application.Abstractions;
using Openspace.Application.Queue;
using Openspace.Infrastructure;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.OpenCode;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<IDomainEventPublisher, SignalRDomainEventPublisher>();
builder.Services.AddSingleton<IActivityPublisher, SignalRActivityPublisher>();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Openspace")
        ?? throw new InvalidOperationException("Missing connection string 'Openspace'."))
    .AddAgents();
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
builder.Services.Configure<OpenCodeOptions>(builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.Configure<WorkspaceOptions>(options =>
{
    builder.Configuration.GetSection(WorkspaceOptions.SectionName).Bind(options);
    if (string.IsNullOrWhiteSpace(options.Root))
    {
        options.Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openspace", "worktrees");
    }
});
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.SectionName));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandling>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "openspace", Version = "1.0.0" })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<OpenspaceTools>();
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapOfficeEndpoints();
app.MapTaskEndpoints();
app.MapHub<BuildingHub>(BuildingHub.Path);
app.MapMcp("/mcp");

// Hello, friend.
app.MapGet("/", () => "Openspace is running.");

await app.RunAsync();

/// <summary>Exposed for architecture and integration tests.</summary>
public partial class Program;
