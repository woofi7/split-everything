namespace SplitEverything.Domain.Common;

public enum GroupRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}

public enum MembershipStatus
{
    Pending = 0,
    Active = 1,
    Removed = 2
}

/// <summary>
/// How an expense total is divided between participants.
/// </summary>
public enum SplitType
{
    Equal = 0,
    Percentage = 1,
    Shares = 2,
    ExactAmount = 3,
    Itemized = 4
}

public enum RecurrenceUnit
{
    Day = 0,
    Week = 1,
    Month = 2,
    Year = 3
}

public enum ActivityKind
{
    ExpenseCreated = 0,
    ExpenseUpdated = 1,
    ExpenseDeleted = 2,
    ExpenseTransferred = 3,
    SettlementCreated = 4,
    SettlementDeleted = 5,
    CommentPosted = 6,
    MemberJoined = 7,
    MemberInvited = 8,
    MemberRemoved = 9,
    GroupCreated = 10,
    GroupArchived = 11,
    GroupUnarchived = 12,
    GroupMerged = 13,
    GroupSplit = 14,
    DebtNudge = 15,
    ImportCommitted = 16,
    MembersMerged = 17
}

public enum SyncOperation
{
    Create = 0,
    Update = 1,
    Delete = 2,
    /// <summary>Entity moved from one group to another, carrying its history.</summary>
    Transfer = 3,
    /// <summary>Marker entry written when two group logs are reconciled into one.</summary>
    Merge = 4,
    /// <summary>Marker entry written when one group log is partitioned into two.</summary>
    Split = 5,
    /// <summary>Marker entry replacing a range of compacted history.</summary>
    Snapshot = 6
}

public enum SyncEntityType
{
    Group = 0,
    GroupMember = 1,
    Expense = 2,
    ExpenseSplit = 3,
    ExpenseItem = 4,
    Settlement = 5,
    ExpenseComment = 6,
    // Categories were removed from the app. The value stays reserved rather than
    // being reused, so any sync log row written before that still maps to
    // something rather than to whatever took its number.
    CategoryRule = 7,
    UserPreference = 8
}

/// <summary>
/// Result of comparing two vector clocks.
/// </summary>
public enum ClockOrdering
{
    Equal = 0,
    /// <summary>Left happened after right.</summary>
    After = 1,
    /// <summary>Left happened before right.</summary>
    Before = 2,
    /// <summary>Neither dominates: a true conflict needing resolution.</summary>
    Concurrent = 3
}

public enum ConflictResolution
{
    Unresolved = 0,
    KeepLocal = 1,
    KeepRemote = 2,
    Merged = 3
}

public enum GroupLineageKind
{
    Merge = 0,
    Split = 1
}

public enum PushChannel
{
    WebPush = 0,
    Apns = 1,
    Fcm = 2
}
