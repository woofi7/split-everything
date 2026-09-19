using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SplitEverything.Domain.Entities;

namespace SplitEverything.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Domain.Entities.PushSubscription> PushSubscriptions => Set<Domain.Entities.PushSubscription>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupInvite> GroupInvites => Set<GroupInvite>();
    public DbSet<GroupLineageLink> GroupLineageLinks => Set<GroupLineageLink>();

    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpensePayer> ExpensePayers => Set<ExpensePayer>();
    public DbSet<ExpenseSplit> ExpenseSplits => Set<ExpenseSplit>();
    public DbSet<ExpenseItem> ExpenseItems => Set<ExpenseItem>();
    public DbSet<ExpenseItemShare> ExpenseItemShares => Set<ExpenseItemShare>();
    public DbSet<ExpenseComment> ExpenseComments => Set<ExpenseComment>();
    public DbSet<ExpenseRevision> ExpenseRevisions => Set<ExpenseRevision>();
    public DbSet<RecurringExpense> RecurringExpenses => Set<RecurringExpense>();

    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<Receipt> Receipts => Set<Receipt>();

    public DbSet<ActivityLogEntry> ActivityLog => Set<ActivityLogEntry>();
    public DbSet<SyncLogEntry> SyncLog => Set<SyncLogEntry>();
    public DbSet<SyncSnapshot> SyncSnapshots => Set<SyncSnapshot>();
    public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

    public DbSet<ExchangeRateSnapshot> ExchangeRates => Set<ExchangeRateSnapshot>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ForceUtcTimestamps(builder);
        base.OnModelCreating(builder);
    }

    private static void ForceUtcTimestamps(ModelBuilder builder)
    {
        var toUtc = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(),
            value => value.ToUniversalTime());

        var toUtcNullable = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            value => value.HasValue ? value.Value.ToUniversalTime() : value,
            value => value.HasValue ? value.Value.ToUniversalTime() : value);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                    property.SetValueConverter(toUtc);
                else if (property.ClrType == typeof(DateTimeOffset?))
                    property.SetValueConverter(toUtcNullable);
            }
        }
    }
}
