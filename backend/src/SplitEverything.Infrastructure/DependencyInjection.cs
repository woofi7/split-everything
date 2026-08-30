using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SplitEverything.Application.Abstractions;
using SplitEverything.Application.Services;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Currency;
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Infrastructure.Persistence;
using SplitEverything.Infrastructure.Services;
using SplitEverything.Infrastructure.Storage;
using SplitEverything.Infrastructure.Sync;

namespace SplitEverything.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// One registration point for everything below the API, so the test host and
    /// the real host cannot drift apart on wiring.
    /// </summary>
    public static IServiceCollection AddSplitEverythingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
                               ?? throw new InvalidOperationException(
                                   "ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSplitEverythingPostgres(connectionString));

        services.AddSingleton(Bind<AuthOptions>(configuration, AuthOptions.SectionName));
        services.AddSingleton(Bind<PushOptions>(configuration, PushOptions.SectionName));
        services.AddSingleton(Bind<ReceiptStorageOptions>(configuration, ReceiptStorageOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IGroupSequenceAllocator, GroupSequenceAllocator>();
        services.AddScoped<ISyncWriter, SyncWriter>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();

        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IGroupService, GroupService>();
        services.AddScoped<IInviteService, InviteService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<ISettlementService, SettlementService>();
        services.AddScoped<ISyncService, SyncService>();
        services.AddScoped<IGroupLifecycleService, GroupLifecycleService>();
        services.AddScoped<IImportService, ImportService>();
        services.AddScoped<IStatsService, StatsService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRecurringExpenseService, RecurringExpenseService>();
        services.AddScoped<IReceiptService, ReceiptService>();

        // Local disk today, behind the interface so S3 or MinIO is a swap here.
        services.AddScoped<IReceiptStorage, LocalDiskReceiptStorage>();

        services.AddHttpClient<ICurrencyConverter, FrankfurterCurrencyConverter>(client =>
        {
            client.BaseAddress = new Uri(
                configuration["Currency:FrankfurterBaseUrl"] ?? "https://api.frankfurter.dev/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IPushDispatcher, PushDispatcher>();
        services.AddScoped<IPushSender, WebPushSender>();
        services.AddSingleton<IFcmAccessTokenProvider, FcmAccessTokenProvider>();
        services.AddSingleton<IApnsJwtProvider, ApnsJwtProvider>();
        services.AddHttpClient<FcmPushSender>();
        services.AddHttpClient<ApnsPushSender>();
        services.AddScoped<IPushSender>(sp => sp.GetRequiredService<FcmPushSender>());
        services.AddScoped<IPushSender>(sp => sp.GetRequiredService<ApnsPushSender>());

        // No mail is sent from here: an invite is a link and a QR code, and running
        // an SMTP server for that is more moving parts than the feature is worth.
        // The body still goes to the log, so it can be copied out of a self-hosted
        // install rather than vanishing.
        services.AddScoped<IEmailSender, LoggingEmailSender>();

        return services;
    }

    private static T Bind<T>(IConfiguration configuration, string section) where T : new()
    {
        var instance = new T();
        configuration.GetSection(section).Bind(instance);
        return instance;
    }
}
