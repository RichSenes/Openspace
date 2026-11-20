using Microsoft.Extensions.Options;
using Openspace.Application.Brain;
using Openspace.Domain.Common;

namespace Openspace.Api.Settings;

/// <param name="Folder">The vault in use now.</param>
/// <param name="Configured">The folder set for the next start; differs from Folder until Openspace restarts.</param>
/// <param name="Kinds">How many notes of each kind; notes without a kind count as "note".</param>
/// <param name="LastChange">When a note last changed; null for an empty vault.</param>
public sealed record BrainSummary(string Folder, string Configured, int Notes, IReadOnlyDictionary<string, int> Kinds, DateTimeOffset? LastChange);

/// <param name="Folder">An absolute folder, or one starting with ~.</param>
public sealed record BrainFolderRequest(string Folder);

/// <summary>Where the vault is and what it holds, for Settings › Brain.</summary>
internal static class BrainSettingsEndpoints
{
    public static void MapBrainSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var brain = app.MapGroup("/api/brain").WithTags("Brain");

        brain.MapGet("/summary", SummaryAsync);

        // The vault is opened once at start: a new folder is used after a restart.
        brain.MapPut("/folder", async (BrainFolderRequest request, IConfiguration configuration, IOptions<SettingsFileOptions> file,
            IOptionsMonitor<BrainOptions> options, IVault vault, CancellationToken ct) =>
        {
            var folder = Valid(request.Folder);
            SettingsEndpoints.Patch(file.Value.Path, section => section(BrainOptions.SectionName)["Folder"] = folder);
            ((IConfigurationRoot)configuration).Reload();
            return await SummaryAsync(vault, options, ct);
        });
    }

    private static async Task<BrainSummary> SummaryAsync(IVault vault, IOptionsMonitor<BrainOptions> options, CancellationToken ct)
    {
        // The folders' READMEs explain the vault; they are not notes of the person or the agents.
        var notes = (await vault.ListAsync(ct))
            .Where(n => !string.Equals(Path.GetFileName(n.Path), "README.md", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var kinds = notes
            .GroupBy(n => string.IsNullOrWhiteSpace(n.Kind) ? "note" : n.Kind!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        DateTimeOffset? last = notes.Count == 0 ? null : notes.Max(n => n.Modified);
        return new BrainSummary(vault.Folder, options.CurrentValue.Folder, notes.Count, kinds, last);
    }

    internal static string Valid(string? folder)
    {
        var trimmed = folder?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !(trimmed.StartsWith('~') || Path.IsPathFullyQualified(trimmed)))
        {
            throw new DomainException("The brain needs an absolute folder, or one starting with ~, such as ~/Documents/Brain.");
        }

        return trimmed.Length > 1 ? Path.TrimEndingDirectorySeparator(trimmed) : trimmed;
    }
}
