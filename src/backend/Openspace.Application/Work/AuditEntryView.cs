using Openspace.Domain.Work;

namespace Openspace.Application.Work;

public sealed record AuditEntryView(long Id, DateTimeOffset At, AuditKind Kind, string Summary, string? Detail, bool? Succeeded)
{
    public static AuditEntryView From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(entry.Id, entry.At, entry.Kind, entry.Summary, entry.Detail, entry.Succeeded);
    }
}
