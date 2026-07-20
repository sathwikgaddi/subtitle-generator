using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.Task, p.Version }).IsUnique();

        builder.Property(p => p.Task).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Version).IsRequired();
        builder.Property(p => p.Template).IsRequired();
        builder.Property(p => p.ModelParamsJson).HasColumnType("jsonb");
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).IsRequired();

        // Exactly one active prompt version per task — docs/Database.md §2.10.
        builder.HasIndex(p => p.Task)
            .IsUnique()
            .HasFilter("is_active");
    }
}
