using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Openspace.Application.Abstractions;
using Openspace.Domain.Common;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

public sealed class OpenspaceDbContext(
    DbContextOptions<OpenspaceDbContext> options,
    IDomainEventPublisher eventPublisher)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Office> Offices => Set<Office>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

    /// <summary>Saves, then publishes the events raised by the saved aggregates.</summary>
    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Count > 0)
            .ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).OrderBy(e => e.OccurredAt).ToList();

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        aggregates.ForEach(a => a.ClearDomainEvents());
        if (events.Count > 0)
        {
            await eventPublisher.PublishAsync(events, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpenspaceDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Properties<OfficeId>().HaveConversion<IdConverters.OfficeIdConverter>();
        configurationBuilder.Properties<RoomId>().HaveConversion<IdConverters.RoomIdConverter>();
        configurationBuilder.Properties<DeskId>().HaveConversion<IdConverters.DeskIdConverter>();
        configurationBuilder.Properties<WorkTaskId>().HaveConversion<IdConverters.WorkTaskIdConverter>();

        // SQLite cannot order or compare DateTimeOffset natively: store it as a sortable 64-bit number.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}
