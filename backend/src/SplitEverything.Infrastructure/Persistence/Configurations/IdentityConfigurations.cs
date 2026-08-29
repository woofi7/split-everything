using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.GoogleSubject).HasMaxLength(64).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.AvatarUrl).HasMaxLength(1024);
        builder.Property(u => u.DefaultCurrency).HasMaxLength(3).IsRequired();
        builder.Property(u => u.Locale).HasMaxLength(16).IsRequired();

        // Google subject is the identity key; email can change on the Google side.
        builder.HasIndex(u => u.GoogleSubject).IsUnique();
        builder.HasIndex(u => u.Email);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.DeviceId).HasMaxLength(64);
        builder.Property(t => t.UserAgent).HasMaxLength(512);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.ExpiresAt });

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.IsActive);
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasMaxLength(64);
        builder.Property(d => d.Label).HasMaxLength(120);
        builder.Property(d => d.Platform).HasMaxLength(32).IsRequired();

        builder.HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<Domain.Entities.PushSubscription>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Endpoint).HasMaxLength(2048).IsRequired();
        builder.Property(p => p.P256dh).HasMaxLength(256);
        builder.Property(p => p.Auth).HasMaxLength(256);
        builder.Property(p => p.DeviceId).HasMaxLength(64);

        builder.HasIndex(p => p.Endpoint).IsUnique();
        builder.HasIndex(p => p.UserId);

        builder.HasOne(p => p.User)
            .WithMany(u => u.PushSubscriptions)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
