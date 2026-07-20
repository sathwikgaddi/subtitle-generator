using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class WordConfiguration : IEntityTypeConfiguration<Word>
{
    public void Configure(EntityTypeBuilder<Word> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => new { w.CueId, w.SequenceNumber }).IsUnique();

        builder.Property(w => w.Text).IsRequired();
        builder.Property(w => w.IsHighlightedAuto).IsRequired();

        builder.HasOne(w => w.Cue)
            .WithMany(c => c.Words)
            .HasForeignKey(w => w.CueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
