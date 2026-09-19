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
using SplitEverything.Infrastructure.Notifications;
using SplitEverything.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

if (!builder.Environment.IsDevelopment())
{
    builder.Configuration["Auth:AllowDevelopmentSignIn"] = "false";
}
else
{
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

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKeys.For(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1)
        }));

    options.AddPolicy(RateLimitPolicies.Diagnostics, context => RateLimitPartition.GetFixedWindowLimiter(
        RateLimitKeys.For(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(5)
        }));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
        context => RateLimitPartition.GetFixedWindowLimiter(
            RateLimitKeys.For(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1200,
                Window = TimeSpan.FromMinutes(1)
            }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<RecurringExpenseWorker>();
builder.Services.AddHostedService<ExchangeRateWorker>();
builder.Services.AddHostedService<SyncLogCompactionWorker>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
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

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

var push = app.Services.GetRequiredService<PushOptions>();
var pushProblems = new List<string>();

if (string.IsNullOrWhiteSpace(push.VapidPublicKey)) pushProblems.Add("Push:VapidPublicKey is empty");
else if (!VapidKey.IsValidPublicKey(push.VapidPublicKey))
    pushProblems.Add("Push:VapidPublicKey is not a base64url P-256 public key (65 bytes, starts with B)");

if (string.IsNullOrWhiteSpace(push.VapidPrivateKey)) pushProblems.Add("Push:VapidPrivateKey is empty");
else if (!VapidKey.IsValidPrivateKey(push.VapidPrivateKey))
    pushProblems.Add("Push:VapidPrivateKey is not a base64url 32-byte key");

if (!VapidKey.IsValidSubject(push.VapidSubject))
    pushProblems.Add("Push:VapidSubject is not a mailto: or https: contact");

if (pushProblems.Count > 0)
{
    app.Logger.LogWarning(
        "Web Push is not usable: {Problems}. Notifications cannot be turned on until this is fixed. "
        + "Generate a pair with node infra/vapid/generate.mjs",
        string.Join("; ", pushProblems));
}

app.Run();
