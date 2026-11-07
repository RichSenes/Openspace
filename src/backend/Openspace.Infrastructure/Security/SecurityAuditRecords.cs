using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Infrastructure.Skills;

namespace Openspace.Infrastructure.Security;

public sealed class SecurityAuditOptions
{
    public const string SectionName = "SecurityAudits";

    /// <summary>Where audits keep their records: a folder per project, a <c>run-N</c> folder per run.</summary>
    public string Folder { get; set; } = "~/.local/share/openspace/security-audits";
}

/// <summary>
/// Reads the records the security-audit skill writes: <c>run-metadata.json</c>,
/// <c>findings.json</c> and <c>REPORT.md</c> in each run's folder. Openspace adds only
/// <c>openspace.json</c>, which names the task. A record still being written reads as missing.
/// </summary>
internal sealed class SecurityAuditRecords(IOptionsMonitor<SecurityAuditOptions> options) : ISecurityAuditRecords
{
    private const string RunPrefix = "run-";
    private const string Marker = "openspace.json";
    private readonly Lock _opening = new();

    private string Root => Path.GetFullPath(SkillCatalog.Expand(options.CurrentValue.Folder));

    public string Open(string project, DateTimeOffset now)
    {
        var folder = Project(project);
        lock (_opening)
        {
            Directory.CreateDirectory(folder);
            var next = Runs(folder).Select(r => r.Number).DefaultIfEmpty(0).Max() + 1;
            var run = Path.Combine(folder, RunPrefix + next.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(run);
            Write(Path.Combine(run, Marker), new MarkerFile { StartedAt = now });
            return run;
        }
    }

    public void Link(string folder, Guid taskId, bool handOff = false)
    {
        var marker = Path.Combine(folder, Marker);
        lock (_opening)
        {
            var current = ReadMarker(marker);
            Write(marker, current with { StartedAt = current.StartedAt ?? DateTimeOffset.UtcNow, TaskId = taskId, HandOff = handOff });
        }
    }

    public void MarkHanded(string project, int run, IEnumerable<string> fingerprints, bool all)
    {
        var marker = Path.Combine(Run(project, run), Marker);
        lock (_opening)
        {
            var current = ReadMarker(marker);
            Write(marker, current with
            {
                Handed = [.. (current.Handed ?? []).Concat(fingerprints).Distinct(StringComparer.Ordinal)],
                HandedOff = current.HandedOff || all,
            });
        }
    }

    public IReadOnlyList<SecurityAuditRun> List(string project)
    {
        var folder = Project(project);
        return Directory.Exists(folder) ? [.. Runs(folder).OrderBy(r => r.Number).Select(r => Read(r.Number, r.Path))] : [];
    }

    public string? Report(string project, int run)
    {
        var file = Path.Combine(Run(project, run), "REPORT.md");
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    public IReadOnlyList<SecurityFinding> Findings(string project, int run)
    {
        var folder = Run(project, run);
        var handed = (ReadMarker(Path.Combine(folder, Marker)).Handed ?? []).ToHashSet(StringComparer.Ordinal);
        using var findings = Json(Path.Combine(folder, "findings.json"));
        if (findings?.RootElement is not { ValueKind: JsonValueKind.Array } items)
        {
            return [];
        }

        return [.. items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(Finding).Select(f => f with { Handed = handed.Contains(f.Fingerprint) })];
    }

    internal static SecurityFinding Finding(JsonElement item)
    {
        var verdict = Text(item, "verdict") ?? "unknown";
        var severity = item.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.Object ? Text(s, "overall_severity") : null;
        string? location = null;
        if (item.TryGetProperty("trace", out var trace) && trace.ValueKind == JsonValueKind.Array && trace.GetArrayLength() > 0)
        {
            var first = trace[0];
            location = Text(first, "file") is { } file ? $"{file}:{(first.TryGetProperty("line", out var l) ? l.ToString() : "?")}" : null;
        }

        var remediation = item.TryGetProperty("remediation", out var r) && r.ValueKind == JsonValueKind.Object ? Text(r, "strategy") : null;
        return new SecurityFinding(
            Text(item, "fingerprint") ?? string.Empty,
            verdict,
            Text(item, "title") ?? "(untitled)",
            Text(item, "description") ?? string.Empty,
            verdict == "confirmed" ? severity : null,
            location,
            remediation);
    }

    private static SecurityAuditRun Read(int number, string folder)
    {
        var marker = ReadMarker(Path.Combine(folder, Marker));
        string? profile = null;
        string? sourceRef = null;
        using (var metadata = Json(Path.Combine(folder, "run-metadata.json")))
        {
            if (metadata?.RootElement is { ValueKind: JsonValueKind.Object } m)
            {
                profile = Text(m, "profile");
                sourceRef = m.TryGetProperty("source_ref", out var source)
                    ? source.ValueKind == JsonValueKind.String ? source.GetString() : Text(source, "commit") ?? source.ToString()
                    : null;
            }
        }

        var findings = new List<SecurityFinding>();
        using (var json = Json(Path.Combine(folder, "findings.json")))
        {
            if (json?.RootElement is { ValueKind: JsonValueKind.Array } items)
            {
                findings.AddRange(items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(Finding));
            }
        }

        var severities = findings
            .Where(f => f.Severity is not null)
            .GroupBy(f => f.Severity!)
            .ToDictionary(g => g.Key, g => g.Count());
        return new SecurityAuditRun(
            number,
            marker.StartedAt ?? Directory.GetCreationTimeUtc(folder),
            marker.TaskId,
            profile,
            sourceRef,
            findings.Count(f => f.Verdict == "confirmed"),
            findings.Count(f => f.Verdict == "needs_validation"),
            findings.Count(f => f.Verdict == "rejected"),
            severities,
            File.Exists(Path.Combine(folder, "REPORT.md")),
            marker.HandOff,
            marker.HandedOff);
    }

    private string Project(string project)
    {
        if (project.Length == 0 || project.Contains('/', StringComparison.Ordinal) || project.Contains('\\', StringComparison.Ordinal) || project.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("A project name is a single folder name.", nameof(project));
        }

        return Path.Combine(Root, project);
    }

    private string Run(string project, int run) => Path.Combine(Project(project), RunPrefix + run.ToString(CultureInfo.InvariantCulture));

    private static IEnumerable<(int Number, string Path)> Runs(string folder) =>
        Directory.EnumerateDirectories(folder, RunPrefix + "*")
            .Select(path => (Ok: int.TryParse(System.IO.Path.GetFileName(path)[RunPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n), Number: n, Path: path))
            .Where(r => r.Ok && r.Number > 0)
            .Select(r => (r.Number, r.Path));

    /// <summary>What Openspace keeps in a run's folder: when it started, its task, and what went to the supervisor.</summary>
    private sealed record MarkerFile
    {
        public DateTimeOffset? StartedAt { get; init; }

        public Guid? TaskId { get; init; }

        public bool HandOff { get; init; }

        public bool HandedOff { get; init; }

        public List<string>? Handed { get; init; }
    }

    private static MarkerFile ReadMarker(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<MarkerFile>(File.ReadAllText(file), JsonSerializerOptions.Web) ?? new MarkerFile() : new MarkerFile();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new MarkerFile();
        }
    }

    private static void Write(string file, MarkerFile marker) =>
        File.WriteAllText(file, JsonSerializer.Serialize(marker, JsonSerializerOptions.Web));

    /// <summary>A JSON record, or null when it is missing or half written.</summary>
    private static JsonDocument? Json(string file)
    {
        try
        {
            return File.Exists(file) ? JsonDocument.Parse(File.ReadAllText(file)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
