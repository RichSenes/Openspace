using Openspace.Domain.Common;

namespace Openspace.Domain.Work;

public enum AuditKind
{
    /// <summary>The task moved through its life: created, started, reviewed, merged…</summary>
    Lifecycle,

    /// <summary>The agent moved to a new step of its plan.</summary>
    Step,

    Read,

    /// <summary>A file was created or changed.</summary>
    Change,

    Command,

    Search,

    Permission,

    /// <summary>A note written on the task by a person, the agent or Openspace.</summary>
    Note,

    /// <summary>A required procedure step reported as done, with its evidence.</summary>
    Procedure,

    Other,
}

/// <summary>
/// One line of a task's audit log: something that happened, when, and the raw
/// detail behind it. Entries are only ever added, never changed.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Longest detail kept; command output and diffs beyond this are cut.</summary>
    public const int MaxDetailLength = 16_000;

    private AuditEntry(
        long id, WorkTaskId taskId, DateTimeOffset at, AuditKind kind, string summary, string? detail, bool? succeeded, string? externalId)
    {
        Id = id;
        TaskId = taskId;
        At = at;
        Kind = kind;
        Summary = summary;
        Detail = detail;
        Succeeded = succeeded;
        ExternalId = externalId;
    }

    /// <summary>Assigned by storage in the order entries are recorded; 0 until then.</summary>
    public long Id { get; }

    public WorkTaskId TaskId { get; }

    public DateTimeOffset At { get; }

    public AuditKind Kind { get; }

    /// <summary>One human sentence: "Changed header.php", "Ran npm test".</summary>
    public string Summary { get; }

    /// <summary>The evidence: a diff, a command and its output, an error.</summary>
    public string? Detail { get; }

    /// <summary>Whether the action worked, when that applies.</summary>
    public bool? Succeeded { get; }

    /// <summary>Identifier of the source event (e.g. an OpenCode tool call), to record it once.</summary>
    public string? ExternalId { get; }

    public static AuditEntry Record(
        WorkTaskId taskId,
        DateTimeOffset at,
        AuditKind kind,
        string summary,
        string? detail = null,
        bool? succeeded = null,
        string? externalId = null)
    {
        var text = Guard.NotBlank(summary, "Audit summary", 500);
        var trimmed = string.IsNullOrWhiteSpace(detail) ? null : detail.TrimEnd();
        if (trimmed is { Length: > MaxDetailLength })
        {
            trimmed = string.Concat(trimmed.AsSpan(0, MaxDetailLength), "\n… (cut)");
        }

        return new AuditEntry(0, taskId, at, kind, text, trimmed, succeeded, externalId);
    }
}
