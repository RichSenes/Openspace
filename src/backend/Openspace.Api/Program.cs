using System.Text.Json.Serialization;
using Openspace.Api;
using Openspace.Api.Endpoints;
using Openspace.Api.Mcp;
using Openspace.Api.Realtime;
using Openspace.Api.Settings;
using Openspace.Application;
using Openspace.Application.Agents;
using Openspace.Application.Abstractions;
using Openspace.Application.Queue;
using Openspace.Infrastructure;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.OpenCode;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Settings changed from Openspace itself come last, so they win over appsettings.
var settingsFile = Path.GetFullPath(
    builder.Configuration[$"{SettingsFileOptions.SectionName}:Path"] ?? new SettingsFileOptions().Path,
    builder.Environment.ContentRootPath);
builder.Configuration.AddJsonFile(settingsFile, optional: true, reloadOnChange: false);
builder.Services.Configure<SettingsFileOptions>(o => o.Path = settingsFile);
builder.Services.Configure<Openspace.Api.Sounds.SoundOptions>(builder.Configuration.GetSection(Openspace.Api.Sounds.SoundOptions.SectionName));
builder.Services.AddSingleton<Openspace.Api.Sounds.SoundStore>();

builder.Services.AddSingleton<IDomainEventPublisher, SignalRDomainEventPublisher>();
builder.Services.AddSingleton<IActivityPublisher, SignalRActivityPublisher>();
builder.Services.AddSingleton<Openspace.Application.Routines.IRoutineProgress, SignalRRoutineProgress>();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Openspace")
        ?? throw new InvalidOperationException("Missing connection string 'Openspace'."))
    .AddAgents();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<Openspace.Infrastructure.Git.GitHubOptions>(builder.Configuration.GetSection(Openspace.Infrastructure.Git.GitHubOptions.SectionName));
builder.Services.Configure<Openspace.Infrastructure.Persistence.BackupOptions>(builder.Configuration.GetSection(Openspace.Infrastructure.Persistence.BackupOptions.SectionName));
builder.Services.Configure<Openspace.Application.Checks.CheckOptions>(builder.Configuration.GetSection(Openspace.Application.Checks.CheckOptions.SectionName));
builder.Services.Configure<Openspace.Infrastructure.Telegram.TelegramOptions>(builder.Configuration.GetSection(Openspace.Infrastructure.Telegram.TelegramOptions.SectionName));
builder.Services.Configure<Openspace.Infrastructure.Routines.PdfOptions>(builder.Configuration.GetSection(Openspace.Infrastructure.Routines.PdfOptions.SectionName));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
builder.Services.Configure<Openspace.Application.LocalModels.LocalModelOptions>(builder.Configuration.GetSection(Openspace.Application.LocalModels.LocalModelOptions.SectionName));
builder.Services.Configure<OpenCodeOptions>(builder.Configuration.GetSection(OpenCodeOptions.SectionName));
builder.Services.Configure<ClaudeCodeOptions>(builder.Configuration.GetSection(ClaudeCodeOptions.SectionName));
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
app.MapRoleEndpoints();
app.MapEpicEndpoints();
app.MapReportEndpoints();
app.MapEngineEndpoints();
app.MapGpuEndpoints();
app.MapRoutineEndpoints();
app.MapSettingsEndpoints();
Openspace.Api.Sounds.SoundEndpoints.MapSoundEndpoints(app);
app.MapHub<BuildingHub>(BuildingHub.Path);
app.MapMcp("/mcp");

// Hello, friend.
app.MapGet("/", () => "Openspace is running.");

await app.RunAsync();

/// <summary>Exposed for architecture and integration tests.</summary>
public partial class Program;
