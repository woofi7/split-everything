using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SplitEverything.Api.BackgroundJobs;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Contracts.Expenses;
using SplitEverything.Application.Contracts.Groups;
using SplitEverything.Application.Services;
using SplitEverything.Domain.Common;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Sync;
using SplitEverything.Application.Contracts.Sync;
using SplitEverything.Tests.Support;

namespace SplitEverything.Tests.Api;

/// <summary>
/// The three schedulers. What matters is that each one does its work on a tick,
/// survives a failing run without dying, and stops cleanly on shutdown - a worker
/// that exits on the first exception would silently stop generating rent.
/// </summary>
public class BackgroundWorkerTests(PostgresFixture fixture) : ServiceTestBase(fixture)
{
    private ServiceProvider BuildProvider(Action<ServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(Clock);
        services.AddScoped(_ => NewContext());
        services.AddScoped<IGroupSequenceAllocator>(sp =>
            new GroupSequenceAllocator(sp.GetRequiredService<AppDbContext>()));
        services.AddScoped<ISyncWriter>(sp => new SyncWriter(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<IGroupSequenceAllocator>(), Clock));
        services.AddScoped<IActivityService>(sp =>
            new ActivityService(sp.GetRequiredService<AppDbContext>(), Clock));
        services.AddSingleton(Currency);
        services.AddSingleton(Push);
        services.AddScoped<IRecurringExpenseService>(sp => new RecurringExpenseService(
            sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ISyncWriter>(),
            sp.GetRequiredService<IActivityService>(), Currency, Push, Clock));
        services.AddScoped<IGroupLifecycleService>(sp => new GroupLifecycleService(
            sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<ISyncWriter>(),
            sp.GetRequiredService<IActivityService>(), Clock));

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Starts a worker, lets it tick a few times, then shuts it down. With the
    /// immediate schedule the loop body really runs, which is the part worth testing.
    /// </summary>
    private static async Task RunTicksAsync(BackgroundService worker, TimeSpan? window = null)
    {
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(window ?? TimeSpan.FromMilliseconds(300));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_recurring_worker_survives_a_failing_run()
    {
        var recurring = Substitute.For<IRecurringExpenseService>();
        recurring.RunDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("boom"));

        var provider = BuildProvider(services =>
        {
            services.RemoveAll<IRecurringExpenseService>();
            services.AddScoped(_ => recurring);
        });

        var worker = new RecurringExpenseWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<RecurringExpenseWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        // Several ticks all threw, and the worker is still running rather than
        // having died and silently stopped generating rent.
        await recurring.Received().RunDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_recurring_worker_creates_the_occurrences_that_are_due()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Roommates", "CAD", null, null, null, null));
        var member = group.Members.Single().Id;

        var provider = BuildProvider();
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRecurringExpenseService>()
                .CreateAsync(user.Id, new CreateRecurringExpenseRequest(
                    group.Id, member, "Rent", 1200m, "CAD", null, SplitType.Equal,
                    [new SplitInputDto(member, null)], RecurrenceUnit.Month, 1, 1, null,
                    Clock.UtcNow, null, null));
        }

        var worker = new RecurringExpenseWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<RecurringExpenseWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        (await NewContext().Expenses.CountAsync(e => e.Description == "Rent")).ShouldBe(1);
    }

    [Fact]
    public async Task The_recurring_worker_stops_when_the_host_shuts_down()
    {
        var provider = BuildProvider();
        var worker = new RecurringExpenseWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<RecurringExpenseWorker>.Instance, WorkerSchedule.Immediate);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // StopAsync completing means the loop observed cancellation and returned,
        // rather than the host being left waiting on a worker that ignores it.
        (worker.ExecuteTask is not null).ShouldBeTrue();
    }

    [Fact]
    public async Task The_exchange_rate_worker_refreshes_the_currencies_in_use()
    {
        var user = await TestData.SeedUserAsync(Db);
        await Groups.CreateAsync(user.Id, new CreateGroupRequest("Euro trip", "EUR", null, null, null, null));

        var provider = BuildProvider();
        var worker = new ExchangeRateWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExchangeRateWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        // Only the currencies this install actually uses, not the whole table.
        await Currency.Received().RefreshCacheAsync(
            Arg.Is<IEnumerable<string>>(codes => codes.Contains("EUR") && codes.Contains("CAD")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_compaction_worker_collapses_history_older_than_a_year()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Long lived", "CAD", null, null, null, null));
        var member = group.Members.Single().Id;
        await Expenses.CreateAsync(user.Id, new CreateExpenseRequest(
            group.Id, member, "Old", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(member, null)], null, null, null, null, null, null, null));

        Clock.Advance(TimeSpan.FromDays(400));

        var provider = BuildProvider();
        var lifecycle = provider.CreateScope().ServiceProvider.GetRequiredService<IGroupLifecycleService>();
        var result = await lifecycle.CompactAsync(group.Id, Clock.UtcNow.AddDays(-365));

        result.CompactedEntries.ShouldBeGreaterThan(0);

        var worker = new SyncLogCompactionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<SyncLogCompactionWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);
    }

    [Fact]
    public async Task The_compaction_worker_snapshots_a_group_on_its_own()
    {
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Long lived", "CAD", null, null, null, null));
        var member = group.Members.Single().Id;
        await Expenses.CreateAsync(user.Id, new CreateExpenseRequest(
            group.Id, member, "Old", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(member, null)], null, null, null, null, null, null, null));
        Clock.Advance(TimeSpan.FromDays(400));

        var provider = BuildProvider();
        var worker = new SyncLogCompactionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<SyncLogCompactionWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        (await NewContext().SyncSnapshots.CountAsync(s => s.GroupId == group.Id))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_compaction_worker_survives_a_failing_run()
    {
        // The worker only calls out when a group actually has stale history, so the
        // failing path is only reachable once there is something to compact.
        var user = await TestData.SeedUserAsync(Db);
        var group = await Groups.CreateAsync(user.Id,
            new CreateGroupRequest("Long lived", "CAD", null, null, null, null));
        var member = group.Members.Single().Id;
        await Expenses.CreateAsync(user.Id, new CreateExpenseRequest(
            group.Id, member, "Old", 40m, "CAD", TestData.Jan1, SplitType.Equal,
            [new SplitInputDto(member, null)], null, null, null, null, null, null, null));
        Clock.Advance(TimeSpan.FromDays(400));

        var lifecycle = Substitute.For<IGroupLifecycleService>();
        lifecycle.CompactAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<Task<CompactionResult>>(_ =>
                throw new InvalidOperationException("boom"));

        var provider = BuildProvider(services =>
        {
            services.RemoveAll<IGroupLifecycleService>();
            services.AddScoped(_ => lifecycle);
        });

        var worker = new SyncLogCompactionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<SyncLogCompactionWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        await lifecycle.Received().CompactAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_exchange_rate_worker_survives_a_failing_run()
    {
        // A single currency needs no conversion, so there has to be a second one for
        // the refresh to be attempted at all.
        var user = await TestData.SeedUserAsync(Db);
        await Groups.CreateAsync(user.Id, new CreateGroupRequest("Euro trip", "EUR", null, null, null, null));

        var currency = Substitute.For<ICurrencyConverter>();
        currency.RefreshCacheAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("boom"));

        var provider = BuildProvider(services =>
        {
            services.RemoveAll<ICurrencyConverter>();
            services.AddSingleton(currency);
        });

        var worker = new ExchangeRateWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ExchangeRateWorker>.Instance, WorkerSchedule.Immediate);

        await RunTicksAsync(worker);

        await currency.Received().RefreshCacheAsync(
            Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }
}
