using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Offices;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class OfficeConfiguration : IEntityTypeConfiguration<Office>
{
    public void Configure(EntityTypeBuilder<Office> builder)
    {
        builder.ToTable("Offices");
        builder.HasKey(o => o.Id);
        builder.Ignore(o => o.DomainEvents);
        builder.Property(o => o.Name).IsRequired();

        builder.OwnsMany(o => o.Rooms, room =>
        {
            room.ToTable("Rooms");
            room.WithOwner().HasForeignKey("OfficeId");
            room.HasKey(r => r.Id);
            room.Property(r => r.Id).ValueGeneratedNever();
            room.Property(r => r.Name).IsRequired();
            room.Property(r => r.Context).IsRequired();

            room.OwnsMany(r => r.Desks, desk =>
            {
                desk.ToTable("Desks");
                desk.WithOwner().HasForeignKey("RoomId");
                desk.HasKey(d => d.Id);
                desk.Property(d => d.Id).ValueGeneratedNever();
                desk.Property(d => d.Name).IsRequired();
                desk.OwnsOne(d => d.Agent, agent =>
                {
                    agent.Property(a => a.AgentName).HasColumnName("AgentName").IsRequired();
                    agent.Property(a => a.Model).HasColumnName("Model");
                });
                desk.Navigation(d => d.Agent).IsRequired();
            });

            room.Navigation(r => r.Desks).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(o => o.Rooms).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
