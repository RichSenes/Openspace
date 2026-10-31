using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Roles;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);
        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.Description).IsRequired();
        builder.Property(r => r.Instructions).IsRequired();
        builder.Property(r => r.Model);
        builder.OwnsOne(r => r.Permissions, p =>
        {
            p.Property(x => x.Edit).HasColumnName("CanEdit").HasConversion<string>();
            p.Property(x => x.Bash).HasColumnName("CanRunCommands").HasConversion<string>();
            p.Property(x => x.Web).HasColumnName("CanUseWeb").HasConversion<string>();
            p.Property(x => x.Helpers).HasColumnName("CanDelegate").HasConversion<string>();
        });
        builder.Navigation(r => r.Permissions).IsRequired();
    }
}
