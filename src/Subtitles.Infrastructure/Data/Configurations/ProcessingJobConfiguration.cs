using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(j => j.Id);

        builder.Property(j => j.JobType).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(j => j.AvailableAt).IsRequired();
        builder.Property(j => j.CreatedAt).IsRequired();

        // Supports the FOR UPDATE SKIP LOCKED dequeue query verbatim from
        // docs/Architecture.md §2.3: WHERE status='Queued' AND available_at<=now() ORDER BY created_at.
        builder.HasIndex(j => new { j.Status, j.AvailableAt, j.CreatedAt });
        builder.HasIndex(j => j.VideoId);

        builder.HasOne(j => j.Video)
            .WithMany(v => v.ProcessingJobs)
            .HasForeignKey(j => j.VideoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
