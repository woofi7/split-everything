using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Key).HasMaxLength(48).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(60).IsRequired();
        builder.Property(c => c.IconName).HasMaxLength(48).IsRequired();
        builder.Property(c => c.ColorHex).HasMaxLength(9).IsRequired();
        builder.Property(c => c.KeywordsJson).HasColumnType("jsonb");

        builder.HasIndex(c => new { c.GroupId, c.Key }).IsUnique();

        builder.HasOne(c => c.Group)
            .WithMany()
            .HasForeignKey(c => c.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
