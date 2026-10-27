using System.Text.Json;
using System.Text.Json.Nodes;
using Openspace.Application.LocalModels;

namespace Openspace.Infrastructure.LocalModels;

/// <summary>
/// The OpenCode configuration that names the local models as a provider, so desks can pick
/// them. <c>scripts/openspace</c> hands the file to <c>opencode serve</c>, which merges it
/// with the person's own configuration.
/// </summary>
internal static class OpenCodeLocalProvider
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Render(LocalModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var models = new JsonObject();
        foreach (var model in options.Models.Where(m => !string.IsNullOrWhiteSpace(m.Id)))
        {
            models[model.Id] = new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(model.Name) ? model.Id : model.Name,
                ["limit"] = new JsonObject { ["context"] = model.Context, ["output"] = Math.Min(8192, model.Context / 4) },
            };
        }

        var settings = new JsonObject { ["baseURL"] = new Uri(options.ServerUrl, "/v1").ToString() };
        if (!string.IsNullOrWhiteSpace(options.ApiKeyFile))
        {
            // OpenCode reads the key from the file itself: it is never copied here.
            settings["apiKey"] = $"{{file:{options.ApiKeyFile}}}";
        }

        var config = new JsonObject
        {
            ["$schema"] = "https://opencode.ai/config.json",
            ["provider"] = new JsonObject
            {
                [options.Provider] = new JsonObject
                {
                    ["npm"] = "@ai-sdk/openai-compatible",
                    ["name"] = "This computer",
                    ["options"] = settings,
                    ["models"] = models,
                },
            },
        };
        return config.ToJsonString(Indented) + "\n";
    }

    /// <returns>True when the file changed: OpenCode sees it after a restart.</returns>
    public static async Task<bool> WriteAsync(LocalModelOptions options, CancellationToken cancellationToken)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.OpenCodeConfigPath))
        {
            return false;
        }

        var text = Render(options);
        var path = options.OpenCodeConfigPath;
        if (File.Exists(path) && await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false) == text)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
