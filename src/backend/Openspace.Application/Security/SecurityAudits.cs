using System.Globalization;
using System.Text;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Roles;
using Openspace.Domain.Work;

namespace Openspace.Application.Security;

/// <summary>How far an audit goes, as the security-audit skill names it.</summary>
public enum SecurityAuditProfile
{
    Quick,
    Standard,
    Deep,
}

/// <summary>A run of an office's audits, with the task that runs it.</summary>
public sealed record SecurityAuditView(SecurityAuditRun Run, WorkTaskStatus? Status, string? DeskName);

/// <summary>
/// Security audits of an office's repository, run by a desk whose role names the
/// <c>security-audit</c> skill. The agent writes its records outside the repository, in a
/// run folder Openspace gives it, and never changes the code: fixes are separate tasks.
/// </summary>
public sealed class SecurityAudits(
    IOfficeRepository offices,
    IRoleRepository roles,
    IWorkTaskRepository tasks,
    ISecurityAuditRecords records,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>The skill an auditor's role names.</summary>
    public const string Skill = "security-audit";

    /// <summary>The role Openspace gives the auditors it sets up.</summary>
    public const string RoleName = "Security auditor";

    public const string RoomName = "Security";

    public const string DeskName = "Auditor";

    /// <summary>
    /// The role's instructions. The skill carries the method; these say what the desk is for
    /// and what it must never do in an office.
    /// </summary>
    public const string RoleInstructions = """
        - You are the office's security auditor. You look for real vulnerabilities and report them with evidence; you never fix them.
        - Always work with the security-audit skill and follow its phases, verdicts and evidence bar.
        - Never change, add or delete files of the repository. Write only in the output folder the task gives you.
        - Hand hunters and verifiers to your helper agents, each with its own folder under the output folder, as the skill describes.
        - Never probe deployed sites, external services or real accounts: source and local checks only.
        - Never read, copy or print secrets, credentials or personal data, even when they are in the repository: name the file and line instead.
        - Before finishing, run the skill's validators on findings.json and coverage-ledger.json and fix what they reject.
        - Finish with a short note: how many findings are confirmed, how many need validation, how many were rejected, and the path of REPORT.md.
        """;

    /// <summary>Starts an audit at the office's auditor desk, or at the one named.</summary>
    public async Task<WorkTaskId> StartAsync(
        OfficeId officeId, DeskId? deskId, SecurityAuditProfile profile, string requestedBy, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        if (office.RepositoryPath is null)
        {
            throw new RejectedException("no-repository", $"Office '{office.Name}' has no repository to audit.");
        }

        var desk = deskId is { } id
            ? office.Rooms.SelectMany(r => r.Desks).FirstOrDefault(d => d.Id == id) ?? throw new NotFoundException("Desk", id.Value)
            : (await AuditorsAsync(office, cancellationToken).ConfigureAwait(false)).FirstOrDefault()
              ?? throw new RejectedException("no-auditor", $"Office '{office.Name}' has no security desk yet: add one first.");

        var now = clock.GetUtcNow();
        var title = $"Security audit ({profile.ToString().ToLowerInvariant()})";
        var folder = records.Open(Project(office), now);
        var task = WorkTask.Assign(office, desk.Id, title, Brief(office, profile, folder), requestedBy, now);
        task.WriteOutputTo(folder);
        records.Link(folder, task.Id.Value);
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return task.Id;
    }

    /// <summary>
    /// Gives the office a Security room with an Auditor desk on Claude Code, and the shared
    /// Security auditor role, made the first time it is needed.
    /// </summary>
    public async Task<DeskId> AddDeskAsync(OfficeId officeId, string? model, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var existing = await AuditorsAsync(office, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            return existing[0].Id;
        }

        var now = clock.GetUtcNow();
        var role = (await roles.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Name, RoleName, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            role = Role.Define(
                RoleName,
                "Audits the office's code for security holes with the security-audit skill, without changing it.",
                RoleInstructions,
                null,
                new RolePermissions(PermissionLevel.Allow, PermissionLevel.Allow, PermissionLevel.Deny, PermissionLevel.Allow),
                now);
            role.UseSkills([Skill], now);
            roles.Add(role);
        }

        var room = office.Rooms.FirstOrDefault(r => r.Kind == RoomKind.Work && string.Equals(r.Name, RoomName, StringComparison.OrdinalIgnoreCase))
            ?? office.OpenRoom(RoomName, now);
        var desk = office.AddDesk(room.Id, DeskName, AgentProfile.Create("build", string.IsNullOrWhiteSpace(model) ? "sonnet" : model.Trim(), AgentEngine.ClaudeCode), now, role.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return desk.Id;
    }

    /// <summary>The office's runs, newest first, with the state of the task behind each.</summary>
    public async Task<IReadOnlyList<SecurityAuditView>> ListAsync(OfficeId officeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var views = new List<SecurityAuditView>();
        foreach (var run in records.List(Project(office)).OrderByDescending(r => r.Number))
        {
            var task = run.TaskId is { } id ? await tasks.FindAsync(new WorkTaskId(id), cancellationToken).ConfigureAwait(false) : null;
            var desk = task?.DeskId is { } deskId ? office.Rooms.SelectMany(r => r.Desks).FirstOrDefault(d => d.Id == deskId)?.Name : null;
            views.Add(new SecurityAuditView(run, task?.Status, desk));
        }

        return views;
    }

    public async Task<string?> ReportAsync(OfficeId officeId, int run, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        return records.Report(Project(office), run);
    }

    public async Task<IReadOnlyList<SecurityFinding>> FindingsAsync(OfficeId officeId, int run, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        return records.Findings(Project(office), run);
    }

    /// <summary>
    /// Proposes a task to fix a finding: a person picks its desk and approves it. A finding
    /// still to verify is proven by a test first, and left alone when the test shows nothing.
    /// </summary>
    public async Task<WorkTaskId> ProposeFixAsync(
        OfficeId officeId, int run, string fingerprint, string requestedBy, CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var finding = records.Findings(Project(office), run).FirstOrDefault(f => f.Fingerprint == fingerprint)
            ?? throw new RejectedException("finding-not-found", $"Run {run} has no finding {fingerprint}.");
        if (finding.Verdict is not ("confirmed" or "needs_validation"))
        {
            throw new RejectedException("finding-rejected", "A rejected finding was disproved: there is nothing to fix.");
        }

        var verb = finding.Verdict == "confirmed" ? "Fix" : "Verify and fix";
        var task = WorkTask.Propose(office, Shorten($"{verb}: {finding.Title}", 120), FixBrief(finding, run), requestedBy, clock.GetUtcNow());
        tasks.Add(task);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return task.Id;
    }

    /// <summary>The office's desks whose role names the security-audit skill.</summary>
    private async Task<List<Desk>> AuditorsAsync(Office office, CancellationToken cancellationToken)
    {
        var auditing = (await roles.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.Skills?.Contains(Skill) == true)
            .Select(r => r.Id)
            .ToHashSet();
        return [.. office.Rooms.SelectMany(r => r.Desks).Where(d => d.RoleId is { } id && auditing.Contains(id))];
    }

    /// <summary>The folder name of an office's audits: its name, made safe for a path.</summary>
    public static string Project(Office office)
    {
        ArgumentNullException.ThrowIfNull(office);
        var safe = new string([.. office.Name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        while (safe.Contains("--", StringComparison.Ordinal))
        {
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        }

        safe = safe.Trim('-');
        return safe.Length > 0 ? safe : office.Id.Value.ToString("N", CultureInfo.InvariantCulture)[..8];
    }

    internal static string Brief(Office office, SecurityAuditProfile profile, string folder) => $"""
        Run a full security audit of this repository ({office.Name}) with the security-audit skill. Load the skill first.

        - Profile: {profile.ToString().ToLowerInvariant()}.
        - Output directory: {folder}
          Use exactly this directory for every record of the run (run-metadata.json, architecture.md, coverage-ledger.json, findings.json, REPORT.md and the agents' folders). It already exists. Never write records inside the repository.
        - Look at the earlier runs next to it ({Path.GetDirectoryName(folder)}) as the skill's prior runs.
        - Do not change any file of the repository: fixes are separate tasks.
        - Source and local checks only: no deployed sites, no external services, no real accounts.
        """;

    internal static string FixBrief(SecurityFinding finding, int run)
    {
        var text = new StringBuilder();
        text.AppendLine(finding.Verdict == "confirmed"
            ? string.Create(CultureInfo.InvariantCulture, $"Fix this confirmed security finding from audit run {run} ({finding.Severity ?? "no severity"}).")
            : string.Create(CultureInfo.InvariantCulture, $"Verify, then fix, this security finding from audit run {run}. The auditor could not run the code to confirm it. First write a test that shows the hole; if the test shows nothing, change no code, keep the test only if it is useful, and say so in a note."));
        text.AppendLine();
        text.AppendLine(finding.Description);
        if (finding.Location is { } where)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Where it starts: {where}");
        }

        if (finding.Remediation is { } how)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"Suggested fix: {how}");
        }

        text.AppendLine();
        text.AppendLine("Make the smallest change that closes it, add a test that would have caught it, and keep everything else as it is.");
        return text.ToString().Trim();
    }

    private static string Shorten(string text, int most) => text.Length <= most ? text : string.Concat(text.AsSpan(0, most - 1), "…");
}
