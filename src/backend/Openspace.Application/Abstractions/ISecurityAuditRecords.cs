namespace Openspace.Application.Abstractions;

/// <summary>One run of a security audit, as its records on disk tell it.</summary>
/// <param name="Number">The run's number for its project, from 1.</param>
/// <param name="TaskId">The Openspace task that ran it.</param>
/// <param name="Severities">Confirmed findings by overall severity: informational, low, medium, high, critical.</param>
/// <param name="HasReport">Whether the run finished its REPORT.md.</param>
/// <param name="HandOff">Whether its findings go to the supervisor on their own once it finishes.</param>
/// <param name="HandedOff">Whether that already happened.</param>
public sealed record SecurityAuditRun(
    int Number,
    DateTimeOffset StartedAt,
    Guid? TaskId,
    string? Profile,
    string? SourceRef,
    int Confirmed,
    int NeedsValidation,
    int Rejected,
    IReadOnlyDictionary<string, int> Severities,
    bool HasReport,
    bool HandOff = false,
    bool HandedOff = false);

/// <summary>A finding of a run, short enough to list and to hand to a fix.</summary>
/// <param name="Verdict">confirmed, needs_validation or rejected.</param>
/// <param name="Severity">The overall severity of a confirmed finding; null for the others.</param>
/// <param name="Location">Where it starts, as <c>file:line</c>.</param>
/// <param name="Remediation">How to fix it, for confirmed findings.</param>
/// <param name="Handed">Whether it was already handed to the supervisor to fix.</param>
public sealed record SecurityFinding(
    string Fingerprint,
    string Verdict,
    string Title,
    string Description,
    string? Severity,
    string? Location,
    string? Remediation,
    bool Handed = false);

/// <summary>
/// Where security audits keep their records: a folder per project and run, outside every
/// repository, written by the auditing agent and read by Openspace.
/// </summary>
public interface ISecurityAuditRecords
{
    /// <summary>Opens the next run's empty folder for the project and returns its full path.</summary>
    string Open(string project, DateTimeOffset now);

    /// <summary>Records which task runs the audit in a run's folder, and whether its findings go on by themselves.</summary>
    void Link(string folder, Guid taskId, bool handOff = false);

    /// <summary>Records that these findings were handed to the supervisor; with <paramref name="all"/>, the run's hand-off is done.</summary>
    void MarkHanded(string project, int run, IEnumerable<string> fingerprints, bool all);

    IReadOnlyList<SecurityAuditRun> List(string project);

    /// <summary>The run's REPORT.md, or null while it has none.</summary>
    string? Report(string project, int run);

    IReadOnlyList<SecurityFinding> Findings(string project, int run);
}
