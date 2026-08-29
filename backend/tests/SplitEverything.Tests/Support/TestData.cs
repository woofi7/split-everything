using SplitEverything.Domain.Common;
using SplitEverything.Domain.Entities;
using SplitEverything.Domain.Sync;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Persistence.Seed;

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
        DateTimeOffset? spentAt = null, Guid? categoryId = null) => new()
    {
        GroupId = groupId,
        PaidByMemberId = payerMemberId,
        Description = description,
        Amount = amount,
        Currency = currency,
        AmountInBaseCurrency = amount,
        ExchangeRate = 1m,
        SpentAt = spentAt ?? Jan1,
        CategoryId = categoryId,
        SplitType = SplitType.Equal,
        Clock = VectorClock.Empty.Tick(DeviceA)
    };

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

    public static Guid CategoryId(string key) => CategorySeed.DeterministicId(key);

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

    public static async Task<User> SeedUserAsync(AppDbContext db, string name = "Alice")
    {
        var user = User(name);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user;
    }
}
