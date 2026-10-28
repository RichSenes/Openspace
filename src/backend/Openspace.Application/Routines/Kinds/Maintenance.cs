using System.Globalization;
using System.Text;
using System.Text.Json;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Routines.Kinds;

/// <summary>
/// Keeps the person's projects safe and current. It reads what every project depends on (the
/// repositories of the offices, and any other folder named), asks the public vulnerability
/// databases about each version in use and the registries for the latest, and looks at the
/// SDKs, the system's packages and the work that exists only on this disk. On its report day,
/// or when run by hand, it sends everything, with a PDF of the full list; on the other days it
/// speaks only when a new vulnerability shows up, so it can run every morning. For a project of
/// an office it proposes there a task that updates what is vulnerable and what moved on
/// without breaking changes. It never updates anything itself.
/// </summary>
public sealed class Maintenance(
    IDependencyReader dependencies,
    IAdvisories advisories,
    IPackageRegistry registry,
    IToolchains toolchains,
    ISystemUpdates system,
    IRepositoryStates repositories,
    IDatabaseBackups backups,
    IOfficeRepository offices,
    IWorkTaskRepository tasks,
    IPdfPrinter printer) : IRoutineKind
{
    public const string KindKey = "maintenance";

    /// <summary>The proposal that hands the updates to a project's office as a task.</summary>
    public const string UpdateAction = "update-dependencies";

    public string Key => KindKey;

    public string Name => "Maintenance";

    public string Description =>
        "Checks every project for vulnerable and outdated dependencies, SDKs and system updates, and proposes the updates as tasks to its office.";

    public string DefaultSettings => """
        {
          "title": "Maintenance",
          "channel": "",
          "language": "English",
          "folder": "~/Documents/Maintenance",
          "reportOn": "Thursday",
          "offices": true,
          "skip": [],
          "folders": [],
          "tasks": true
        }
        """;

    /// <summary>Registry questions asked at once: enough to be quick, few enough to be polite.</summary>
    private const int AtOnce = 6;

    /// <summary>Lines of vulnerable packages in a message; the rest are in the PDF.</summary>
    private const int MostLines = 30;

    /// <summary>A database copy older than this is worth a word.</summary>
    private static readonly TimeSpan OldBackup = TimeSpan.FromDays(2);

    /// <summary>A toolchain whose support ends within this is worth a word even when up to date.</summary>
    private const int EndingSoonDays = 120;

    /// <summary>A proposed task waits in its office for a desk, and can be dropped there: it goes on its own.</summary>
    public bool CanUndo(string action) => action == UpdateAction;

    public async Task<string> RunAsync(RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = MaintenanceSettings.Read(context.Settings);
        var words = MaintenanceWords.For(settings.Language);
        var local = TimeZoneInfo.ConvertTime(context.Now, context.Clock.LocalTimeZone);
        var full = context.Run.ByHand || settings.ReportOn is null || settings.ReportOn == local.DayOfWeek;

        var projects = await ProjectsAsync(settings, cancellationToken).ConfigureAwait(false);
        if (projects.Count == 0)
        {
            context.Write("No project to look at: no office has a repository and the settings name no folder.");
        }

        // What each project depends on, from its own files.
        var used = new Dictionary<Project, IReadOnlyList<Dependency>>();
        foreach (var project in projects)
        {
            context.Progress($"Reading the dependencies of {project.Name}");
            try
            {
                used[project] = dependencies.Read(project.Folder, project.Skip);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
            {
                context.Write($"Could not read the dependencies of {project.Name} ({ex.Message}).");
                used[project] = [];
            }
        }

        var all = used.Values.SelectMany(d => d).DistinctBy(IAdvisories.Key).ToList();
        context.Write($"{all.Count} package versions in {projects.Count} projects.");

        IReadOnlyDictionary<string, IReadOnlyList<Advisory>>? known = null;
        if (all.Count > 0)
        {
            context.Progress($"Asking the vulnerability databases about {all.Count} packages");
            try
            {
                known = await advisories.FindAsync(all, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"The vulnerability databases did not answer ({ex.Message}).");
            }
        }

        var latest = await LatestAsync(context, used.Values.SelectMany(d => d).Where(d => d.Direct), cancellationToken).ConfigureAwait(false);

        // Per project: what to fix, what moved on, what is only here.
        var reports = new List<ProjectReport>();
        var fresh = 0;
        foreach (var project in projects)
        {
            var fixes = Fixes(used[project], known);
            foreach (var fix in fixes)
            {
                foreach (var advisory in fix.Advisories)
                {
                    if (await context.MeetAsync($"vuln:{project.Folder}:{fix.Ecosystem}:{fix.Name.ToUpperInvariant()}:{advisory.Id}", string.Empty, $"{fix.Name} {advisory.Name}", cancellationToken).ConfigureAwait(false))
                    {
                        fix.New = true;
                        fresh++;
                    }
                }
            }

            var vulnerable = fixes.Select(f => (f.Ecosystem, f.Name.ToUpperInvariant())).ToHashSet();
            var updates = used[project]
                .Where(d => d.Direct && !vulnerable.Contains((d.Ecosystem, d.Name.ToUpperInvariant())))
                .Select(d => latest.TryGetValue((d.Ecosystem, d.Name.ToUpperInvariant()), out var last) && last is not null && VersionOrder.Instance.Compare(last, d.Version) > 0
                    ? new Update(d.Ecosystem, d.Name, d.Version, last, VersionOrder.IsMajor(d.Version, last))
                    : null)
                .OfType<Update>()
                .DistinctBy(u => (u.Ecosystem, u.Name.ToUpperInvariant(), u.From))
                .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            RepositoryState? state = null;
            try
            {
                state = await repositories.ReadAsync(project.Folder, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                context.Write($"Could not read the git state of {project.Name} ({ex.Message}).");
            }

            reports.Add(new ProjectReport(project, fixes, updates, state));
        }

        context.Progress("Looking at the SDKs and the system");
        var tools = await TryAsync<IReadOnlyList<Toolchain>>(context, "The SDKs could not be checked", async () => await toolchains.CheckAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false) ?? [];
        var waiting = await TryAsync<SystemUpdates>(context, "The system's updates could not be checked", () => system.CheckAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        var copies = await backups.ListAsync(cancellationToken).ConfigureAwait(false);

        // A task for each office whose project needs one, when it is new or newly urgent.
        foreach (var report in reports.Where(r => settings.Tasks && r.Project.OfficeId is not null && (full || r.Fixes.Any(f => f.New))))
        {
            report.Proposed = await ProposeAsync(context, report, words, cancellationToken).ConfigureAwait(false);
        }

        var vulnerableCount = reports.Sum(r => r.Fixes.Count);
        var summary = $"{vulnerableCount} vulnerable packages ({fresh} new) and {reports.Sum(r => r.Updates.Count)} updates in {projects.Count} projects"
            + $"; {tools.Count(t => t.Latest is not null)} SDKs behind"
            + (waiting is null ? string.Empty : $"; {waiting.Packages} system updates");
        if (!full)
        {
            if (fresh == 0)
            {
                return summary + "; nothing new, the report is for another day.";
            }

            await context.DeliverAsync(Alert(settings, words, local, reports), null, cancellationToken).ConfigureAwait(false);
            return summary + "; new vulnerabilities sent.";
        }

        var text = Report(settings, words, local, reports, tools, waiting, copies, context.Now, known is null);
        await context.DeliverAsync(text, null, cancellationToken).ConfigureAwait(false);
        var pdf = await PrintAsync(context, settings, words, local, reports, tools, waiting, cancellationToken).ConfigureAwait(false);
        if (pdf is not null)
        {
            await context.DeliverAsync($"{settings.Title} · {local.ToString("d MMMM yyyy", words.Culture)}", pdf, cancellationToken).ConfigureAwait(false);
        }

        return summary + "; report sent.";
    }

    public async Task<string> CarryOutAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        var payload = JsonSerializer.Deserialize<UpdatePayload>(proposal.Payload ?? "null", JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The proposal does not say what to update.");
        var office = await offices.FindAsync(new OfficeId(payload.OfficeId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The office of this project is gone.");
        var words = MaintenanceWords.For(MaintenanceSettings.Read(context.Settings).Language);
        var task = WorkTask.Propose(office, words.TaskTitle(payload.Fixes.Count, payload.Updates.Count), Brief(payload), Short($"Routine {context.Routine.Name}", 80), context.Now);
        tasks.Add(task);
        await context.MarkAsync(payload.Key, SeenState.Taken, cancellationToken).ConfigureAwait(false);
        return $"Proposed the task \"{task.Title}\" in {office.Name}: choose a desk there to start it.";
    }

    public async Task RejectedAsync(Proposal proposal, RoutineContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(context);
        if (JsonSerializer.Deserialize<UpdatePayload>(proposal.Payload ?? "null", JsonSerializerOptions.Web) is { } payload)
        {
            await context.MarkAsync(payload.Key, SeenState.Dismissed, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The offices' repositories, then the folders the settings name; each once, the ones to skip left out.</summary>
    private async Task<List<Project>> ProjectsAsync(MaintenanceSettings settings, CancellationToken cancellationToken)
    {
        var projects = new List<Project>();
        bool Skipped(string name, string folder) =>
            settings.Skip.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.TrimEndingDirectorySeparator(MorningPaper.Expand(s)), folder, StringComparison.Ordinal));

        if (settings.Offices)
        {
            foreach (var office in (await offices.ListAsync(cancellationToken).ConfigureAwait(false)).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (office.RepositoryPath is { } path && Directory.Exists(path) && !Skipped(office.Name, Path.TrimEndingDirectorySeparator(path)))
                {
                    projects.Add(new Project(office.Name, Path.TrimEndingDirectorySeparator(path), office.Id.Value, office.Name, office.PrivatePaths));
                }
            }
        }

        foreach (var named in settings.Folders)
        {
            var folder = Path.TrimEndingDirectorySeparator(MorningPaper.Expand(named));
            if (Directory.Exists(folder) && !Skipped(Path.GetFileName(folder), folder) && projects.All(p => p.Folder != folder))
            {
                projects.Add(new Project(Path.GetFileName(folder), folder, null, null, []));
            }
        }

        return projects;
    }

    /// <summary>The latest version of each package projects name themselves, asked a few at a time.</summary>
    private async Task<Dictionary<(Ecosystem, string), string?>> LatestAsync(RoutineContext context, IEnumerable<Dependency> direct, CancellationToken cancellationToken)
    {
        var names = direct.Select(d => (d.Ecosystem, d.Name)).DistinctBy(n => (n.Ecosystem, n.Name.ToUpperInvariant())).ToList();
        var latest = new Dictionary<(Ecosystem, string), string?>();
        var failed = 0;
        using var gate = new SemaphoreSlim(AtOnce);
        var asked = 0;
        await Task.WhenAll(names.Select(async n =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                context.Progress($"Asking the registries for the latest versions, {Interlocked.Increment(ref asked)} of {names.Count}");
                var version = await registry.LatestAsync(n.Ecosystem, n.Name, cancellationToken).ConfigureAwait(false);
                lock (latest)
                {
                    latest[(n.Ecosystem, n.Name.ToUpperInvariant())] = version;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
        if (failed > 0)
        {
            context.Write($"The registries did not say the latest version of {failed} packages.");
        }

        return latest;
    }

    /// <summary>
    /// The vulnerable packages, one line each however many versions of it are locked: the version
    /// to reach is the first that fixes all its advisories.
    /// </summary>
    private static List<Fix> Fixes(IReadOnlyList<Dependency> used, IReadOnlyDictionary<string, IReadOnlyList<Advisory>>? known)
    {
        if (known is null)
        {
            return [];
        }

        return [.. used
            .Select(d => (Dependency: d, Advisories: known.GetValueOrDefault(IAdvisories.Key(d)) ?? []))
            .Where(d => d.Advisories.Count > 0)
            .GroupBy(d => (d.Dependency.Ecosystem, Name: d.Dependency.Name.ToUpperInvariant()))
            .Select(g =>
            {
                var found = g.SelectMany(d => d.Advisories).DistinctBy(a => a.Id).OrderByDescending(a => a.Severity).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
                var versions = g.Select(d => d.Dependency.Version).Distinct(StringComparer.Ordinal).Order(VersionOrder.Instance).ToList();
                var target = found.All(a => a.Fixed is not null) ? found.Select(a => a.Fixed!).Max(VersionOrder.Instance) : null;
                var first = g.First().Dependency;
                return new Fix(first.Ecosystem, first.Name, string.Join(", ", versions), target, g.Any(d => d.Dependency.Direct), found);
            })
            .OrderByDescending(f => f.Worst)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Hands the project's updates to its office, once for each set of them.</summary>
    private static async Task<bool> ProposeAsync(RoutineContext context, ProjectReport report, MaintenanceWords words, CancellationToken cancellationToken)
    {
        var fixes = report.Fixes.Where(f => f.To is not null).Select(f => new UpdateItem(f.Ecosystem.ToString(), f.Name, f.From, f.To!, string.Join(", ", f.Advisories.Select(a => $"{a.Name} ({a.Severity})")))).ToList();
        var updates = report.Updates.Where(u => !u.Major).Select(u => new UpdateItem(u.Ecosystem.ToString(), u.Name, u.From, u.To, string.Empty)).ToList();
        if (fixes.Count + updates.Count == 0)
        {
            return false;
        }

        var fingerprint = RoutineContext.Fingerprint([.. fixes.Concat(updates).Select(i => $"{i.Ecosystem}:{i.Name}:{i.To}").Order(StringComparer.Ordinal)]);
        var key = $"update:{report.Project.OfficeId}:{fingerprint}";
        if (!await context.MeetAsync(key, fingerprint, report.Project.Name, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var payload = new UpdatePayload(key, report.Project.OfficeId!.Value, report.Project.Name, fixes, updates);
        var body = string.Join('\n', fixes.Select(f => $"{f.Name} {f.From} → {f.To} ({f.Why})").Concat(updates.Select(u => $"{u.Name} {u.From} → {u.To}")));
        await context.ProposeAsync(UpdateAction, $"{report.Project.Name}: {words.TaskTitle(fixes.Count, updates.Count)}", body, payload, cancellationToken).ConfigureAwait(false);
        await context.MarkAsync(key, SeenState.Shown, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>What the agent is asked: precise versions, the tests, nothing else touched.</summary>
    public static string Brief(UpdatePayload payload)
    {
        var text = new StringBuilder();
        if (payload.Fixes.Count > 0)
        {
            text.Append("Update these packages to at least the version shown: each one fixes known vulnerabilities.\n");
            foreach (var fix in payload.Fixes)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {fix.Ecosystem} {fix.Name}: {fix.From} → {fix.To} ({fix.Why})\n");
            }

            text.Append("A package that is not named in the project's own files comes in through another one: update or override the one that brings it in.\n");
        }

        if (payload.Updates.Count > 0)
        {
            text.Append(payload.Fixes.Count > 0 ? "\nThen these, newer releases without breaking changes:\n" : "Update these packages, newer releases without breaking changes:\n");
            foreach (var update in payload.Updates)
            {
                text.Append(CultureInfo.InvariantCulture, $"- {update.Ecosystem} {update.Name}: {update.From} → {update.To}\n");
            }
        }

        text.Append("\nUpdate the lock files with the package managers' own commands, never by hand. Build the project and run all its tests. ")
            .Append("If an update breaks something, fix it; if it cannot be fixed safely, leave that package where it was and say why. ")
            .Append("Do not update anything else and do not change the project's behaviour.");
        return text.ToString();
    }

    /// <summary>The daily word when a new vulnerability shows up: only the new ones.</summary>
    private static string Alert(MaintenanceSettings settings, MaintenanceWords words, DateTimeOffset local, List<ProjectReport> reports)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{settings.Title} · {words.NewOnes} · {local.ToString("d MMMM", words.Culture)}");
        foreach (var report in reports.Where(r => r.Fixes.Any(f => f.New)))
        {
            AppendFixes(text, words, report, report.Fixes.Where(f => f.New).ToList());
        }

        return text.ToString();
    }

    private static string Report(
        MaintenanceSettings settings, MaintenanceWords words, DateTimeOffset local, List<ProjectReport> reports, IReadOnlyList<Toolchain> tools,
        SystemUpdates? waiting, IReadOnlyList<BackupView> copies, DateTimeOffset now, bool notChecked)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"{settings.Title} · {Capital(local.ToString("dddd d MMMM", words.Culture))}");

        text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Security}");
        if (notChecked)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{words.NotChecked}");
        }
        else if (reports.All(r => r.Fixes.Count == 0))
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{words.NoneKnown}");
        }
        else
        {
            foreach (var report in reports.Where(r => r.Fixes.Count > 0))
            {
                AppendFixes(text, words, report, report.Fixes);
            }
        }

        if (tools.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Tools}");
            foreach (var tool in tools)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {tool.Name} {tool.Installed}");
                text.Append(tool.Latest is null ? $": {words.Current}" : $" → {tool.Latest}{(tool.Security ? $" · {words.SecurityFix}" : string.Empty)}");
                if (tool.EndOfLife is { } end && end.DayNumber - DateOnly.FromDateTime(local.DateTime).DayNumber < EndingSoonDays)
                {
                    text.Append(CultureInfo.InvariantCulture, $" · {words.SupportEnds(end.ToString("d MMM yyyy", words.Culture), end < DateOnly.FromDateTime(local.DateTime))}");
                }
            }
        }

        if (waiting is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.System}");
            text.Append(waiting.Packages == 0 ? $"\n• {words.SystemCurrent}" : $"\n• {words.SystemPackages(waiting.Packages, waiting.Security)}: sudo apt update && sudo apt full-upgrade");
            if (waiting.Apps.Count > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {words.Apps}: {string.Join(", ", waiting.Apps)}");
                var commands = new List<string>();
                if (waiting.Apps.Any(a => a.StartsWith("snap ", StringComparison.Ordinal)))
                {
                    commands.Add("sudo snap refresh");
                }

                if (waiting.Apps.Any(a => a.StartsWith("flatpak ", StringComparison.Ordinal)))
                {
                    commands.Add("flatpak update");
                }

                text.Append(CultureInfo.InvariantCulture, $": {string.Join(" && ", commands)}");
            }

            if (waiting.Restart)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {words.Restart}");
            }
        }

        var behind = reports.Where(r => r.Updates.Count > 0).ToList();
        if (behind.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n\n{words.Updates}");
            foreach (var report in behind)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n• {report.Project.Name}: {words.UpdateCount(report.Updates.Count, report.Updates.Count(u => u.Major))}");
            }

            text.Append(CultureInfo.InvariantCulture, $"\n{words.InThePdf}");
        }

        var onlyHere = reports.Where(r => r.State is { } s && (s.Changed > 0 || s.Unpushed > 0 || !s.HasRemote)).ToList();
        var copy = copies.Count > 0 ? copies.MaxBy(c => c.CreatedAt) : null;
        text.Append(CultureInfo.InvariantCulture, $"\n\n{words.OnlyHere}");
        foreach (var report in onlyHere)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n• {report.Project.Name} ({report.State!.Branch}): {words.Work(report.State)}");
        }

        text.Append(CultureInfo.InvariantCulture, $"\n• {words.Database(copy is null ? null : now - copy.CreatedAt, copy is not null && now - copy.CreatedAt > OldBackup)}");
        return text.ToString();
    }

    private static void AppendFixes(StringBuilder text, MaintenanceWords words, ProjectReport report, List<Fix> fixes)
    {
        text.Append(CultureInfo.InvariantCulture, $"\n{report.Project.Name}");
        foreach (var fix in fixes.Take(MostLines))
        {
            var worst = fix.Advisories[0];
            text.Append(CultureInfo.InvariantCulture, $"\n• {fix.Name} {fix.From} → {fix.To ?? words.NoFix} · {words.Rate(worst.Severity)} · {worst.Name}: {Short(worst.Summary, 90)}");
            if (fix.Advisories.Count > 1)
            {
                text.Append(CultureInfo.InvariantCulture, $" {words.Others(fix.Advisories.Count - 1)}");
            }

            if (!fix.Direct)
            {
                text.Append(CultureInfo.InvariantCulture, $" {words.Indirect}");
            }
        }

        if (fixes.Count > MostLines)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n• {words.More(fixes.Count - MostLines)}");
        }

        if (report.Proposed)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n{words.TaskProposed(report.Project.OfficeName!)}");
        }
    }

    private async Task<string?> PrintAsync(
        RoutineContext context, MaintenanceSettings settings, MaintenanceWords words, DateTimeOffset local, List<ProjectReport> reports, IReadOnlyList<Toolchain> tools,
        SystemUpdates? waiting, CancellationToken cancellationToken)
    {
        try
        {
            var folder = MorningPaper.Expand(settings.Folder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{settings.Title}-{local:yyyy-MM-dd}.pdf");
            var html = MaintenancePage.Render(settings.Title, Capital(local.ToString("dddd d MMMM yyyy", words.Culture)), words, reports, tools, waiting);
            await File.WriteAllBytesAsync(path, await printer.PrintAsync(html, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"The PDF could not be made ({ex.Message}): the message has the essentials.");
            return null;
        }
    }

    private static async Task<T?> TryAsync<T>(RoutineContext context, string failure, Func<Task<T?>> ask, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await ask().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            context.Write($"{failure} ({ex.Message}).");
            return null;
        }
    }

    private static string Capital(string text) => text.Length == 0 ? text : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text.AsSpan(1));

    private static string Short(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");
}

/// <param name="OfficeId">The office that works on it; null for a folder named in the settings.</param>
/// <param name="Skip">Folders inside it never to look into.</param>
internal sealed record Project(string Name, string Folder, Guid? OfficeId, string? OfficeName, IReadOnlyCollection<string> Skip);

/// <param name="From">The versions in use, "1.2.0, 1.3.1" when several are locked.</param>
/// <param name="To">The first version that fixes them all; null when one has no fix yet.</param>
/// <param name="Advisories">The worst first.</param>
internal sealed record Fix(Ecosystem Ecosystem, string Name, string From, string? To, bool Direct, IReadOnlyList<Advisory> Advisories)
{
    public Severity Worst => Advisories[0].Severity;

    /// <summary>Met for the first time in this run.</summary>
    public bool New { get; set; }
}

/// <param name="Major">The first number changes: it may break things, so it is left to the person.</param>
internal sealed record Update(Ecosystem Ecosystem, string Name, string From, string To, bool Major);

internal sealed record ProjectReport(Project Project, List<Fix> Fixes, List<Update> Updates, RepositoryState? State)
{
    /// <summary>A task was proposed to its office in this run.</summary>
    public bool Proposed { get; set; }
}

public sealed record UpdateItem(string Ecosystem, string Name, string From, string To, string Why);

/// <summary>What a proposal hands to an office: the vulnerable packages to their fix, the safe updates.</summary>
public sealed record UpdatePayload(string Key, Guid OfficeId, string Project, IReadOnlyList<UpdateItem> Fixes, IReadOnlyList<UpdateItem> Updates);

/// <summary>A maintenance routine's settings, read from its JSON.</summary>
/// <param name="ReportOn">The day of the full report; null to send it at every run.</param>
/// <param name="Offices">Whether the repositories of the offices are looked at.</param>
/// <param name="Skip">Offices (by name) or folders left out.</param>
/// <param name="Folders">Other projects, by folder.</param>
/// <param name="Tasks">Whether updates are proposed as tasks to the offices.</param>
public sealed record MaintenanceSettings(
    string Title, string Language, string Folder, DayOfWeek? ReportOn, bool Offices, IReadOnlyList<string> Skip, IReadOnlyList<string> Folders, bool Tasks)
{
    public static MaintenanceSettings Read(JsonElement settings)
    {
        string Text(string name, string fallback) =>
            settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : fallback;

        bool Flag(string name) => !settings.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.False;

        List<string> Names(string name) => settings.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0)]
            : [];

        var day = Text("reportOn", "Thursday");
        return new MaintenanceSettings(
            Text("title", "Maintenance"), Text("language", "English"), Text("folder", "~/Documents/Maintenance"),
            Enum.TryParse<DayOfWeek>(day, true, out var parsed) ? parsed : MaintenanceWords.ItalianDay(day),
            Flag("offices"), Names("skip"), Names("folders"), Flag("tasks"));
    }
}

/// <summary>The maintenance report's fixed words, in the person's language.</summary>
internal sealed record MaintenanceWords(
    string Security, string NotChecked, string NoneKnown, string NoFix, Func<int, string> Others, string Indirect, Func<int, string> More,
    Func<string, string> TaskProposed, string Tools, string Current, string SecurityFix, Func<string, bool, string> SupportEnds,
    string System, string SystemCurrent, Func<int, int, string> SystemPackages, string Apps, string Restart,
    string Updates, Func<int, int, string> UpdateCount, string InThePdf, string OnlyHere, Func<RepositoryState, string> Work,
    Func<TimeSpan?, bool, string> Database, string NewOnes, Func<int, int, string> TaskTitle, Func<Severity, string> Rate,
    string Package, string InUse, string Fixed, string Latest, string Major, CultureInfo Culture)
{
    public static MaintenanceWords For(string language) => language.Trim().ToUpperInvariant() is "ITALIAN" or "ITALIANO" ? Italian : English;

    public static DayOfWeek? ItalianDay(string day) => day.Trim().ToUpperInvariant() switch
    {
        "LUNEDÌ" or "LUNEDI" => DayOfWeek.Monday,
        "MARTEDÌ" or "MARTEDI" => DayOfWeek.Tuesday,
        "MERCOLEDÌ" or "MERCOLEDI" => DayOfWeek.Wednesday,
        "GIOVEDÌ" or "GIOVEDI" => DayOfWeek.Thursday,
        "VENERDÌ" or "VENERDI" => DayOfWeek.Friday,
        "SABATO" => DayOfWeek.Saturday,
        "DOMENICA" => DayOfWeek.Sunday,
        _ => null,
    };

    private static string Ago(TimeSpan age, bool italian) => age.TotalHours < 1
        ? (italian ? "meno di un'ora fa" : "less than an hour ago")
        : age.TotalDays < 1
            ? (italian ? $"{(int)age.TotalHours} ore fa" : $"{(int)age.TotalHours} hours ago")
            : (italian ? $"{(int)age.TotalDays} giorni fa" : $"{(int)age.TotalDays} days ago");

    private static readonly MaintenanceWords Italian = new(
        "Sicurezza",
        "Le banche dati delle vulnerabilità non hanno risposto: le dipendenze non sono state controllate.",
        "Nessuna vulnerabilità nota nelle dipendenze dei progetti.",
        "nessuna correzione ancora",
        n => n == 1 ? "(+1 altra)" : $"(+{n} altre)",
        "(indiretta)",
        n => $"altre {n} nel PDF",
        office => $"Ho proposto un compito nell'ufficio {office} per aggiornarle.",
        "Strumenti",
        "aggiornato",
        "sicurezza",
        (date, past) => past ? $"FUORI SUPPORTO dal {date}" : $"supporto fino al {date}",
        "Sistema",
        "Nessun pacchetto di sistema da aggiornare.",
        (n, security) => $"{n} pacchetti di sistema da aggiornare ({security} di sicurezza)",
        "App da aggiornare",
        "Serve un riavvio per completare gli aggiornamenti già installati.",
        "Aggiornamenti normali",
        (n, major) => (n == 1 ? "1 pacchetto" : $"{n} pacchetti") + (major == 0 ? string.Empty : $", {major} con cambi grossi"),
        "L'elenco completo è nel PDF.",
        "Solo su questo disco",
        s => string.Join(", ", new[]
        {
            s.HasRemote ? null : "nessun remoto: esiste solo qui",
            s.Unpushed == 0 ? null : s.Unpushed == 1 ? "1 commit da spingere" : $"{s.Unpushed} commit da spingere",
            s.Changed == 0 ? null : s.Changed == 1 ? "1 file non salvato in un commit" : $"{s.Changed} file non salvati in un commit",
        }.OfType<string>()),
        (age, old) => age is null ? "Database di Openspace: nessuna copia" : $"Database di Openspace: ultima copia {Ago(age.Value, true)}{(old ? " · TROPPO VECCHIA" : string.Empty)}",
        "nuove vulnerabilità",
        (fixes, updates) => fixes > 0 ? "Correggere le dipendenze vulnerabili" : "Aggiornare le dipendenze",
        s => s switch { Severity.Critical => "CRITICA", Severity.High => "ALTA", Severity.Moderate => "MEDIA", Severity.Low => "BASSA", _ => "NON VALUTATA" },
        "Pacchetto", "In uso", "Corretta in", "Ultima", "cambi grossi",
        CultureInfo.GetCultureInfo("it-IT"));

    private static readonly MaintenanceWords English = new(
        "Security",
        "The vulnerability databases did not answer: the dependencies were not checked.",
        "No known vulnerabilities in the projects' dependencies.",
        "no fix yet",
        n => n == 1 ? "(+1 more)" : $"(+{n} more)",
        "(indirect)",
        n => $"{n} more in the PDF",
        office => $"I proposed a task in the {office} office to update them.",
        "Tools",
        "up to date",
        "security",
        (date, past) => past ? $"OUT OF SUPPORT since {date}" : $"supported until {date}",
        "System",
        "No system package to update.",
        (n, security) => $"{n} system packages to update ({security} for security)",
        "Apps to update",
        "A restart is needed to finish the updates already installed.",
        "Regular updates",
        (n, major) => (n == 1 ? "1 package" : $"{n} packages") + (major == 0 ? string.Empty : $", {major} with breaking changes"),
        "The full list is in the PDF.",
        "Only on this disk",
        s => string.Join(", ", new[]
        {
            s.HasRemote ? null : "no remote: it exists only here",
            s.Unpushed == 0 ? null : s.Unpushed == 1 ? "1 commit to push" : $"{s.Unpushed} commits to push",
            s.Changed == 0 ? null : s.Changed == 1 ? "1 file not committed" : $"{s.Changed} files not committed",
        }.OfType<string>()),
        (age, old) => age is null ? "Openspace database: no copy" : $"Openspace database: last copy {Ago(age.Value, false)}{(old ? " · TOO OLD" : string.Empty)}",
        "new vulnerabilities",
        (fixes, updates) => fixes > 0 ? "Fix the vulnerable dependencies" : "Update the dependencies",
        s => s switch { Severity.Critical => "CRITICAL", Severity.High => "HIGH", Severity.Moderate => "MODERATE", Severity.Low => "LOW", _ => "UNRATED" },
        "Package", "In use", "Fixed in", "Latest", "breaking changes",
        CultureInfo.GetCultureInfo("en-GB"));
}
