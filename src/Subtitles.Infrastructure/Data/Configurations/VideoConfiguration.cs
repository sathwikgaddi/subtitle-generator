using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.HasKey(v => v.Id);
        builder.Property(v => v.OriginalFileName).IsRequired();
        builder.Property(v => v.BlobPath).IsRequired();
        builder.Property(v => v.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(v => v.DetectedLanguageConfidence).HasPrecision(4, 3);
        builder.Property(v => v.CreatedAt).IsRequired();
        builder.Property(v => v.UpdatedAt).IsRequired();

        builder.HasIndex(v => v.AccountId);
        builder.HasIndex(v => v.Status);

        // Deliberately not cascade: video deletion is app-level soft delete
        // (docs/Database.md §3), not a hard DB cascade.
        builder.HasOne(v => v.Account)
            .WithMany(a => a.Videos)
            .HasForeignKey(v => v.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.UploadedByUser)
            .WithMany()
            .HasForeignKey(v => v.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
