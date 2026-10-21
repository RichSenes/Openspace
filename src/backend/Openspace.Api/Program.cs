using System.Text.Json.Serialization;
using Openspace.Api;
using Openspace.Api.Endpoints;
using Openspace.Api.Realtime;
using Openspace.Application;
using Openspace.Application.Abstractions;
using Openspace.Application.Queue;
using Openspace.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddSingleton<IDomainEventPublisher, SignalRDomainEventPublisher>();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration.GetConnectionString("Openspace")
        ?? throw new InvalidOperationException("Missing connection string 'Openspace'."));
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
builder.Services.Configure<OperatorOptions>(builder.Configuration.GetSection(OperatorOptions.SectionName));

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErrorHandling>();
builder.Services.AddSignalR();

var app = builder.Build();
await app.Services.MigrateDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapOfficeEndpoints();
app.MapTaskEndpoints();
app.MapHub<BuildingHub>(BuildingHub.Path);

// Hello, friend.
app.MapGet("/", () => "Openspace is running.");

await app.RunAsync();

/// <summary>Exposed for architecture and integration tests.</summary>
public partial class Program;
