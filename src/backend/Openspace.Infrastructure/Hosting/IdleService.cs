using Microsoft.Extensions.Hosting;

namespace Openspace.Infrastructure.Hosting;

/// <summary>Stands in for a background service that configuration switched off.</summary>
internal sealed class IdleService : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}
