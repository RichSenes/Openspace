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

        builder.Property(t => t.ProcedureReminders);
        builder.Ignore(t => t.DependsOn);
        builder.Property(t => t.PullRequestUrl).HasMaxLength(500);
        builder.Property(t => t.PullRequestTarget).HasMaxLength(200);
        builder.PrimitiveCollection<List<Guid>>("_dependsOn").HasColumnName("DependsOn");
        builder.Ignore(t => t.MissingSteps);
        builder.OwnsMany(t => t.Procedure, step =>
        {
            step.ToTable("TaskProcedureSteps");
            step.WithOwner().HasForeignKey("WorkTaskId");
            step.HasKey("WorkTaskId", nameof(ProcedureStep.Number));
            step.Property(s => s.Number).ValueGeneratedNever();
            step.Property(s => s.Text).IsRequired();
            step.Property(s => s.Evidence);
            step.Property(s => s.CompletedAt);
            step.Ignore(s => s.IsDone);
        });
        builder.Navigation(t => t.Procedure).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property(t => t.StatusChangedAt);
        builder.Property(t => t.Round);
        builder.Ignore(t => t.TotalTokens);
        builder.OwnsMany(t => t.ManualChecks, check =>
        {
            check.ToTable("TaskManualChecks");
            check.WithOwner().HasForeignKey("WorkTaskId");
            check.HasKey("WorkTaskId", nameof(ManualCheck.Number));
            check.Property(c => c.Number).ValueGeneratedNever();
            check.Property(c => c.What).IsRequired();
            check.Property(c => c.How).IsRequired();
            check.Property(c => c.CheckedAt);
            check.Ignore(c => c.IsChecked);
        });
        builder.Navigation(t => t.ManualChecks).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(t => t.Usage, usage =>
        {
            usage.ToTable("TaskRoundUsage");
            usage.WithOwner().HasForeignKey("WorkTaskId");
            usage.HasKey("WorkTaskId", nameof(RoundUsage.Round));
            usage.Property(u => u.Round).ValueGeneratedNever();
            usage.Ignore(u => u.Tokens);
        });
        builder.Navigation(t => t.Usage).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
