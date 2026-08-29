using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Configurations;

public class SyncLogEntryConfiguration : IEntityTypeConfiguration<SyncLogEntry>
{
    public void Configure(EntityTypeBuilder<SyncLogEntry> builder)
    {
        builder.ToTable("sync_log");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();

        builder.Property(e => e.DeviceId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();

        // The delta pull is "everything in this group after cursor N", so the
        // (group, seq) pair is both the uniqueness guarantee and the read path.
        builder.HasIndex(e => new { e.GroupId, e.ServerSeq }).IsUnique();
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.LineageId);
        builder.HasIndex(e => e.SupersededBySnapshotId);

        builder.HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.SupersededBySnapshot)
            .WithMany()
            .HasForeignKey(e => e.SupersededBySnapshotId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class SyncSnapshotConfiguration : IEntityTypeConfiguration<SyncSnapshot>
{
    public void Configure(EntityTypeBuilder<SyncSnapshot> builder)
    {
        builder.ToTable("sync_snapshots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.StateJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(s => new { s.GroupId, s.UpToServerSeq });

        builder.HasOne(s => s.Group)
            .WithMany()
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SyncConflictConfiguration : IEntityTypeConfiguration<SyncConflict>
{
    public void Configure(EntityTypeBuilder<SyncConflict> builder)
    {
        builder.ToTable("sync_conflicts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.StoredPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.StoredVectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.IncomingPayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.IncomingVectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.ConflictingFieldsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.StoredDeviceId).HasMaxLength(64);
        builder.Property(c => c.IncomingDeviceId).HasMaxLength(64).IsRequired();

        builder.HasIndex(c => new { c.GroupId, c.Resolution });
        builder.HasIndex(c => new { c.EntityType, c.EntityId });
    }
}

public class ActivityLogEntryConfiguration : IEntityTypeConfiguration<ActivityLogEntry>
{
    public void Configure(EntityTypeBuilder<ActivityLogEntry> builder)
    {
        builder.ToTable("activity_log");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.Summary).HasMaxLength(500).IsRequired();
        builder.Property(a => a.MetadataJson).HasColumnType("jsonb");

        builder.HasIndex(a => new { a.GroupId, a.OccurredAt });
        builder.HasIndex(a => a.ActorUserId);

        builder.HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExchangeRateSnapshotConfiguration : IEntityTypeConfiguration<ExchangeRateSnapshot>
{
    public void Configure(EntityTypeBuilder<ExchangeRateSnapshot> builder)
    {
        builder.ToTable("exchange_rates");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.QuoteCurrency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Rate).HasPrecision(18, 8);

        builder.HasIndex(r => new { r.BaseCurrency, r.QuoteCurrency, r.RateDate }).IsUnique();
    }
}

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> builder)
    {
        builder.ToTable("import_batches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Source).HasMaxLength(32).IsRequired();
        builder.Property(b => b.SourceLabel).HasMaxLength(260);

        builder.HasIndex(b => b.ImportedByUserId);
        builder.HasIndex(b => b.GroupId);
    }
}
