using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class AgreementConfiguration : IEntityTypeConfiguration<Agreement>
{
    public void Configure(EntityTypeBuilder<Agreement> builder)
    {
        builder.ToTable("Agreements");
        builder.HasKey(a => a.Id);
        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.GoesSecond);
        builder.Property(a => a.FirstDesk);
        builder.Property(a => a.FirstTask);
        builder.Property(a => a.SecondDesk);
        builder.Property(a => a.SecondTask);
        builder.Property(a => a.OpenedAt);
        builder.Property(a => a.Subject).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>();
        builder.Property(a => a.FirstProposal).IsRequired();
        builder.Property(a => a.SecondProposal).IsRequired();
        builder.Property(a => a.Reason).IsRequired();
        builder.Property(a => a.MinutesPath).IsRequired();
        builder.HasOne<Office>().WithMany().HasForeignKey(a => a.OfficeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.OfficeId, a.OpenedAt });
    }
}
