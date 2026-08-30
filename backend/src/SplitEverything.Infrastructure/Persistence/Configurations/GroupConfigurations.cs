using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Configurations;

public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).HasMaxLength(120).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(2000);
        builder.Property(g => g.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(g => g.IconName).HasMaxLength(48);
        builder.Property(g => g.ColorHex).HasMaxLength(9).IsRequired();
        builder.Property(g => g.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(g => g.DefaultSplitValuesJson).HasColumnType("jsonb");
        builder.Property(g => g.LastWriterDeviceId).HasMaxLength(64);

        builder.HasIndex(g => g.LineageId);
        builder.HasIndex(g => g.CreatedByUserId);

        builder.Ignore(g => g.Clock);
    }
}

public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(m => m.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastWriterDeviceId).HasMaxLength(64);

        // One membership per user per group. Placeholder rows (null user) are exempt,
        // since a group can hold several unclaimed names.
        builder.HasIndex(m => new { m.GroupId, m.UserId })
            .IsUnique()
            .HasFilter("user_id IS NOT NULL");
        builder.HasIndex(m => m.UserId);

        builder.HasOne(m => m.Group)
            .WithMany(g => g.Members)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(m => m.Clock);
        builder.Ignore(m => m.IsPlaceholder);
    }
}

public class GroupInviteConfiguration : IEntityTypeConfiguration<GroupInvite>
{
    public void Configure(EntityTypeBuilder<GroupInvite> builder)
    {
        builder.ToTable("group_invites");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(i => i.InvitedEmail).HasMaxLength(320);

        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.GroupId);
        builder.HasIndex(i => i.InvitedEmail);

        builder.HasOne(i => i.Group)
            .WithMany(g => g.Invites)
            .HasForeignKey(i => i.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(i => i.IsRedeemable);
    }
}

public class GroupLineageLinkConfiguration : IEntityTypeConfiguration<GroupLineageLink>
{
    public void Configure(EntityTypeBuilder<GroupLineageLink> builder)
    {
        builder.ToTable("group_lineage_links");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(l => l.Note).HasMaxLength(1000);

        builder.HasIndex(l => l.SourceGroupId);
        builder.HasIndex(l => l.TargetGroupId);
        builder.HasIndex(l => l.MovedLineageId);
    }
}

