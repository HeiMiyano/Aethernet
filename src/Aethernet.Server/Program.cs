using System.Security.Claims;
using System.Text;
using Aethernet.API;
using Aethernet.Data;
using Aethernet.Server.Hubs;
using Aethernet.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- logging ----
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"));

// ---- database ----
builder.Services.AddDbContextPool<AethernetDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is required")));

// ---- redis (presence + signalr backplane) ----
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        _ => ConnectionMultiplexer.Connect(redisConn));
}

// ---- auth ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var jwtKey = builder.Configuration["Jwt:SigningKey"]
                     ?? throw new InvalidOperationException("Jwt:SigningKey is required");
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer        = true,
            ValidateAudience      = true,
            ValidateLifetime      = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer           = builder.Configuration["Jwt:Issuer"] ?? "aethernet-auth",
            ValidAudience         = builder.Configuration["Jwt:Audience"] ?? "aethernet-hub",
            IssuerSigningKey      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew             = TimeSpan.FromSeconds(30),
        };

        // SignalR passes the JWT in the `access_token` query string on the websocket upgrade.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                var path  = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments(AethernetConstants.HubPath))
                    ctx.Token = token!;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ---- signalR ----
var signalR = builder.Services.AddSignalR(o =>
{
    o.EnableDetailedErrors      = builder.Environment.IsDevelopment();
    o.MaximumReceiveMessageSize = AethernetConstants.MaxCharacterDataBytes;
    o.KeepAliveInterval         = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval     = TimeSpan.FromSeconds(60);
}).AddMessagePackProtocol();

if (!string.IsNullOrWhiteSpace(redisConn))
    signalR.AddStackExchangeRedis(redisConn, o => o.Configuration.ChannelPrefix = RedisChannel.Literal("aethernet"));

// ---- domain services ----
builder.Services.AddSingleton<IPresenceTracker, RedisPresenceTracker>();
builder.Services.AddScoped<IPairResolver, PairResolver>();
builder.Services.AddScoped<ICharacterDataDispatcher, CharacterDataDispatcher>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddSingleton<IRateLimiter, InMemoryRateLimiter>();
// SignalR maps to ClaimTypes.NameIdentifier. Built-in NameUserIdProvider is internal in
// recent ASP.NET Core builds, so we provide a one-liner equivalent.
builder.Services.AddSingleton<IUserIdProvider, ClaimsNameIdentifierUserIdProvider>();

// ---- metrics / ops ----
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AethernetDbContext>("postgres");

builder.Services.AddControllers();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks(Routes.Server.Health);
app.MapHealthChecks(Routes.Server.Ready);
app.MapMetrics(Routes.Server.Metrics);

app.MapGet(Routes.Server.ApiInfo, (IConfiguration cfg) => new
{
    protocolVersion = AethernetConstants.ProtocolVersion,
    build           = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
    motd            = cfg["Motd"],
});

app.MapControllers();

app.MapHub<AethernetHub>(AethernetConstants.HubPath, o =>
{
    o.ApplicationMaxBufferSize = AethernetConstants.MaxCharacterDataBytes;
    o.TransportMaxBufferSize   = AethernetConstants.MaxCharacterDataBytes;
});

app.Run();

// Exposed for integration tests.
public partial class Program;

/// <summary>
/// Tells SignalR to use the JWT's NameIdentifier claim (i.e. our UID) as the user id.
/// Replaces the now-internal Microsoft.AspNetCore.SignalR.Internal.NameUserIdProvider.
/// </summary>
internal sealed class ClaimsNameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
