using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Offices;
using Openspace.Domain.Reports;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
{
    public void Configure(EntityTypeBuilder<Report> builder)
    {
        builder.ToTable("Reports");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);
        builder.Property(r => r.Kind).HasConversion<string>();
        builder.Property(r => r.Title).IsRequired();
        builder.Property(r => r.Body).IsRequired();
        builder.Property(r => r.RequestedBy).IsRequired();
        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.From);
        builder.Property(r => r.To);
        builder.Property(r => r.EpicId);
        builder.Property(r => r.WrittenByAi);
        builder.HasOne<Office>().WithMany().HasForeignKey(r => r.OfficeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Epic>().WithMany().HasForeignKey(r => r.EpicId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(r => new { r.OfficeId, r.CreatedAt });
    }
}
