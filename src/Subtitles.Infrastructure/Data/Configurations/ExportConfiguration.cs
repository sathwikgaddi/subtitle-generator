using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class ExportConfiguration : IEntityTypeConfiguration<Export>
{
    public void Configure(EntityTypeBuilder<Export> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => e.VideoId);

        builder.Property(e => e.SubtitleTrackType).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Format).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasOne(e => e.Video)
            .WithMany(v => v.Exports)
            .HasForeignKey(e => e.VideoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
