using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Openspace.Application.Functions;

namespace Openspace.Api.Tests;

/// <summary>Settings › Models: each function's engine and model, and today's planner and reviewer kept in step.</summary>
public sealed class ModelsApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] MarkedFree = [" zen/grok-code ", "", "ZEN/grok-code", "openrouter/qwen"];
    private static readonly string[] OneFree = ["zen/grok-code"];
    private static readonly string[] NotAnId = ["grok code"];

    private readonly OpenspaceApp _app = new();
    private readonly HttpClient _http;

    public ModelsApiTests() => _http = _app.CreateClient();

    public void Dispose()
    {
        _http.Dispose();
        _app.Dispose();
    }

    private sealed record EngineChoice(string? Engine, string? ClaudeModel, string? OpenCodeModel);

    private sealed record FunctionChoice(string Engine, string? Model);

    private sealed record Decider(bool Configured, string Command, string Url);

    private sealed record Models(EngineChoice Shiori, EngineChoice Reviewer, FunctionChoice Digests, FunctionChoice Journals, List<JsonElement> LocalModels, Decider Decider, List<string> FreeModels);

    private sealed record Settings(string Autonomy, int Seats, bool ClaudeSupervises, string? SupervisorModel, bool ClaudeReviews, string? ReviewModel, string? DraftModel, string? OpenCodeReviewModel = null);

    private async Task<T> GetAsync<T>(string url) => (await _http.GetFromJsonAsync<T>(url, Json))!;

    private static object Request(EngineChoice shiori, EngineChoice reviewer, FunctionChoice digests, FunctionChoice journals) =>
        new { shiori, reviewer, digests, journals };

    [Fact]
    public async Task Untouched_settings_give_Shiori_the_latest_Opus_and_each_new_function_its_default()
    {
        var models = await GetAsync<Models>("/api/models");

        Assert.Equal(new EngineChoice("ClaudeCode", "opus", null), models.Shiori);
        Assert.Equal(new EngineChoice("ClaudeCode", null, null), models.Reviewer);
        Assert.Equal(new FunctionChoice("ClaudeCode", "sonnet"), models.Digests);
        Assert.Equal(new FunctionChoice("Local", null), models.Journals);
        Assert.False(models.Decider.Configured);
        Assert.Empty(models.LocalModels);
        Assert.Empty(models.FreeModels);
    }

    [Fact]
    public async Task Free_models_are_saved_trimmed_once_each_kept_when_not_sent_and_checked()
    {
        var choices = (Shiori: new EngineChoice("ClaudeCode", null, null), Reviewer: new EngineChoice("ClaudeCode", null, null),
            Digests: new FunctionChoice("ClaudeCode", "sonnet"), Journals: new FunctionChoice("Local", null));
        (await _http.PutAsJsonAsync("/api/models", new
        {
            choices.Shiori, choices.Reviewer, choices.Digests, choices.Journals,
            freeModels = MarkedFree,
        }, Json)).EnsureSuccessStatusCode();
        var options = _app.Services.GetRequiredService<IOptionsMonitor<FunctionModelOptions>>().CurrentValue;

        Assert.Equal(["zen/grok-code", "openrouter/qwen"], (await GetAsync<Models>("/api/models")).FreeModels);
        Assert.Equal(["zen/grok-code", "openrouter/qwen"], options.FreeModels);

        // A client that knows nothing of free models leaves them as they are.
        (await _http.PutAsJsonAsync("/api/models", Request(choices.Shiori, choices.Reviewer, choices.Digests, choices.Journals), Json)).EnsureSuccessStatusCode();
        Assert.Equal(["zen/grok-code", "openrouter/qwen"], (await GetAsync<Models>("/api/models")).FreeModels);

        (await _http.PutAsJsonAsync("/api/models", new { choices.Shiori, choices.Reviewer, choices.Digests, choices.Journals, freeModels = OneFree }, Json)).EnsureSuccessStatusCode();
        Assert.Equal(["zen/grok-code"], (await GetAsync<Models>("/api/models")).FreeModels);

        using var wrong = await _http.PutAsJsonAsync("/api/models", new { choices.Shiori, choices.Reviewer, choices.Digests, choices.Journals, freeModels = NotAnId }, Json);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);
    }

    [Fact]
    public async Task Choices_are_written_to_the_planner_and_reviewer_settings_and_apply_at_once()
    {
        var put = await _http.PutAsJsonAsync("/api/models", Request(
            new EngineChoice("OpenCode", " sonnet ", " github-copilot/gpt-5 "),
            new EngineChoice("OpenCode", "opus", "github-copilot/claude-sonnet"),
            new FunctionChoice("Local", " local/gemma "),
            new FunctionChoice("OpenCode", null)), Json);
        put.EnsureSuccessStatusCode();
        var models = await GetAsync<Models>("/api/models");
        var settings = await GetAsync<Settings>("/api/settings");
        var options = _app.Services.GetRequiredService<IOptionsMonitor<FunctionModelOptions>>().CurrentValue;

        // The Claude models of an engine not chosen are kept, ready for when it is chosen again.
        Assert.Equal(new EngineChoice("OpenCode", "sonnet", "github-copilot/gpt-5"), models.Shiori);
        Assert.Equal(new EngineChoice("OpenCode", "opus", "github-copilot/claude-sonnet"), models.Reviewer);
        Assert.Equal(new FunctionChoice("Local", "local/gemma"), models.Digests);
        Assert.Equal(new FunctionChoice("OpenCode", null), models.Journals);
        Assert.Equal((false, "sonnet", "github-copilot/gpt-5"), (settings.ClaudeSupervises, settings.SupervisorModel, settings.DraftModel));
        Assert.Equal((false, "opus", "github-copilot/claude-sonnet"), (settings.ClaudeReviews, settings.ReviewModel, settings.OpenCodeReviewModel));
        Assert.Equal((ModelEngine.OpenCode, "github-copilot/gpt-5"), (options.Shiori.Engine, options.Shiori.Model));
        Assert.Equal((ModelEngine.Local, "local/gemma"), (options.Digests.Engine, options.Digests.Model));
        Assert.Contains("\"Journals\"", await File.ReadAllTextAsync(_app.SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_review_clears_the_OpenCode_reviewer_as_before()
    {
        (await _http.PutAsJsonAsync("/api/models", Request(
            new EngineChoice("ClaudeCode", null, null),
            new EngineChoice(null, "sonnet", "github-copilot/claude-sonnet"),
            new FunctionChoice("ClaudeCode", "sonnet"),
            new FunctionChoice("Local", null)), Json)).EnsureSuccessStatusCode();

        var models = await GetAsync<Models>("/api/models");
        var settings = await GetAsync<Settings>("/api/settings");

        Assert.Equal(new EngineChoice(null, "sonnet", null), models.Reviewer);
        Assert.Equal((false, (string?)null), (settings.ClaudeReviews, settings.OpenCodeReviewModel));
    }

    [Fact]
    public async Task A_saved_blank_Claude_model_stays_Claude_Codes_default()
    {
        var before = await GetAsync<Settings>("/api/settings");
        (await _http.PutAsJsonAsync("/api/settings", before with { SupervisorModel = "" }, Json)).EnsureSuccessStatusCode();

        var models = await GetAsync<Models>("/api/models");

        Assert.Equal("opus", before.SupervisorModel);
        Assert.Equal(new EngineChoice("ClaudeCode", null, null), models.Shiori);
    }

    [Fact]
    public async Task Saving_permissions_and_seats_alone_leaves_the_planner_and_reviewer_as_they_are()
    {
        (await _http.PutAsJsonAsync("/api/models", Request(
            new EngineChoice("OpenCode", "sonnet", "github-copilot/gpt-5"),
            new EngineChoice("OpenCode", null, "github-copilot/claude-sonnet"),
            new FunctionChoice("ClaudeCode", "sonnet"),
            new FunctionChoice("Local", null)), Json)).EnsureSuccessStatusCode();

        (await _http.PutAsJsonAsync("/api/settings", new { autonomy = "Ask", seats = 2 }, Json)).EnsureSuccessStatusCode();
        var settings = await GetAsync<Settings>("/api/settings");

        Assert.Equal(("Ask", 2), (settings.Autonomy, settings.Seats));
        Assert.Equal((false, "github-copilot/gpt-5"), (settings.ClaudeSupervises, settings.DraftModel));
        Assert.Equal("github-copilot/claude-sonnet", settings.OpenCodeReviewModel);
    }

    public static TheoryData<string?, string?, string?, string, string?> Refused => new()
    {
        // Shiori always has an engine; nor does it run on a local model yet.
        { null, null, "sonnet", "ClaudeCode", "sonnet" },
        { "Local", null, "sonnet", "ClaudeCode", "sonnet" },
        // An OpenCode reviewer without a model would be no review at all.
        { "ClaudeCode", "OpenCode", null, "ClaudeCode", "sonnet" },
        // A model name a desk would refuse.
        { "ClaudeCode", "ClaudeCode", "not a model!", "ClaudeCode", "sonnet" },
        // Local models are named as Settings › Local AI names them.
        { "ClaudeCode", "ClaudeCode", null, "Local", "gemma" },
        { "ClaudeCode", "ClaudeCode", null, "OpenCode", "gemma" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Choices_that_cannot_run_are_refused_and_nothing_is_written(
        string? shiori, string? reviewer, string? reviewerOpenCode, string digests, string? digestsModel)
    {
        var put = await _http.PutAsJsonAsync("/api/models", Request(
            new EngineChoice(shiori, null, null),
            new EngineChoice(reviewer, reviewer == "ClaudeCode" ? reviewerOpenCode : null, reviewer == "OpenCode" ? reviewerOpenCode : null),
            new FunctionChoice(digests, digestsModel),
            new FunctionChoice("Local", null)), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, put.StatusCode);
        Assert.False(File.Exists(_app.SettingsPath));
    }
}
