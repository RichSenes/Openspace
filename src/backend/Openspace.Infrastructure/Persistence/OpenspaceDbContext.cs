using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Openspace.Application.Abstractions;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence;

public sealed class OpenspaceDbContext(DbContextOptions<OpenspaceDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Office> Offices => Set<Office>();

    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
