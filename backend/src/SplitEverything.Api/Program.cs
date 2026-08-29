using System.Text;
using System.Text.Json.Serialization;
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
using SplitEverything.Infrastructure.Persistence.Seed;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

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
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(authOptions.JwtSigningKey.PadRight(32, '.'))),
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

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (allowedOrigins.Length == 0)
    {
        // Same-origin deployment behind Traefik needs no CORS at all; only a
        // configured origin list opens it up, and credentials require exact origins.
        policy.WithOrigins("http://localhost:5173", "capacitor://localhost", "http://localhost");
    }
    else
    {
        policy.WithOrigins(allowedOrigins);
    }

    policy.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<RecurringExpenseWorker>();
builder.Services.AddHostedService<ExchangeRateWorker>();
builder.Services.AddHostedService<SyncLogCompactionWorker>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SyncHub>("/hubs/sync");

// Migrate and seed on start: this is a single-instance homelab deployment, so the
// simple path is the right one, and the category seed has to run for a fresh volume.
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedRunner.RunAsync(db);
}

app.Run();
