using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using SplitEverything.Api.BackgroundJobs;
using SplitEverything.Api.Hubs;
using SplitEverything.Api.Infrastructure;
using SplitEverything.Application.Abstractions;
using SplitEverything.Infrastructure;
using SplitEverything.Infrastructure.Auth;
using SplitEverything.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Second, independent guard on the development sign-in. The service refuses when
// the flag is off; this makes sure the flag cannot be on outside Development at
// all, even if the environment sets it by mistake. It has to run before the
// infrastructure registration, which snapshots the options.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration["Auth:AllowDevelopmentSignIn"] = "false";
}
else
{
    // Invite links and their QR codes are built from this. Left as localhost, a
    // link scanned by a phone points the phone at itself, so on a development
    // machine a loopback host is swapped for one the phone can reach. Also has to
    // run before the registration below, which snapshots the options.
    var configured = builder.Configuration["Auth:AppBaseUrl"] ?? string.Empty;
    var reachable = DevelopmentAppBaseUrl.Rewrite(configured, LocalNetworkAddress.Detect());

    if (reachable != configured)
    {
        builder.Configuration["Auth:AppBaseUrl"] = reachable;
        Console.WriteLine($"Invite links will use {reachable}, reachable from other devices on this network.");
    }
}

builder.Services.AddSplitEverythingInfrastructure(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as their names, so a client reading the API does not have to
        // track integer values that shift when the enum grows.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUserAccessor>();
builder.Services.AddScoped<ISyncBroadcaster, SignalRSyncBroadcaster>();

builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

var authOptions = new AuthOptions();
builder.Configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);

/*
 * A signing key shorter than 256 bits cannot sign an HS256 token, and this used to
 * pad one out with dots to make it fit. That took a weak key and made it look
 * accepted: sixteen characters of secret and sixteen of full stops. Refusing to
 * start is the only honest answer, and it fails on the deploy rather than quietly
 * signing every token in the app with a guessable key.
 */
if (Encoding.UTF8.GetByteCount(authOptions.JwtSigningKey) < 32)
{
    throw new InvalidOperationException(
        "Auth:JwtSigningKey must be at least 32 bytes. Generate one with: openssl rand -base64 48");
}


builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = authOptions.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.JwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // The browser cannot set headers on a WebSocket handshake, so the
                // SignalR client passes the token as a query parameter instead.
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(token)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

/*
 * Who may call this API from a browser.
 *
 * Configured, and only configured, in production: a deployment behind one hostname
 * makes no cross-origin requests at all, and the list used to fall back to
 * localhost with credentials allowed, which let anything served from a dev port on
 * a user's own machine talk to the live API as them.
 *
 * Development adds what a phone on the same wifi uses to reach the dev server,
 * since that address is not knowable when the file is written. The native shells
 * are always allowed: their origin is fixed by the platform, not by a network.
 */
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var corsOrigins = new List<string>(allowedOrigins) { "capacitor://localhost", "ionic://localhost" };

if (builder.Environment.IsDevelopment())
{
    corsOrigins.Add("http://localhost:5173");
    corsOrigins.Add("http://localhost:4173");

    var lan = LocalNetworkAddress.Detect();
    if (!string.IsNullOrWhiteSpace(lan))
    {
        corsOrigins.Add($"http://{lan}:5173");
        corsOrigins.Add($"http://{lan}:4173");
    }
}

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(corsOrigins.Distinct().ToArray())
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

/*
 * How often one caller may knock.
 *
 * This app is reachable from the internet, and the endpoints worth hammering are
 * few and obvious: signing in, refreshing a token, redeeming an invite. They get a
 * tight window of their own. Everything else gets a loose one, generous enough that
 * a phone coming back online and draining a full outbox never meets it.
 *
 * Keyed by address, which is the only thing available before a caller is anybody.
 * Behind a reverse proxy that means the proxy's forwarded address, so the header is
 * honoured below.
 */
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKeys.For(context),
        _ => new FixedWindowRateLimiterOptions
        {
            // Enough for a household's devices reconnecting at once, and nowhere
            // near enough to guess anything. It was twenty, which a phone and a
            // laptop coming back online together could reach between them.
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1)
        }));

    options.AddPolicy(RateLimitPolicies.Diagnostics, context => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKeys.For(context),
        _ => new FixedWindowRateLimiterOptions
        {
            // A crash loop should report itself once, not five hundred times.
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(5)
        }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            RateLimitKeys.For(context),
            _ => new FixedWindowRateLimiterOptions
            {
                /*
                 * Generous, because a client draining an outbox and pulling deltas
                 * is chatty by design and being refused is worse than being slow:
                 * the app reads a refusal as a failure and says it is offline.
                 */
                PermitLimit = 1200,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<RecurringExpenseWorker>();
builder.Services.AddHostedService<ExchangeRateWorker>();
builder.Services.AddHostedService<SyncLogCompactionWorker>();

/*
 * The caller's real address, when there is a proxy in front.
 *
 * Everything about this app is served behind one: Traefik in the deployment, the
 * dev server's own proxy while developing. Without this the address on every
 * request is the proxy's, which made the rate limit above count every user of the
 * app as one caller and refuse them all together.
 *
 * Only honoured from a proxy on a private network - the defaults plus the ranges
 * container networks live in - because the header is one any caller can write.
 * From the internet it is ignored, which is the point.
 */
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Named in full: both namespaces in play here declare an IPNetwork.
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

// Migrate on start: this is a single-instance homelab deployment, so the simple
// path is the right one.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
