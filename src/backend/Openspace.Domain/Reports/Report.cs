using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Domain.Reports;

public readonly record struct ReportId(Guid Value)
{
    public static ReportId New() => new(Guid.CreateVersion7());
}

public enum ReportKind
{
    /// <summary>Everything that happened in an office over a period, usually a day.</summary>
    Period,

    /// <summary>One epic, from the request to where it stands.</summary>
    Epic,

    /// <summary>What changed for the people using the product, since the previous changelog.</summary>
    Changelog,
}

/// <summary>A written account of work, for people who were not there. Kept as written.</summary>
public sealed class Report : AggregateRoot<ReportId>
{
    private const int MaxBodyLength = 200_000;

    private Report(
        ReportId id, OfficeId officeId, ReportKind kind, string title, string body, string requestedBy,
        DateTimeOffset createdAt, DateTimeOffset? from, DateTimeOffset? to, EpicId? epicId, bool writtenByAi)
        : base(id)
    {
        OfficeId = officeId;
        Kind = kind;
        Title = title;
        Body = body;
        RequestedBy = requestedBy;
        CreatedAt = createdAt;
        From = from;
        To = to;
        EpicId = epicId;
        WrittenByAi = writtenByAi;
    }

    public OfficeId OfficeId { get; }

    public ReportKind Kind { get; }

    public string Title { get; }

    /// <summary>The report in Markdown.</summary>
    public string Body { get; }

    public string RequestedBy { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Start of the period covered, for period reports.</summary>
    public DateTimeOffset? From { get; }

    /// <summary>End of the period covered (excluded), for period reports.</summary>
    public DateTimeOffset? To { get; }

    public EpicId? EpicId { get; }

    /// <summary>False when the AI was unavailable and the report lists the facts only.</summary>
    public bool WrittenByAi { get; }

    public static Report ForPeriod(
        Office office, DateTimeOffset from, DateTimeOffset to, string title, string body, bool writtenByAi, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(office);
        if (to <= from)
        {
            throw new DomainException("A report period must end after it starts.");
        }

        return Create(office.Id, ReportKind.Period, title, body, writtenByAi, requestedBy, now, from, to, null);
    }

    public static Report ForEpic(Epic epic, string title, string body, bool writtenByAi, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(epic);
        return Create(epic.OfficeId, ReportKind.Epic, title, body, writtenByAi, requestedBy, now, null, null, epic.Id);
    }

    /// <param name="from">The end of the previous changelog; null for the first one.</param>
    public static Report ForChangelog(
        Office office, DateTimeOffset? from, DateTimeOffset to, string title, string body, bool writtenByAi, string requestedBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(office);
        if (from >= to)
        {
            throw new DomainException("A changelog must end after the previous one.");
        }

        return Create(office.Id, ReportKind.Changelog, title, body, writtenByAi, requestedBy, now, from, to, null);
    }

    private static Report Create(
        OfficeId officeId, ReportKind kind, string title, string body, bool writtenByAi, string requestedBy,
        DateTimeOffset now, DateTimeOffset? from, DateTimeOffset? to, EpicId? epicId)
    {
        var report = new Report(
            ReportId.New(), officeId, kind,
            Guard.NotBlank(title, "Report title", 200),
            Guard.NotBlank(body, "Report", MaxBodyLength),
            Guard.NotBlank(requestedBy, "Requester", 80),
            now, from, to, epicId, writtenByAi);
        report.Raise(new ReportWritten(report.Id, officeId, now));
        return report;
    }
}

public sealed record ReportWritten(ReportId ReportId, OfficeId OfficeId, DateTimeOffset OccurredAt) : IDomainEvent;
