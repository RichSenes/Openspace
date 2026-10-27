using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Openspace.Domain.Offices;
using Openspace.Domain.Routines;

namespace Openspace.Infrastructure.Persistence.Configurations;

internal sealed class RoutineConfiguration :
    IEntityTypeConfiguration<Routine>,
    IEntityTypeConfiguration<RoutineRun>,
    IEntityTypeConfiguration<Proposal>,
    IEntityTypeConfiguration<SeenItem>
{
    public void Configure(EntityTypeBuilder<Routine> builder)
    {
        builder.ToTable("Routines");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(80);
        builder.Property(r => r.Kind).IsRequired().HasMaxLength(40);
        builder.Property(r => r.Schedule).HasConversion(ScheduleConverter.Instance).IsRequired();
        builder.Property(r => r.Settings).IsRequired();
        builder.Property(r => r.Enabled);
        builder.Property(r => r.AskFirst);
        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.LastRunAt);
        builder.Property(r => r.NextRunAt);
        builder.HasOne<Office>().WithMany().HasForeignKey(r => r.OfficeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.NextRunAt);
    }

    public void Configure(EntityTypeBuilder<RoutineRun> builder)
    {
        builder.ToTable("RoutineRuns");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);
        builder.Property(r => r.Status).HasConversion<string>();
        builder.Property(r => r.ByHand);
        builder.Property(r => r.StartedAt);
        builder.Property(r => r.FinishedAt);
        builder.Property(r => r.Summary);
        builder.Property(r => r.Log).IsRequired();
        builder.HasOne<Routine>().WithMany().HasForeignKey(r => r.RoutineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Office>().WithMany().HasForeignKey(r => r.OfficeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.RoutineId, r.StartedAt });
    }

    public void Configure(EntityTypeBuilder<Proposal> builder)
    {
        builder.ToTable("Proposals");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.DomainEvents);
        builder.Property(p => p.Action).IsRequired().HasMaxLength(40);
        builder.Property(p => p.Title).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Body).IsRequired();
        builder.Property(p => p.Payload).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>();
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.DecidedAt);
        builder.Property(p => p.Outcome);
        builder.HasOne<Routine>().WithMany().HasForeignKey(p => p.RoutineId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<RoutineRun>().WithMany().HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.OfficeId, p.Status });
    }

    public void Configure(EntityTypeBuilder<SeenItem> builder)
    {
        builder.ToTable("SeenItems");
        builder.HasKey(s => new { s.RoutineId, s.Key });
        builder.Property(s => s.Key).HasMaxLength(500);
        builder.Property(s => s.Fingerprint).IsRequired();
        builder.Property(s => s.Title).IsRequired().HasMaxLength(300);
        builder.Property(s => s.State).HasConversion<string>();
        builder.Property(s => s.FirstSeenAt);
        builder.Property(s => s.LastSeenAt);
        builder.Property(s => s.ChangedAt);
        builder.HasOne<Routine>().WithMany().HasForeignKey(s => s.RoutineId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>A schedule as a small JSON object: {"days":[1,4],"at":"07:00","every":1,"since":"2026-11-02"}.</summary>
    internal sealed class ScheduleConverter() : ValueConverter<RoutineSchedule, string>(s => Write(s), text => Read(text))
    {
        public static readonly ScheduleConverter Instance = new();

        private sealed record Stored(int[] Days, string At, int Every, string? Since);

        public static string Write(RoutineSchedule schedule) => JsonSerializer.Serialize(new Stored(
            [.. schedule.Days.Select(d => (int)d)],
            schedule.At.ToString("HH:mm", CultureInfo.InvariantCulture),
            schedule.EveryWeeks,
            schedule.Since == DateOnly.MinValue ? null : schedule.Since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        public static RoutineSchedule Read(string text)
        {
            var stored = JsonSerializer.Deserialize<Stored>(text)!;
            return RoutineSchedule.Weekly(
                stored.Days.Select(d => (DayOfWeek)d),
                TimeOnly.ParseExact(stored.At, "HH:mm", CultureInfo.InvariantCulture),
                stored.Every,
                stored.Since is null ? null : DateOnly.ParseExact(stored.Since, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
    }
}
