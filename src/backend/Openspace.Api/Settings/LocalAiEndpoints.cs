using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Openspace.Application.LocalModels;

namespace Openspace.Api.Settings;

/// <param name="CardMiB">The graphics card's memory; null when no driver tool can read it.</param>
/// <param name="UsedMiB">How much of it is in use now.</param>
/// <param name="HoldersRunning">The programs that come first and are running now.</param>
public sealed record LocalAiView(LocalAiSettingsView Settings, int? CardMiB, int? UsedMiB, IReadOnlyList<string> HoldersRunning);

/// <summary>
/// The local models, the programs that come before them on the graphics card and the small
/// model on the processor, set from Openspace: written to the settings file, applied at once.
/// </summary>
internal static class LocalAiEndpoints
{
    private const string Section = LocalModelOptions.SectionName;

    public static void MapLocalAiEndpoints(this IEndpointRouteBuilder app)
    {
        var local = app.MapGroup("/api/local-ai").WithTags("Settings");

        local.MapGet("/", ReadAsync);

        local.MapPut("/", async (LocalAiSettingsView request, IConfiguration configuration, IOptions<SettingsFileOptions> file,
            IOptionsMonitor<LocalModelOptions> options, IGpuProbe probe, CancellationToken ct) =>
        {
            var valid = LocalAiSettings.Validate(request);
            Write(file.Value.Path, valid, configuration);
            ((IConfigurationRoot)configuration).Reload();
            return await ReadAsync(options, probe, ct);
        });

        local.MapPost("/check", async (LocalModelView model, IGpuProbe probe, CancellationToken ct) =>
        {
            var memory = await probe.MemoryAsync(ct);
            return LocalAiSettings.Check(model, File.Exists, OnPath, memory?.TotalMiB);
        });
    }

    private static async Task<LocalAiView> ReadAsync(IOptionsMonitor<LocalModelOptions> options, IGpuProbe probe, CancellationToken ct)
    {
        var current = options.CurrentValue;
        var memory = await probe.MemoryAsync(ct);
        return new LocalAiView(LocalAiSettings.Of(current), memory?.TotalMiB, memory?.UsedMiB, probe.RunningHolders(current.Holders));
    }

    /// <summary>
    /// Writes the whole section. Lists in configuration are merged item by item with the files
    /// below (appsettings.Local.json): items they have beyond the new list are written blank,
    /// and blank items are dropped when the options are read.
    /// </summary>
    internal static void Write(string path, LocalAiSettingsView settings, IConfiguration below) =>
        SettingsEndpoints.Patch(path, section =>
        {
            var local = section(Section);
            local["Enabled"] = settings.Enabled;
            local["ServerUrl"] = settings.ServerUrl;
            local["IdleUnload"] = TimeSpan.FromMinutes(settings.IdleUnloadMinutes).ToString("c", CultureInfo.InvariantCulture);
            local["YieldGrace"] = TimeSpan.FromMinutes(settings.YieldGraceMinutes).ToString("c", CultureInfo.InvariantCulture);

            var models = new JsonArray();
            var modelsBelow = below.GetSection($"{Section}:Models").GetChildren().ToList();
            for (var i = 0; i < Math.Max(settings.Models.Count, modelsBelow.Count); i++)
            {
                var argumentsBelow = i < modelsBelow.Count ? modelsBelow[i].GetSection("Arguments").GetChildren().Count() : 0;
                if (i < settings.Models.Count)
                {
                    var m = settings.Models[i];
                    models.Add(new JsonObject
                    {
                        ["Id"] = m.Id,
                        ["Name"] = m.Name,
                        ["Command"] = m.Command,
                        ["Arguments"] = Padded(m.Arguments, argumentsBelow),
                        ["MemoryMiB"] = m.MemoryMiB,
                        ["Context"] = m.Context,
                    });
                }
                else
                {
                    models.Add(new JsonObject { ["Id"] = "", ["Name"] = "", ["Command"] = "", ["Arguments"] = Padded([], argumentsBelow) });
                }
            }

            local["Models"] = models;

            var holders = new JsonArray();
            var holdersBelow = below.GetSection($"{Section}:Holders").GetChildren().Count();
            for (var i = 0; i < Math.Max(settings.Holders.Count, holdersBelow); i++)
            {
                var h = i < settings.Holders.Count ? settings.Holders[i] : new GpuHolderView("", "");
                holders.Add(new JsonObject { ["Name"] = h.Name, ["Match"] = h.Match });
            }

            local["Holders"] = holders;
            local["Decider"] = new JsonObject
            {
                ["Command"] = settings.Decider.Command,
                ["Arguments"] = Padded(settings.Decider.Arguments, below.GetSection($"{Section}:Decider:Arguments").GetChildren().Count()),
                ["Url"] = settings.Decider.Url,
            };
        });

    private static JsonArray Padded(IReadOnlyList<string> items, int below)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(item);
        }

        while (array.Count < below)
        {
            array.Add("");
        }

        return array;
    }

    /// <summary>Whether a bare command is found in one of the PATH's folders.</summary>
    private static bool OnPath(string command) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => File.Exists(Path.Combine(folder, command)));
}
