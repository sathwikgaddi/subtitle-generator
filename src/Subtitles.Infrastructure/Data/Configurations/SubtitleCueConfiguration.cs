using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class SubtitleCueConfiguration : IEntityTypeConfiguration<SubtitleCue>
{
    public void Configure(EntityTypeBuilder<SubtitleCue> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.SubtitleTrackId, c.SequenceNumber }).IsUnique();

        builder.Property(c => c.GeneratedText).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
        builder.Ignore(c => c.Text);

        builder.HasOne(c => c.SubtitleTrack)
            .WithMany(t => t.Cues)
            .HasForeignKey(c => c.SubtitleTrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
