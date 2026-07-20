using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class SubtitleTrackConfiguration : IEntityTypeConfiguration<SubtitleTrack>
{
    public void Configure(EntityTypeBuilder<SubtitleTrack> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.VideoId, t.TrackType }).IsUnique();

        builder.Property(t => t.TrackType).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.LanguageCode).IsRequired();
        builder.Property(t => t.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        builder.HasOne(t => t.Video)
            .WithMany(v => v.SubtitleTracks)
            .HasForeignKey(t => t.VideoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
