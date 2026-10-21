using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Openspace.Application.Abstractions;
using Openspace.Application.Queue;
using Openspace.Infrastructure.Git;
using Openspace.Infrastructure.Hosting;
using Openspace.Infrastructure.OpenCode;
using Openspace.Infrastructure.Persistence;

namespace Openspace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OpenspaceDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OpenspaceDbContext>());
        services.AddScoped<IOfficeRepository, OfficeRepository>();
        services.AddScoped<IWorkTaskRepository, WorkTaskRepository>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IEpicRepository, EpicRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();
        services.TryAddSingleton<IDomainEventPublisher>(NullDomainEventPublisher.Instance);
        services.TryAddSingleton<IActivityPublisher, NullActivityPublisher>();
        services.AddSingleton<IWorkspaceManager, GitWorkspaceManager>();
        return services;
    }

    /// <summary>Connects agents: the OpenCode server, its event stream and the background queue.</summary>
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        // Planning and drafting wait for the model to answer: allow minutes, not seconds.
        services.AddHttpClient<IAgentRuntime, OpenCodeRuntime>(ConfigureOpenCode)
            .ConfigureHttpClient(http => http.Timeout = TimeSpan.FromMinutes(5));
        services.AddHttpClient(OpenCodeEventListener.HttpClientName, ConfigureOpenCode)
            .ConfigureHttpClient(http => http.Timeout = Timeout.InfiniteTimeSpan);
        // Plain AddSingleton: AddHostedService would deduplicate two factories of the same return type.
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<OpenCodeOptions>>().Value.ListenToEvents
            ? ActivatorUtilities.CreateInstance<OpenCodeEventListener>(sp)
            : new IdleService());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IOptions<QueueOptions>>().Value.Enabled
            ? ActivatorUtilities.CreateInstance<QueueRunnerService>(sp)
            : new IdleService());
        return services;
    }

    private static void ConfigureOpenCode(IServiceProvider services, HttpClient http)
    {
        var baseUrl = services.GetRequiredService<IOptions<OpenCodeOptions>>().Value.BaseUrl.ToString();
        http.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
    }

    /// <summary>Creates the database or brings it up to the latest migration.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
