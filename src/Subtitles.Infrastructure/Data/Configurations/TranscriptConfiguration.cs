using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class TranscriptConfiguration : IEntityTypeConfiguration<Transcript>
{
    public void Configure(EntityTypeBuilder<Transcript> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => t.VideoId).IsUnique();

        builder.Property(t => t.LanguageCode).IsRequired();
        builder.Property(t => t.RawText).IsRequired();
        builder.Property(t => t.CreatedAt).IsRequired();

        builder.Property(t => t.WordTimestamps)
            .HasConversion(
                w => JsonSerializer.Serialize(w, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<IReadOnlyList<WordTimestamp>>(json, JsonSerializerOptions.Web)
                    ?? Array.Empty<WordTimestamp>())
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<WordTimestamp>>(
                (a, b) => (a ?? Array.Empty<WordTimestamp>()).SequenceEqual(b ?? Array.Empty<WordTimestamp>()),
                w => w.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.GetHashCode())),
                w => w.ToList()));
        builder.Property(t => t.WordTimestamps).HasColumnType("jsonb");

        builder.HasOne(t => t.Video)
            .WithOne(v => v.Transcript)
            .HasForeignKey<Transcript>(t => t.VideoId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
