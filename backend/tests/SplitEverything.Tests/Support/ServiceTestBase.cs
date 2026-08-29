using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Tests.Support;

/// <summary>
/// Wires the real services against the real database, substituting only what
/// crosses the network: Google, Frankfurter, email and push. Those are the
/// boundaries worth faking; faking the database would only test the fake.
/// </summary>
public abstract class ServiceTestBase(PostgresFixture fixture) : DatabaseTestBase(fixture)
{
    protected FixedClock Clock { get; } = new(new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero));

    protected ICurrencyConverter Currency { get; private set; } = null!;
    protected ISyncBroadcaster Broadcaster { get; private set; } = null!;
    protected IPushDispatcher Push { get; private set; } = null!;
    protected IEmailSender Email { get; private set; } = null!;

    protected ISyncWriter Writer { get; private set; } = null!;
    protected ActivityService Activity { get; private set; } = null!;
    protected GroupService Groups { get; private set; } = null!;
    protected ExpenseService Expenses { get; private set; } = null!;
    protected SettlementService Settlements { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Currency = Substitute.For<ICurrencyConverter>();
        // Default to a no-op conversion so tests that do not care about FX read
        // cleanly; the currency tests override this per case.
        Currency.ConvertAsync(Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                new ConversionResult(call.Arg<decimal>(), 1m, Clock.UtcNow)));

        Broadcaster = Substitute.For<ISyncBroadcaster>();
        Push = Substitute.For<IPushDispatcher>();
        Email = Substitute.For<IEmailSender>();

        Writer = new SyncWriter(Db, new GroupSequenceAllocator(Db), Clock);
        Activity = new ActivityService(Db, Clock);
        Groups = new GroupService(Db, Writer, Activity, Clock);
        Expenses = new ExpenseService(Db, Writer, Activity, Currency, Broadcaster, Push, Clock);
        Settlements = new SettlementService(Db, Writer, Activity, Currency, Broadcaster, Push, Clock);
    }

    protected static SyncOperationDto Operation(
        Guid groupId,
        SplitEverything.Domain.Common.SyncEntityType type,
        Guid entityId,
        SplitEverything.Domain.Common.SyncOperation operation,
        string payloadJson,
        IReadOnlyDictionary<string, long> clock)
        => new(Guid.NewGuid(), type, entityId, operation, groupId, payloadJson, clock, DateTimeOffset.UtcNow);

    protected static NullLogger<T> Logger<T>() => NullLogger<T>.Instance;
}
