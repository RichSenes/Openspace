using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Reports;
using Openspace.Domain.Work;

namespace Openspace.Application.Reports;

/// <summary>
/// Writes reports for people: the facts come from the audit log; a model turns them
/// into prose and, if it cannot, the facts themselves are the report.
/// </summary>
public sealed partial class ReportWriter(
    IOfficeRepository offices,
    IEpicRepository epics,
    IWorkTaskRepository tasks,
    IAuditLog audit,
    IReportRepository reports,
    IUnitOfWork unitOfWork,
    IAgentRuntime runtime,
    TimeProvider clock,
    ILogger<ReportWriter> logger)
{
    private const string Writer = """
        You write work reports for people who were not there: a manager, a colleague, a client.
        Use only the facts you are given; never invent work, results, people or dates. If something is unknown, leave it out.
        Write in plain language for non-developers first, with technical details (files, branches) only where they help.
        Answer in Markdown with these sections, skipping any that would be empty: a two or three sentence summary, Done, In progress, Problems and blocks, Next steps.
        Write everything in the report language, section headings included.
        Mention tasks that failed or whose required procedure is incomplete under Problems and blocks.
        No introduction, no sign-off.
        """;

    public async Task<ReportView> WriteForPeriodAsync(
        OfficeId officeId, DateTimeOffset from, DateTimeOffset to, string scope, string language, string requestedBy,
        CancellationToken cancellationToken)
    {
        var office = await offices.GetAsync(officeId, cancellationToken).ConfigureAwait(false);
        var active = await audit.TasksActiveBetweenAsync(from, to, cancellationToken).ConfigureAwait(false);
        var worked = new List<TaskActivity>();
        foreach (var id in active)
        {
            if (await tasks.FindAsync(id, cancellationToken).ConfigureAwait(false) is { } task && task.OfficeId == office.Id)
            {
                var log = await audit.ForTaskAsync(id, cancellationToken).ConfigureAwait(false);
                worked.Add(new TaskActivity(task, [.. log.Where(e => e.At >= from && e.At < to)]));
            }
        }

        var facts = ReportFacts.Build(office, scope, [.. worked.OrderBy(w => w.Task.CreatedAt)]);
        var (body, byAi) = await WriteAsync(facts, language, cancellationToken).ConfigureAwait(false);
        var report = Report.ForPeriod(office, from, to, $"{office.Name}: {scope}", body, byAi, requestedBy, clock.GetUtcNow());
        return await SaveAsync(report, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReportView> WriteForEpicAsync(EpicId epicId, string language, string requestedBy, CancellationToken cancellationToken)
    {
        var epic = await epics.GetAsync(epicId, cancellationToken).ConfigureAwait(false);
        var office = await offices.GetAsync(epic.OfficeId, cancellationToken).ConfigureAwait(false);
        var worked = new List<TaskActivity>();
        foreach (var task in await tasks.ListByEpicAsync(epic.Id, cancellationToken).ConfigureAwait(false))
        {
            worked.Add(new TaskActivity(task, await audit.ForTaskAsync(task.Id, cancellationToken).ConfigureAwait(false)));
        }

        var facts = ReportFacts.Build(office, epic.Title, worked, epic);
        var (body, byAi) = await WriteAsync(facts, language, cancellationToken).ConfigureAwait(false);
        var report = Report.ForEpic(epic, epic.Title, body, byAi, requestedBy, clock.GetUtcNow());
        return await SaveAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string Body, bool ByAi)> WriteAsync(string facts, string language, CancellationToken cancellationToken)
    {
        try
        {
            var prose = await runtime
                .DraftAsync($"{Writer}\nWrite the report in {language}.", facts, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(prose))
            {
                return (prose.Trim(), true);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogWriterUnavailable(logger, ex);
        }

        return (facts, false);
    }

    private async Task<ReportView> SaveAsync(Report report, CancellationToken cancellationToken)
    {
        reports.Add(report);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ReportView.From(report);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The report writer was unavailable; the report lists the facts only")]
    private static partial void LogWriterUnavailable(ILogger logger, Exception exception);
}
