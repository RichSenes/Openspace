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
        builder.Property(o => o.RepositoryPath);
        builder.Property(o => o.Theme).IsRequired().HasDefaultValue(Office.DefaultTheme);

        builder.OwnsMany(o => o.Rooms, room =>
        {
            room.ToTable("Rooms");
            room.WithOwner().HasForeignKey("OfficeId");
            room.HasKey(r => r.Id);
            room.Property(r => r.Id).ValueGeneratedNever();
            room.Property(r => r.Name).IsRequired();
            room.Property(r => r.Context).IsRequired();
            room.PrimitiveCollection(r => r.Procedure).HasField("_procedure").UsePropertyAccessMode(PropertyAccessMode.Field);
            room.Property(r => r.Width);
            room.Property(r => r.Depth);
            room.OwnsMany(r => r.Furniture, piece =>
            {
                piece.ToTable("Furniture");
                piece.WithOwner().HasForeignKey("RoomId");
                piece.Property<int>("Id");
                piece.HasKey("Id");
                piece.Property(p => p.Name).IsRequired();
            });
            room.Navigation(r => r.Furniture).HasField("_furniture").UsePropertyAccessMode(PropertyAccessMode.Field);

            room.OwnsMany(r => r.Desks, desk =>
            {
                desk.ToTable("Desks");
                desk.WithOwner().HasForeignKey("RoomId");
                desk.HasKey(d => d.Id);
                desk.Property(d => d.Id).ValueGeneratedNever();
                desk.Property(d => d.Name).IsRequired();
                desk.Property(d => d.RoleId);
                desk.OwnsOne(d => d.Agent, agent =>
                {
                    agent.Property(a => a.AgentName).HasColumnName("AgentName").IsRequired();
                    agent.Property(a => a.Model).HasColumnName("Model");
                });
                desk.Navigation(d => d.Agent).IsRequired();
                desk.OwnsOne(d => d.Spot, spot =>
                {
                    spot.Property(s => s.X).HasColumnName("SpotX");
                    spot.Property(s => s.Y).HasColumnName("SpotY");
                });
            });

            room.Navigation(r => r.Desks).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Navigation(o => o.Rooms).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
