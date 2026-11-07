using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Openspace.Application.Abstractions;

namespace Openspace.Application.Work;

public enum SupervisorJobState
{
    Planning,
    Done,
    Failed,
}

/// <summary>
/// A plan the supervisor is making, or made a little while ago.
/// </summary>
/// <param name="Kind">What asked for it: <c>plan</c> from the Board, <c>security</c> for audit findings.</param>
/// <param name="What">A few words a person recognises it by.</param>
/// <param name="Key">What two requests for the same thing share, so the second waits for the first.</param>
/// <param name="Tasks">The proposed tasks it made, once done.</param>
/// <param name="EpicId">The epic they are under, when the supervisor planned them.</param>
public sealed record SupervisorJob(
    Guid Id,
    Guid OfficeId,
    string Kind,
    string What,
    string Key,
    DateTimeOffset StartedAt,
    SupervisorJobState State,
    int Tasks = 0,
    Guid? EpicId = null,
    string? Error = null,
    DateTimeOffset? EndedAt = null);

/// <summary>What a plan made: how many tasks, under which epic.</summary>
public sealed record SupervisorOutcome(int Tasks, Guid? EpicId);

/// <summary>
/// Runs the supervisor's plans in the background, so asking for one never holds a page:
/// a plan takes a minute or more. Every plan is shown while it runs and for a quarter of an
/// hour after, to whoever is watching, wherever they are in Openspace.
/// </summary>
public sealed partial class SupervisorJobs(IServiceScopeFactory scopes, IActivityPublisher publisher, TimeProvider clock, ILogger<SupervisorJobs> logger)
{
    /// <summary>How long a finished plan stays on show.</summary>
    public static readonly TimeSpan Shown = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<Guid, SupervisorJob> _jobs = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();
    private readonly Lock _starting = new();

    public IReadOnlyList<SupervisorJob> Snapshot(Guid? officeId = null)
    {
        var now = clock.GetUtcNow();
        foreach (var old in _jobs.Values.Where(j => j.EndedAt is { } ended && now - ended > Shown))
        {
            _jobs.TryRemove(old.Id, out _);
        }

        return [.. _jobs.Values.Where(j => officeId is null || j.OfficeId == officeId).OrderBy(j => j.StartedAt)];
    }

    /// <summary>
    /// Starts a plan in the background and returns it at once. A plan with the same key
    /// still running is returned instead of starting another.
    /// </summary>
    public SupervisorJob Start(Guid officeId, string kind, string what, string key, Func<IServiceProvider, CancellationToken, Task<SupervisorOutcome>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        SupervisorJob job;
        lock (_starting)
        {
            if (_jobs.Values.FirstOrDefault(j => j.Key == key && j.State == SupervisorJobState.Planning) is { } running)
            {
                return running;
            }

            job = new SupervisorJob(Guid.CreateVersion7(), officeId, kind, Short(what), key, clock.GetUtcNow(), SupervisorJobState.Planning);
            _jobs[job.Id] = job;
            _running[job.Id] = Task.Run(() => RunAsync(job, work));
        }

        return job;
    }

    /// <summary>The background run of a plan, for tests and for a clean stop.</summary>
    public Task Completion(Guid id) => _running.TryGetValue(id, out var task) ? task : Task.CompletedTask;

    private async Task RunAsync(SupervisorJob job, Func<IServiceProvider, CancellationToken, Task<SupervisorOutcome>> work)
    {
        await PublishAsync().ConfigureAwait(false);
        SupervisorJob ended;
        try
        {
            using var scope = scopes.CreateScope();
            var outcome = await work(scope.ServiceProvider, CancellationToken.None).ConfigureAwait(false);
            ended = job with { State = SupervisorJobState.Done, Tasks = outcome.Tasks, EpicId = outcome.EpicId, EndedAt = clock.GetUtcNow() };
        }
        catch (Exception ex) when (ex is RejectedException or NotFoundException or InvalidOperationException or HttpRequestException or TimeoutException or TaskCanceledException)
        {
            LogFailed(logger, job.What, ex.Message);
            ended = job with { State = SupervisorJobState.Failed, Error = ex.Message, EndedAt = clock.GetUtcNow() };
        }

        _jobs[job.Id] = ended;
        _running.TryRemove(job.Id, out _);
        await PublishAsync().ConfigureAwait(false);
    }

    private async Task PublishAsync()
    {
        try
        {
            await publisher.SupervisorJobsChangedAsync(Snapshot(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            LogNotPublished(logger, ex.Message);
        }
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 90 ? line : string.Concat(line.AsSpan(0, 89), "…");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The supervisor could not plan {What}: {Reason}")]
    private static partial void LogFailed(ILogger logger, string what, string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not show the supervisor's plans: {Reason}")]
    private static partial void LogNotPublished(ILogger logger, string reason);
}
