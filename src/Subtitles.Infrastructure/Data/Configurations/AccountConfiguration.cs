using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Subtitles.Domain.Entities;

namespace Subtitles.Infrastructure.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired();
        builder.Property(a => a.PlanTier).IsRequired().HasDefaultValue("free");
        builder.Property(a => a.CreatedAt).IsRequired();
    }
}
