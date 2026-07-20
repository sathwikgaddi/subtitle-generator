using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class AiGenerationConfiguration : IEntityTypeConfiguration<AiGeneration>
{
    public void Configure(EntityTypeBuilder<AiGeneration> builder)
    {
        builder.HasKey(g => g.Id);

        // One row per (video, stage), upserted on regeneration — docs/Database.md §2.11.
        builder.HasIndex(g => new { g.VideoId, g.Stage }).IsUnique();

        // Queryable across all videos for a stage/prompt version, e.g. "find every video whose
        // English track used prompt version 3" — docs/Database.md §3 design note.
        builder.HasIndex(g => new { g.Stage, g.PromptVersionId });

        builder.Property(g => g.Stage).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(g => g.Reason).IsRequired();
        builder.Property(g => g.GeneratedAt).IsRequired();

        builder.HasOne(g => g.Video)
            .WithMany(v => v.AiGenerations)
            .HasForeignKey(g => g.VideoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.SubtitleTrack)
            .WithMany()
            .HasForeignKey(g => g.SubtitleTrackId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(g => g.PromptVersion)
            .WithMany()
            .HasForeignKey(g => g.PromptVersionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
