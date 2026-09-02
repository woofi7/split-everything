using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Persistence;

namespace SplitEverything.Tests.Support;

/// <summary>
/// Fluent-ish builders for the fixtures nearly every test needs. Kept explicit
/// rather than random so a failing assertion names a value you can read.
/// </summary>
public static class TestData
{
    public const string DeviceA = "device-a";
    public const string DeviceB = "device-b";

    public static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static User User(string name = "Alice", string? email = null, string? googleSub = null) => new()
    {
        GoogleSubject = googleSub ?? $"google-{name.ToLowerInvariant()}",
        Email = email ?? $"{name.ToLowerInvariant()}@example.com",
        DisplayName = name,
        DefaultCurrency = "CAD"
    };

    public static Group Group(Guid createdBy, string name = "Roommates", string currency = "CAD") => new()
    {
        Name = name,
        BaseCurrency = currency,
        CreatedByUserId = createdBy,
        ColorHex = "#4f46e5",
        Clock = VectorClock.Empty.Tick(DeviceA)
    };

    public static GroupMember Member(Guid groupId, Guid? userId, string displayName, GroupRole role = GroupRole.Member) => new()
    {
        GroupId = groupId,
        UserId = userId,
        DisplayName = displayName,
        Role = role,
        Status = MembershipStatus.Active,
        Clock = VectorClock.Empty.Tick(DeviceA)
    };

    public static Expense Expense(
        Guid groupId, Guid payerMemberId, decimal amount,
        string description = "Dinner", string currency = "CAD",
        DateTimeOffset? spentAt = null)
    {
        var expense = new Expense
        {
            GroupId = groupId,
            PaidByMemberId = payerMemberId,
            Description = description,
            Amount = amount,
            Currency = currency,
            AmountInBaseCurrency = amount,
            ExchangeRate = 1m,
            SpentAt = spentAt ?? Jan1,
            SplitType = SplitType.Equal,
            Clock = VectorClock.Empty.Tick(DeviceA)
        };

        // Every expense has at least one payer row, and the balances are computed
        // from those rows: a fixture without one is an expense nobody paid for.
        expense.Payers.Add(new ExpensePayer
        {
            GroupId = groupId,
            MemberId = payerMemberId,
            Amount = amount,
            AmountInBaseCurrency = amount,
            Clock = VectorClock.Empty.Tick(DeviceA)
        });

        return expense;
    }

    /// <summary>An expense several people paid for at once.</summary>
    public static Expense SharedExpense(
        Guid groupId, (Guid MemberId, decimal Amount)[] payers,
        string description = "Dinner", string currency = "CAD",
        DateTimeOffset? spentAt = null)
    {
        var total = payers.Sum(y => y.Amount);
        var main = payers.OrderByDescending(y => y.Amount).ThenBy(y => y.MemberId).First().MemberId;
        var expense = Expense(groupId, main, total, description, currency, spentAt);

        expense.Payers.Clear();
        foreach (var (memberId, amount) in payers)
        {
            expense.Payers.Add(new ExpensePayer
            {
                GroupId = groupId,
                MemberId = memberId,
                Amount = amount,
                AmountInBaseCurrency = amount,
                Clock = VectorClock.Empty.Tick(DeviceA)
            });
        }

        return expense;
    }

    public static ExpenseSplit Split(Guid expenseId, Guid groupId, Guid memberId, decimal amount) => new()
    {
        ExpenseId = expenseId,
        GroupId = groupId,
        MemberId = memberId,
        Amount = amount,
        AmountInBaseCurrency = amount,
        Clock = VectorClock.Empty.Tick(DeviceA)
    };

    public static Settlement Settlement(Guid groupId, Guid from, Guid to, decimal amount) => new()
    {
        GroupId = groupId,
        FromMemberId = from,
        ToMemberId = to,
        Amount = amount,
        AmountInBaseCurrency = amount,
        Currency = "CAD",
        SettledAt = Jan1,
        Clock = VectorClock.Empty.Tick(DeviceA)
    };


    /// <summary>
    /// A group with the given member names, the first of which belongs to
    /// <paramref name="owner"/>. Returns the group plus a name-to-member-id map.
    /// </summary>
    public static async Task<(Group Group, Dictionary<string, Guid> Members)> SeedGroupAsync(
        AppDbContext db, User owner, params string[] memberNames)
    {
        if (memberNames.Length == 0) memberNames = [owner.DisplayName];

        var group = Group(owner.Id);
        db.Groups.Add(group);

        var members = new Dictionary<string, Guid>();
        for (var i = 0; i < memberNames.Length; i++)
        {
            var isOwner = i == 0;
            var member = Member(
                group.Id,
                isOwner ? owner.Id : null,
                memberNames[i],
                isOwner ? GroupRole.Owner : GroupRole.Member);
            db.GroupMembers.Add(member);
            members[memberNames[i]] = member.Id;
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (group, members);
    }

    public static async Task<User> SeedUserAsync(
        AppDbContext db, string name = "Alice", string? email = null, string? googleSub = null)
    {
        var user = User(name, email, googleSub);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user;
    }
}
