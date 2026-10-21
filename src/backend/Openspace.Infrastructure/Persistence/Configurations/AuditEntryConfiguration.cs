using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(e => e.Id);
        // A database-assigned sequence keeps entries in the order they were recorded,
        // even when several share the same instant.
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.At);
        builder.Property(e => e.Kind).HasConversion<string>();
        builder.Property(e => e.Summary).IsRequired();
        builder.Property(e => e.Detail);
        builder.Property(e => e.Succeeded);
        builder.Property(e => e.ExternalId);
        builder.HasOne<WorkTask>().WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TaskId, e.At });

        // A source event (tool call, permission request) is recorded at most once per task.
        builder.HasIndex(e => new { e.TaskId, e.ExternalId }).IsUnique().HasFilter("ExternalId IS NOT NULL");
    }
}
