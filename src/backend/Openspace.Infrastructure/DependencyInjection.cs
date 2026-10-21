using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Openspace.Application.Abstractions;
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
        services.TryAddSingleton<IDomainEventPublisher>(NullDomainEventPublisher.Instance);
        return services;
    }

    /// <summary>Creates the database or brings it up to the latest migration.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OpenspaceDbContext>();
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
