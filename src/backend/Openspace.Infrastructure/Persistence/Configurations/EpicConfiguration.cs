using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class EpicConfiguration : IEntityTypeConfiguration<Epic>
{
    public void Configure(EntityTypeBuilder<Epic> builder)
    {
        builder.ToTable("Epics");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.DomainEvents);
        builder.Property(e => e.Title).IsRequired();
        builder.Property(e => e.Request).IsRequired();
        builder.Property(e => e.RequestedBy).IsRequired();
        builder.Property(e => e.CreatedAt);
        builder.HasOne<Office>().WithMany().HasForeignKey(e => e.OfficeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.OfficeId, e.CreatedAt });
    }
}
