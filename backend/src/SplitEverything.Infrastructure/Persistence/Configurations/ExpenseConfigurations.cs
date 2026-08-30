using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Description).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Property(e => e.Currency).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Amount).HasPrecision(19, 4);
        builder.Property(e => e.AmountInBaseCurrency).HasPrecision(19, 4);
        builder.Property(e => e.ExchangeRate).HasPrecision(18, 8);
        builder.Property(e => e.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.LastWriterDeviceId).HasMaxLength(64);
        builder.Property(e => e.ImportFingerprint).HasMaxLength(64);

        builder.HasIndex(e => new { e.GroupId, e.SpentAt });
        builder.HasIndex(e => new { e.GroupId, e.ServerSeq });
        builder.HasIndex(e => e.PaidByMemberId);
        // Import dedupe looks up by fingerprint across the user's groups.
        builder.HasIndex(e => e.ImportFingerprint);
        builder.HasIndex(e => e.ImportBatchId);
        builder.HasIndex(e => e.RecurringExpenseId);

        builder.HasOne(e => e.Group)
            .WithMany(g => g.Expenses)
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.PaidByMember)
            .WithMany()
            .HasForeignKey(e => e.PaidByMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Receipt)
            .WithMany()
            .HasForeignKey(e => e.ReceiptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.RecurringExpense)
            .WithMany()
            .HasForeignKey(e => e.RecurringExpenseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(e => e.Clock);
    }
}

public class ExpenseSplitConfiguration : IEntityTypeConfiguration<ExpenseSplit>
{
    public void Configure(EntityTypeBuilder<ExpenseSplit> builder)
    {
        builder.ToTable("expense_splits");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Amount).HasPrecision(19, 4);
        builder.Property(s => s.AmountInBaseCurrency).HasPrecision(19, 4);
        builder.Property(s => s.InputValue).HasPrecision(19, 6);
        builder.Property(s => s.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.LastWriterDeviceId).HasMaxLength(64);

        builder.HasIndex(s => new { s.ExpenseId, s.MemberId }).IsUnique();
        builder.HasIndex(s => s.MemberId);
        builder.HasIndex(s => s.GroupId);

        builder.HasOne(s => s.Expense)
            .WithMany(e => e.Splits)
            .HasForeignKey(s => s.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Member)
            .WithMany()
            .HasForeignKey(s => s.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(s => s.Clock);
    }
}

public class ExpenseItemConfiguration : IEntityTypeConfiguration<ExpenseItem>
{
    public void Configure(EntityTypeBuilder<ExpenseItem> builder)
    {
        builder.ToTable("expense_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Description).HasMaxLength(300).IsRequired();
        builder.Property(i => i.Amount).HasPrecision(19, 4);
        builder.Property(i => i.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(i => i.LastWriterDeviceId).HasMaxLength(64);

        builder.HasIndex(i => i.ExpenseId);

        builder.HasOne(i => i.Expense)
            .WithMany(e => e.Items)
            .HasForeignKey(i => i.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(i => i.Clock);
    }
}

public class ExpenseItemShareConfiguration : IEntityTypeConfiguration<ExpenseItemShare>
{
    public void Configure(EntityTypeBuilder<ExpenseItemShare> builder)
    {
        builder.ToTable("expense_item_shares");
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => new { s.ExpenseItemId, s.MemberId }).IsUnique();

        builder.HasOne(s => s.ExpenseItem)
            .WithMany(i => i.Shares)
            .HasForeignKey(s => s.ExpenseItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Member)
            .WithMany()
            .HasForeignKey(s => s.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ExpenseCommentConfiguration : IEntityTypeConfiguration<ExpenseComment>
{
    public void Configure(EntityTypeBuilder<ExpenseComment> builder)
    {
        builder.ToTable("expense_comments");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Body).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.LastWriterDeviceId).HasMaxLength(64);

        builder.HasIndex(c => new { c.ExpenseId, c.CreatedAt });
        builder.HasIndex(c => c.GroupId);

        builder.HasOne(c => c.Expense)
            .WithMany(e => e.Comments)
            .HasForeignKey(c => c.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.AuthorMember)
            .WithMany()
            .HasForeignKey(c => c.AuthorMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ParentComment)
            .WithMany(c => c.Replies)
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(c => c.Clock);
    }
}

public class ExpenseRevisionConfiguration : IEntityTypeConfiguration<ExpenseRevision>
{
    public void Configure(EntityTypeBuilder<ExpenseRevision> builder)
    {
        builder.ToTable("expense_revisions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.ChangeSummary).HasMaxLength(500);
        builder.Property(r => r.EditedByDeviceId).HasMaxLength(64);

        builder.HasIndex(r => new { r.ExpenseId, r.Revision }).IsUnique();

        builder.HasOne(r => r.Expense)
            .WithMany(e => e.History)
            .HasForeignKey(r => r.ExpenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("recurring_expenses");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Description).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Amount).HasPrecision(19, 4);
        builder.Property(r => r.SplitTemplateJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.LastWriterDeviceId).HasMaxLength(64);

        // The worker scans for what is due; keep that lookup cheap.
        builder.HasIndex(r => new { r.IsPaused, r.NextRunAt });
        builder.HasIndex(r => r.GroupId);

        builder.HasOne(r => r.Group)
            .WithMany()
            .HasForeignKey(r => r.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(r => r.Clock);
    }
}

public class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("settlements");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Currency).HasMaxLength(3).IsRequired();
        builder.Property(s => s.Amount).HasPrecision(19, 4);
        builder.Property(s => s.AmountInBaseCurrency).HasPrecision(19, 4);
        builder.Property(s => s.ExchangeRate).HasPrecision(18, 8);
        builder.Property(s => s.Note).HasMaxLength(1000);
        builder.Property(s => s.VectorClockJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.LastWriterDeviceId).HasMaxLength(64);

        builder.HasIndex(s => new { s.GroupId, s.SettledAt });
        builder.HasIndex(s => new { s.GroupId, s.ServerSeq });

        builder.HasOne(s => s.Group)
            .WithMany(g => g.Settlements)
            .HasForeignKey(s => s.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.FromMember)
            .WithMany()
            .HasForeignKey(s => s.FromMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ToMember)
            .WithMany()
            .HasForeignKey(s => s.ToMemberId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Receipt)
            .WithMany()
            .HasForeignKey(s => s.ReceiptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Ignore(s => s.Clock);
    }
}

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.StorageKey).HasMaxLength(512).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(r => r.OriginalFileName).HasMaxLength(260);
        builder.Property(r => r.ContentHash).HasMaxLength(64).IsRequired();

        // Same photo uploaded twice reuses one blob.
        builder.HasIndex(r => r.ContentHash).IsUnique();
    }
}
