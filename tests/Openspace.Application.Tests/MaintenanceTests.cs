using Microsoft.Extensions.Logging.Abstractions;
using Openspace.Application.Abstractions;
using Openspace.Application.Routines;
using Openspace.Application.Routines.Kinds;
using Openspace.Domain.Routines;
using Openspace.Domain.Work;

namespace Openspace.Application.Tests;

/// <summary>Maintenance on made-up projects, advisories and versions: no real registry, database or computer is asked.</summary>
public sealed class MaintenanceTests : IDisposable
{
    private readonly Harness _h = new(withRepository: false);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openspace-upkeep-{Guid.NewGuid():N}");
    private readonly FakeReader _reader = new();
    private readonly FakeAdvisories _advisories = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeToolchains _tools = new();
    private readonly FakeSystem _system = new();
    private readonly FakeStates _states = new();
    private readonly FakeBackups _backups = new();

    public MaintenanceTests()
    {
        Directory.CreateDirectory(Shop);
        Directory.CreateDirectory(Notes);
        _h.Office.SetRepository(Shop, Harness.Start);
        _h.Office.SetPrivatePaths([Path.Combine(Shop, "secret-designs")], Harness.Start);

        _reader.Projects[Shop] = [
            new Dependency(Ecosystem.Npm, "left-pad", "1.0.0", true, "web/package-lock.json"),
            new Dependency(Ecosystem.Npm, "tiny-router", "2.1.0", true, "web/package-lock.json"),
            new Dependency(Ecosystem.Npm, "big-framework", "3.4.0", true, "web/package-lock.json"),
            new Dependency(Ecosystem.NuGet, "Example.Json", "4.0.0", false, "src/Shop.csproj"),
        ];
        _reader.Projects[Notes] = [new Dependency(Ecosystem.PyPI, "markdownish", "0.9.0", true, "requirements.txt")];
        _advisories.Known["Npm:LEFT-PAD@1.0.0"] = [Advisory("GHSA-aaaa", Severity.High, "1.0.2", "CVE-2015-0001", "Padding overflows the buffer")];
        _advisories.Known["NuGet:EXAMPLE.JSON@4.0.0"] = [Advisory("GHSA-bbbb", Severity.Critical, null, null, "Deserialization runs code")];
        _registry.Latest[(Ecosystem.Npm, "left-pad")] = "1.3.0";
        _registry.Latest[(Ecosystem.Npm, "tiny-router")] = "2.4.1";
        _registry.Latest[(Ecosystem.Npm, "big-framework")] = "4.0.0";
        _registry.Latest[(Ecosystem.PyPI, "markdownish")] = "0.9.0";
        _tools.Found.Add(new Toolchain(".NET SDK", "10.0.402", "10.0.403", true, new DateOnly(2028, 11, 14)));
        _tools.Found.Add(new Toolchain("Node", "22.23.4", null, false, new DateOnly(2015, 6, 1)));
        _system.Waiting = new SystemUpdates(104, 3, ["snap firefox"], true);
        _states.States[Shop] = new RepositoryState("main", 5, 3, true);
        _states.States[Notes] = new RepositoryState("main", 0, 0, false);
        _backups.Copies.Add(new BackupView("openspace-1.db", "/backups/openspace-1.db", 10, Harness.Start.AddHours(-2)));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Shop => Path.Combine(_root, "shop");

    private string Notes => Path.Combine(_root, "notes");

    private string Reports => Path.Combine(_root, "reports");

    private Maintenance Kind() => new(_reader, _advisories, _registry, _tools, _system, _states, _backups, _h.Offices, _h.Tasks, new FakePrinter());

    private Routine Create(bool askFirst = false, string extra = "")
    {
        var settings = $$"""
            {"title":"Upkeep","language":"English","channel":"STEWARD","folder":{{System.Text.Json.JsonSerializer.Serialize(Reports)}},
             "reportOn":"Thursday","folders":[{{System.Text.Json.JsonSerializer.Serialize(Notes)}}]{{extra}}}
            """;
        var routine = Routine.Create(_h.Office, "Upkeep", Maintenance.KindKey, RoutineSchedule.Daily(new TimeOnly(7, 0)), settings, _h.Clock.GetUtcNow(), TimeZoneInfo.Utc);
        routine.AskBeforeActing(askFirst, _h.Clock.GetUtcNow());
        _h.Routines.Add(routine);
        return routine;
    }

    private RoutineRunner Runner() =>
        new(_h.Routines, _h.Offices, _h.Progress, [Kind()], _h.RoutineRequests, _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<RoutineRunner>.Instance);

    private async Task<RoutineRun> RunByHandAsync(Routine routine)
    {
        _h.RoutineRequests.Ask(routine.Id);
        await Runner().RunWaitingAsync(default);
        return _h.Routines.Runs.Where(r => r.RoutineId == routine.Id).OrderBy(r => r.StartedAt).Last();
    }

    /// <summary>The scheduled run of the next morning at 7.</summary>
    private async Task<RoutineRun> NextMorningAsync(Routine routine)
    {
        _h.Clock.Advance(routine.NextRunAt!.Value - _h.Clock.GetUtcNow());
        await Runner().RunWaitingAsync(default);
        return _h.Routines.Runs.Where(r => r.RoutineId == routine.Id).OrderBy(r => r.StartedAt).Last();
    }

    private ProposalCommands Proposals() => new(_h.Routines, [Kind()], _h.Outbox, _h.UnitOfWork, _h.Clock, NullLogger<ProposalCommands>.Instance);

    private static Advisory Advisory(string id, Severity severity, string? fix, string? cve, string summary) =>
        new(id, summary, severity, cve is null ? [] : [cve], fix, new Uri($"https://osv.example/{id}"));

    [Fact]
    public async Task By_hand_it_sends_the_whole_report_and_a_pdf_and_hands_the_updates_to_the_office()
    {
        var routine = Create();

        var run = await RunByHandAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Equal("2 vulnerable packages (2 new) and 2 updates in 2 projects; 1 SDKs behind; 104 system updates; report sent.", run.Summary);
        Assert.Equal(2, _h.Outbox.Sent.Count);
        Assert.Equal(
            """
            Upkeep · Wednesday 24 June

            Security
            E Corp
            • Example.Json 4.0.0 → no fix yet · CRITICAL · GHSA-bbbb: Deserialization runs code (indirect)
            • left-pad 1.0.0 → 1.0.2 · HIGH · CVE-2015-0001: Padding overflows the buffer
            I proposed a task in the E Corp office to update them.

            Tools
            • .NET SDK 10.0.402 → 10.0.403 · security
            • Node 22.23.4: up to date · OUT OF SUPPORT since 1 Jun 2015

            System
            • 104 system packages to update (3 for security): sudo apt update && sudo apt full-upgrade
            • Apps to update: snap firefox: sudo snap refresh
            • A restart is needed to finish the updates already installed.

            Regular updates
            • E Corp: 2 packages, 1 with breaking changes
            The full list is in the PDF.

            Only on this disk
            • E Corp (main): 3 commits to push, 5 files not committed
            • notes (main): no remote: it exists only here
            • Openspace database: last copy 2 hours ago
            """.ReplaceLineEndings("\n"),
            _h.Outbox.Sent[0].Text);
        Assert.Equal(Path.Combine(Reports, "Upkeep-2015-06-24.pdf"), _h.Outbox.Sent[1].File);
        Assert.True(File.Exists(_h.Outbox.Sent[1].File));

        // The task waits in the office for a desk: the vulnerable package to its fix, the minor update, never the major one.
        var task = Assert.Single(_h.Tasks.Items);
        Assert.Equal(WorkTaskStatus.Proposed, task.Status);
        Assert.Equal("Fix the vulnerable dependencies", task.Title);
        Assert.Contains("- Npm left-pad: 1.0.0 → 1.0.2 (CVE-2015-0001 (High))", task.Description, StringComparison.Ordinal);
        Assert.Contains("- Npm tiny-router: 2.1.0 → 2.4.1", task.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("big-framework", task.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Example.Json", task.Description, StringComparison.Ordinal);
        Assert.Equal(ProposalStatus.Done, Assert.Single(_h.Routines.Proposals).Status);

        // The office's private folders are never looked into.
        Assert.Equal([Path.Combine(Shop, "secret-designs")], _reader.Skipped[Shop]);
    }

    [Fact]
    public async Task On_other_mornings_it_speaks_only_of_new_vulnerabilities()
    {
        var routine = Create();
        await RunByHandAsync(routine);
        _h.Outbox.Sent.Clear();

        // Thursday was the report; Friday morning nothing is new.
        var thursday = await NextMorningAsync(routine);
        Assert.EndsWith("report sent.", thursday.Summary, StringComparison.Ordinal);
        _h.Outbox.Sent.Clear();
        var friday = await NextMorningAsync(routine);
        Assert.EndsWith("nothing new, the report is for another day.", friday.Summary, StringComparison.Ordinal);
        Assert.Empty(_h.Outbox.Sent);

        // Saturday a new one shows up: only that is sent, and a new task goes to the office.
        _advisories.Known["Npm:TINY-ROUTER@2.1.0"] = [Advisory("GHSA-cccc", Severity.Moderate, "2.1.5", "CVE-2015-0002", "Routes leak headers")];
        var saturday = await NextMorningAsync(routine);

        Assert.EndsWith("new vulnerabilities sent.", saturday.Summary, StringComparison.Ordinal);
        Assert.Equal(
            """
            Upkeep · new vulnerabilities · 27 June
            E Corp
            • tiny-router 2.1.0 → 2.1.5 · MODERATE · CVE-2015-0002: Routes leak headers
            I proposed a task in the E Corp office to update them.
            """.ReplaceLineEndings("\n"),
            Assert.Single(_h.Outbox.Sent).Text);
        Assert.Equal(2, _h.Tasks.Items.Count);
    }

    [Fact]
    public async Task Asked_first_it_waits_for_a_yes_and_a_no_is_not_asked_again()
    {
        var routine = Create(askFirst: true);

        await RunByHandAsync(routine);
        var proposal = Assert.Single(_h.Routines.Proposals);
        Assert.Equal(ProposalStatus.Pending, proposal.Status);
        Assert.Equal("E Corp: Fix the vulnerable dependencies", proposal.Title);
        Assert.Empty(_h.Tasks.Items);

        await Proposals().RejectAsync(proposal.Id, default);
        await RunByHandAsync(routine);
        Assert.Single(_h.Routines.Proposals);

        // Something new to fix makes a different set: that one is asked.
        _advisories.Known["Npm:TINY-ROUTER@2.1.0"] = [Advisory("GHSA-cccc", Severity.Moderate, "2.1.5", null, "Routes leak headers")];
        await RunByHandAsync(routine);
        var second = _h.Routines.Proposals.OrderBy(p => p.CreatedAt).Last();
        Assert.Equal(ProposalStatus.Pending, second.Status);

        await Proposals().ApproveAsync(second.Id, default);
        Assert.Equal(ProposalStatus.Done, second.Status);
        Assert.Contains("- Npm tiny-router: 2.1.0 → 2.1.5 (GHSA-cccc (Moderate))", Assert.Single(_h.Tasks.Items).Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offices_left_out_by_name_are_not_looked_at_and_tasks_can_be_turned_off()
    {
        var routine = Create(extra: """, "skip":["e corp"], "tasks":false""");

        var run = await RunByHandAsync(routine);

        Assert.False(_reader.Skipped.ContainsKey(Shop));
        Assert.StartsWith("0 vulnerable packages (0 new) and 0 updates in 1 projects", run.Summary, StringComparison.Ordinal);
        Assert.Contains("No known vulnerabilities in the projects' dependencies.", _h.Outbox.Sent[0].Text, StringComparison.Ordinal);
        Assert.Empty(_h.Routines.Proposals);
    }

    [Fact]
    public async Task When_the_databases_do_not_answer_it_says_so_and_sends_the_rest()
    {
        var routine = Create();
        _advisories.Fails = true;

        var run = await RunByHandAsync(routine);

        Assert.Equal(RoutineRunStatus.Succeeded, run.Status);
        Assert.Contains("The vulnerability databases did not answer: the dependencies were not checked.", _h.Outbox.Sent[0].Text, StringComparison.Ordinal);
        Assert.Contains("• .NET SDK 10.0.402 → 10.0.403 · security", _h.Outbox.Sent[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_italian_and_with_an_old_database_copy()
    {
        _backups.Copies.Clear();
        _backups.Copies.Add(new BackupView("openspace-1.db", "/backups/openspace-1.db", 10, Harness.Start.AddDays(-5)));
        var routine = Create(extra: """, "language":"Italian", "reportOn":"giovedì" """);

        await RunByHandAsync(routine);

        var text = _h.Outbox.Sent[0].Text;
        Assert.StartsWith("Upkeep · Mercoledì 24 giugno\n\nSicurezza\nE Corp\n• Example.Json 4.0.0 → nessuna correzione ancora · CRITICA", text, StringComparison.Ordinal);
        Assert.Contains("• Database di Openspace: ultima copia 5 giorni fa · TROPPO VECCHIA", text, StringComparison.Ordinal);
        Assert.Equal(DayOfWeek.Thursday, MaintenanceSettings.Read(System.Text.Json.JsonDocument.Parse("""{"reportOn":"giovedì"}""").RootElement).ReportOn);
    }

    [Theory]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("2.0.0-rc.1", "2.0.0", -1)]
    [InlineData("v22.1.0", "22.1.0", 0)]
    [InlineData("10.0.403", "10.0.402", 1)]
    [InlineData("4.0.0", "4.0", 0)]
    public void Versions_are_ordered_as_package_managers_order_them(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(VersionOrder.Instance.Compare(left, right)));

    [Fact]
    public void The_brief_names_every_version_and_asks_for_the_tests()
    {
        var brief = Maintenance.Brief(new UpdatePayload("k", Guid.NewGuid(), "Shop",
            [new UpdateItem("Npm", "left-pad", "1.0.0", "1.0.2", "CVE-2015-0001 (High)")], [new UpdateItem("NuGet", "Example.Http", "1.1.0", "1.2.0", string.Empty)]));

        Assert.Contains("- Npm left-pad: 1.0.0 → 1.0.2 (CVE-2015-0001 (High))", brief, StringComparison.Ordinal);
        Assert.Contains("Then these, newer releases without breaking changes:\n- NuGet Example.Http: 1.1.0 → 1.2.0", brief, StringComparison.Ordinal);
        Assert.Contains("run all its tests", brief, StringComparison.Ordinal);
    }

    private sealed class FakeReader : IDependencyReader
    {
        public Dictionary<string, List<Dependency>> Projects { get; } = [];

        public Dictionary<string, IReadOnlyCollection<string>> Skipped { get; } = [];

        public IReadOnlyList<Dependency> Read(string folder, IReadOnlyCollection<string> skip)
        {
            Skipped[folder] = skip;
            return Projects.GetValueOrDefault(folder) ?? [];
        }
    }

    private sealed class FakeAdvisories : IAdvisories
    {
        public Dictionary<string, IReadOnlyList<Advisory>> Known { get; } = [];

        public bool Fails { get; set; }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<Advisory>>> FindAsync(IReadOnlyCollection<Dependency> packages, CancellationToken cancellationToken) =>
            Fails
                ? throw new HttpRequestException("503 Service Unavailable")
                : Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<Advisory>>>(packages.Select(IAdvisories.Key).Where(Known.ContainsKey).ToDictionary(k => k, k => Known[k]));
    }

    private sealed class FakeRegistry : IPackageRegistry
    {
        public Dictionary<(Ecosystem, string), string> Latest { get; } = [];

        public Task<string?> LatestAsync(Ecosystem ecosystem, string name, CancellationToken cancellationToken) =>
            Task.FromResult(Latest.GetValueOrDefault((ecosystem, name)));
    }

    private sealed class FakeToolchains : IToolchains
    {
        public List<Toolchain> Found { get; } = [];

        public Task<IReadOnlyList<Toolchain>> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Toolchain>>(Found);
    }

    private sealed class FakeSystem : ISystemUpdates
    {
        public SystemUpdates? Waiting { get; set; }

        public Task<SystemUpdates?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(Waiting);
    }

    private sealed class FakeStates : IRepositoryStates
    {
        public Dictionary<string, RepositoryState> States { get; } = [];

        public Task<RepositoryState?> ReadAsync(string folder, CancellationToken cancellationToken) => Task.FromResult(States.GetValueOrDefault(folder));
    }

    private sealed class FakeBackups : IDatabaseBackups
    {
        public List<BackupView> Copies { get; } = [];

        public string? Folder => null;

        public Task<IReadOnlyList<BackupView>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BackupView>>(Copies);

        public Task<BackupView> BackUpAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakePrinter : IPdfPrinter
    {
        public Task<byte[]> PrintAsync(string html, CancellationToken cancellationToken) => Task.FromResult("%PDF-1.7"u8.ToArray());
    }
}
