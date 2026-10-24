using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Agents;
using Openspace.Application.Queue;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Api.Settings;

/// <param name="SupervisorModel">Claude model for plans and drafts; empty for Claude Code's default.</param>
/// <param name="DraftModel">OpenCode model for plans and drafts when Claude Code does not make them.</param>
public sealed record SettingsView(
    Autonomy Autonomy,
    int Seats,
    bool ClaudeSupervises,
    string? SupervisorModel,
    bool ClaudeReviews,
    string? ReviewModel,
    string? DraftModel);

/// <summary>
/// What a person changes from Openspace itself. Changes are written to a settings file that
/// overrides appsettings, and apply at once: no restart.
/// </summary>
internal static class SettingsEndpoints
{
    public const int MaxSeats = 12;

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings").WithTags("Settings");
        settings.MapGet("/", Read);
        app.MapGet("/api/backups", (IDatabaseBackups backups, CancellationToken ct) => backups.ListAsync(ct)).WithTags("Settings");
        app.MapPost("/api/backups", (IDatabaseBackups backups, CancellationToken ct) => backups.BackUpAsync(ct)).WithTags("Settings");
        settings.MapPut("/", (SettingsView request, IConfiguration configuration, IOptions<SettingsFileOptions> file,
            IOptionsMonitor<AgentOptions> agents, IOptionsMonitor<QueueOptions> queue,
            IOptionsMonitor<ClaudeCodeOptions> claude, IOptionsMonitor<OpenCodeOptions> openCode) =>
        {
            var valid = Validate(request);
            Write(file.Value.Path, valid);
            ((IConfigurationRoot)configuration).Reload();
            return Read(agents, queue, claude, openCode);
        });
    }

    private static SettingsView Read(
        IOptionsMonitor<AgentOptions> agents, IOptionsMonitor<QueueOptions> queue,
        IOptionsMonitor<ClaudeCodeOptions> claude, IOptionsMonitor<OpenCodeOptions> openCode) =>
        new(
            agents.CurrentValue.Autonomy,
            queue.CurrentValue.Seats,
            claude.CurrentValue.Supervise,
            ClaudeCodeOptions.ModelOrDefault(claude.CurrentValue.SupervisorModel),
            claude.CurrentValue.Review,
            ClaudeCodeOptions.ModelOrDefault(claude.CurrentValue.ReviewModel),
            string.IsNullOrWhiteSpace(openCode.CurrentValue.DraftModel) ? null : openCode.CurrentValue.DraftModel);

    private static SettingsView Validate(SettingsView request)
    {
        if (!Enum.IsDefined(request.Autonomy))
        {
            throw new DomainException("Unknown autonomy: choose Auto or Ask.");
        }

        if (request.Seats is < 1 or > MaxSeats)
        {
            throw new DomainException($"Seats must be between 1 and {MaxSeats}.");
        }

        // The profile rules of desks check model names the same way here.
        return request with
        {
            SupervisorModel = AgentProfile.Create("settings", request.SupervisorModel, AgentEngine.ClaudeCode).Model,
            ReviewModel = AgentProfile.Create("settings", request.ReviewModel, AgentEngine.ClaudeCode).Model,
            DraftModel = AgentProfile.Create("settings", request.DraftModel).Model,
        };
    }

    /// <summary>Keeps whatever else the file holds; blank models are written empty, which means the default.</summary>
    private static void Write(string path, SettingsView settings)
    {
        var root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        JsonObject Section(string name) => root[name] as JsonObject ?? (JsonObject)(root[name] = new JsonObject());

        Section(AgentOptions.SectionName)["Autonomy"] = settings.Autonomy.ToString();
        Section(QueueOptions.SectionName)["Seats"] = settings.Seats;
        var claude = Section(ClaudeCodeOptions.SectionName);
        claude["Supervise"] = settings.ClaudeSupervises;
        claude["SupervisorModel"] = settings.SupervisorModel ?? string.Empty;
        claude["Review"] = settings.ClaudeReviews;
        claude["ReviewModel"] = settings.ReviewModel ?? string.Empty;
        Section(OpenCodeOptions.SectionName)["DraftModel"] = settings.DraftModel ?? string.Empty;

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class SettingsFileOptions
{
    public const string SectionName = "Settings";

    /// <summary>Where changes made in Openspace are kept, next to the database by default.</summary>
    public string Path { get; set; } = "openspace.settings.json";
}
