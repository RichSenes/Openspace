using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Openspace.Domain.Offices;
using Openspace.Domain.Work;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("WorkTasks");
        builder.HasKey(t => t.Id);
        builder.Ignore(t => t.DomainEvents);
        builder.Ignore(t => t.IsClosed);
        builder.Property(t => t.Title).IsRequired();
        builder.Property(t => t.Description).IsRequired();
        builder.Property(t => t.RequestedBy).IsRequired();
        builder.Property(t => t.MergedInto);
        builder.Property(t => t.EpicId);
        builder.HasOne<Epic>().WithMany().HasForeignKey(t => t.EpicId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(t => t.Status).HasConversion<string>();
        builder.HasOne<Office>().WithMany().HasForeignKey(t => t.OfficeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.Status, t.CreatedAt });

        builder.OwnsOne(t => t.Run, run =>
        {
            run.Property(r => r.SessionId).HasColumnName("RunSessionId");
            run.Property(r => r.WorkspacePath).HasColumnName("RunWorkspacePath");
            run.Property(r => r.Branch).HasColumnName("RunBranch");
            run.HasIndex(r => r.SessionId);
        });

        builder.OwnsMany(t => t.Notes, note =>
        {
            note.ToTable("TaskNotes");
            note.WithOwner().HasForeignKey("WorkTaskId");
            note.Property<int>("Id");
            note.HasKey("Id");
            note.Property(n => n.Text).IsRequired();
            note.OwnsOne(n => n.Author, author =>
            {
                author.Property(a => a.Kind).HasColumnName("AuthorKind").HasConversion<string>();
                author.Property(a => a.Name).HasColumnName("AuthorName");
                author.Property(a => a.DeskId).HasColumnName("AuthorDeskId");
            });
            note.Navigation(n => n.Author).IsRequired();
        });

        builder.Navigation(t => t.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
