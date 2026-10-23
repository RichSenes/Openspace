using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Openspace.Application.Review;

namespace Openspace.Infrastructure.Hosting;

/// <summary>Reviews tasks as they reach review, one at a time, each in a scope of its own.</summary>
internal sealed partial class ReviewRunnerService(
    PendingReviews pending,
    IServiceScopeFactory scopes,
    ILogger<ReviewRunnerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var taskId in pending.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TaskReviewer>().ReviewAsync(taskId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReviewFailed(logger, ex, taskId.Value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not review task {TaskId}")]
    private static partial void LogReviewFailed(ILogger logger, Exception exception, Guid taskId);
}
