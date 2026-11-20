using Microsoft.Extensions.Options;
using Openspace.Application.Functions;
using Openspace.Application.LocalModels;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Infrastructure.ClaudeCode;
using Openspace.Infrastructure.OpenCode;

namespace Openspace.Api.Settings;

/// <summary>
/// A function that runs on Claude Code or on OpenCode, with a model for each: the one of the
/// engine not chosen is kept, and the OpenCode one also stands in when Claude Code is chosen but
/// is not on this machine.
/// </summary>
/// <param name="Engine">ClaudeCode or OpenCode; null, for the reviewer only, means no review.</param>
public sealed record EngineChoiceView(ModelEngine? Engine, string? ClaudeModel, string? OpenCodeModel);

/// <param name="Model">A model of the engine; null for its default.</param>
public sealed record FunctionChoiceView(ModelEngine Engine, string? Model);

public sealed record LocalModelChoice(string Id, string Name);

/// <param name="Configured">Whether a command starts the decider; otherwise routines have none.</param>
public sealed record DeciderSummary(bool Configured, string Command, string Url);

public sealed record ModelsRequest(EngineChoiceView Shiori, EngineChoiceView Reviewer, FunctionChoiceView Digests, FunctionChoiceView Journals);

/// <param name="LocalModels">The models of Settings › Local AI, as functions name them.</param>
/// <param name="Decider">Shown only: it is set in Settings › Local AI.</param>
public sealed record ModelsView(
    EngineChoiceView Shiori,
    EngineChoiceView Reviewer,
    FunctionChoiceView Digests,
    FunctionChoiceView Journals,
    IReadOnlyList<LocalModelChoice> LocalModels,
    DeciderSummary Decider);

/// <summary>
/// Settings › Models: the engine and model of each function, written to the settings file and
/// applied at once. Shiori and the reviewer are written to the keys today's planner and reviewer
/// read (ClaudeCode:Supervise, OpenCode:DraftModel…), so nothing that runs now had to change.
/// </summary>
internal static class ModelsEndpoints
{
    public static void MapModelsEndpoints(this IEndpointRouteBuilder app)
    {
        var models = app.MapGroup("/api/models").WithTags("Settings");
        models.MapGet("/", Read);
        models.MapPut("/", (ModelsRequest request, IConfiguration configuration, IOptions<SettingsFileOptions> file,
            IOptionsMonitor<ClaudeCodeOptions> claude, IOptionsMonitor<OpenCodeOptions> openCode,
            IOptionsMonitor<FunctionModelOptions> functions, IOptionsMonitor<LocalModelOptions> local) =>
        {
            var valid = ModelChoices.Validate(request, local.CurrentValue.Provider);
            Write(file.Value.Path, valid);
            ((IConfigurationRoot)configuration).Reload();
            return Read(claude, openCode, functions, local);
        });
    }

    private static ModelsView Read(
        IOptionsMonitor<ClaudeCodeOptions> claude, IOptionsMonitor<OpenCodeOptions> openCode,
        IOptionsMonitor<FunctionModelOptions> functions, IOptionsMonitor<LocalModelOptions> local)
    {
        var f = functions.CurrentValue;
        var l = local.CurrentValue;
        return new ModelsView(
            ModelChoices.Shiori(claude.CurrentValue, openCode.CurrentValue),
            ModelChoices.Reviewer(claude.CurrentValue, openCode.CurrentValue),
            new FunctionChoiceView(f.Digests.Engine, Blank(f.Digests.Model)),
            new FunctionChoiceView(f.Journals.Engine, Blank(f.Journals.Model)),
            l.Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new LocalModelChoice($"{l.Provider}/{m.Id}", string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name))
                .ToList(),
            new DeciderSummary(!string.IsNullOrWhiteSpace(l.Decider.Command), l.Decider.Command, l.Decider.Url.ToString()));
    }

    private static string? Blank(string? model) => string.IsNullOrWhiteSpace(model) ? null : model.Trim();

    /// <summary>Keeps whatever else the file holds; a blank model is written empty, which means the default.</summary>
    private static void Write(string path, ModelsRequest models) => SettingsEndpoints.Patch(path, section =>
    {
        var claude = section(ClaudeCodeOptions.SectionName);
        var openCode = section(OpenCodeOptions.SectionName);

        claude["Supervise"] = models.Shiori.Engine == ModelEngine.ClaudeCode;
        claude["SupervisorModel"] = models.Shiori.ClaudeModel ?? string.Empty;
        openCode["DraftModel"] = models.Shiori.OpenCodeModel ?? string.Empty;

        claude["Review"] = models.Reviewer.Engine == ModelEngine.ClaudeCode;
        claude["ReviewModel"] = models.Reviewer.ClaudeModel ?? string.Empty;
        openCode["ReviewModel"] = models.Reviewer.OpenCodeModel ?? string.Empty;

        var functions = section(FunctionModelOptions.SectionName);
        functions["Digests"] = new System.Text.Json.Nodes.JsonObject { ["Engine"] = models.Digests.Engine.ToString(), ["Model"] = models.Digests.Model ?? string.Empty };
        functions["Journals"] = new System.Text.Json.Nodes.JsonObject { ["Engine"] = models.Journals.Engine.ToString(), ["Model"] = models.Journals.Model ?? string.Empty };
    });
}

/// <summary>How the functions' choices map to the settings that run today, and how they are checked.</summary>
internal static class ModelChoices
{
    /// <summary>Shiori as today's planner sees it: Claude Code when it supervises, OpenCode otherwise.</summary>
    public static EngineChoiceView Shiori(ClaudeCodeOptions claude, OpenCodeOptions openCode) =>
        new(claude.Supervise ? ModelEngine.ClaudeCode : ModelEngine.OpenCode,
            ClaudeCodeOptions.ModelOrDefault(claude.SupervisorModel),
            ClaudeCodeOptions.ModelOrDefault(openCode.DraftModel));

    /// <summary>
    /// The reviewer as today's reviewer sees it: Claude Code when it reviews, else OpenCode when
    /// it has a review model, else no review.
    /// </summary>
    public static EngineChoiceView Reviewer(ClaudeCodeOptions claude, OpenCodeOptions openCode)
    {
        var openCodeModel = ClaudeCodeOptions.ModelOrDefault(openCode.ReviewModel);
        ModelEngine? engine = claude.Review ? ModelEngine.ClaudeCode : openCodeModel is null ? null : ModelEngine.OpenCode;
        return new EngineChoiceView(engine, ClaudeCodeOptions.ModelOrDefault(claude.ReviewModel), openCodeModel);
    }

    /// <summary>Shiori for code that comes later, read from the same keys as the planner.</summary>
    public static void FillShiori(FunctionModelOptions options, IConfiguration configuration)
    {
        var claude = configuration.GetSection(ClaudeCodeOptions.SectionName).Get<ClaudeCodeOptions>() ?? new ClaudeCodeOptions();
        var openCode = configuration.GetSection(OpenCodeOptions.SectionName).Get<OpenCodeOptions>() ?? new OpenCodeOptions();
        var shiori = Shiori(claude, openCode);
        options.Shiori = new FunctionModel
        {
            Engine = shiori.Engine ?? ModelEngine.OpenCode,
            Model = shiori.Engine == ModelEngine.ClaudeCode ? shiori.ClaudeModel : shiori.OpenCodeModel,
            OpenCodeModel = shiori.OpenCodeModel,
        };
    }

    /// <summary>Trimmed and checked: model names the way desks check them, engines each function can use.</summary>
    public static ModelsRequest Validate(ModelsRequest request, string localProvider)
    {
        if (request.Shiori is null || request.Reviewer is null || request.Digests is null || request.Journals is null)
        {
            throw new DomainException("Every function needs its choice: Shiori, reviewer, digests, journals.");
        }

        return new ModelsRequest(
            TwoEngines(request.Shiori, "Shiori", noneAllowed: false),
            TwoEngines(request.Reviewer, "The reviewer", noneAllowed: true),
            Function(request.Digests, "Digests", localProvider),
            Function(request.Journals, "Journals", localProvider));
    }

    private static EngineChoiceView TwoEngines(EngineChoiceView choice, string name, bool noneAllowed)
    {
        if (choice.Engine is ModelEngine.Local || (choice.Engine is not null && !Enum.IsDefined(choice.Engine.Value)))
        {
            throw new DomainException($"{name} runs on Claude Code or OpenCode.");
        }

        if (choice.Engine is null && !noneAllowed)
        {
            throw new DomainException($"{name} needs an engine: Claude Code or OpenCode.");
        }

        var valid = new EngineChoiceView(
            choice.Engine,
            AgentProfile.Create("settings", choice.ClaudeModel, AgentEngine.ClaudeCode).Model,
            AgentProfile.Create("settings", choice.OpenCodeModel).Model);

        // Today an OpenCode reviewer exists only by its model: without one there would be no review.
        if (noneAllowed && valid.Engine == ModelEngine.OpenCode && valid.OpenCodeModel is null)
        {
            throw new DomainException($"{name} on OpenCode needs a model; choose No review otherwise.");
        }

        // No review means no OpenCode review either, as it did before.
        return valid.Engine is null ? valid with { OpenCodeModel = null } : valid;
    }

    private static FunctionChoiceView Function(FunctionChoiceView choice, string name, string localProvider)
    {
        var model = string.IsNullOrWhiteSpace(choice.Model) ? null : choice.Model.Trim();
        return choice.Engine switch
        {
            ModelEngine.ClaudeCode => choice with { Model = AgentProfile.Create("settings", model, AgentEngine.ClaudeCode).Model },
            ModelEngine.OpenCode => choice with { Model = AgentProfile.Create("settings", model).Model },
            ModelEngine.Local when model is null || (model.StartsWith(localProvider + "/", StringComparison.Ordinal) && model.Length > localProvider.Length + 1) =>
                choice with { Model = model },
            ModelEngine.Local => throw new DomainException($"{name}: a local model is named {localProvider}/<id>, as in Settings › Local AI."),
            _ => throw new DomainException($"{name}: unknown engine."),
        };
    }
}
