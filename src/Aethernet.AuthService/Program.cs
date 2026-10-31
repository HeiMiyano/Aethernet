using System.Text;
using Aethernet.API;
using Aethernet.AuthService.Services;
using Aethernet.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.AddDbContextPool<AethernetDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is required")));

builder.Services.AddSingleton<IJwtIssuer, JwtIssuer>();
builder.Services.AddScoped<IRegistrationService, RegistrationService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

var jwtKey = builder.Configuration["Jwt:SigningKey"]
             ?? throw new InvalidOperationException("Jwt:SigningKey is required");

var authBuilder = builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer            = builder.Configuration["Jwt:Issuer"] ?? "aethernet-auth",
        ValidAudience          = builder.Configuration["Jwt:Audience"] ?? "aethernet-hub",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew              = TimeSpan.FromSeconds(30),
    };
});

// Discord is the only way to create an account (see DiscordAuthController). The OAuth flow is
// hand-rolled rather than using the ASP.NET Discord handler, because the browser that authorizes
// isn't the client that ends up holding the credentials — the plugin is.
builder.Services.Configure<DiscordOAuthOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.AddSingleton<DiscordSignInStore>();
builder.Services.AddHttpClient<DiscordOAuthClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/HeiMiyano/Aethernet, 1.0)");
});

builder.Services.AddAuthorization();

// The web moderation dashboard is served by Aethernet.Server (a different origin/subdomain in
// prod — see deploy/Caddyfile), but logs in against THIS service's /auth/login. Only enabled
// when an origin is actually configured; empty by default means no cross-origin access at all.
var corsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddPolicy("AdminDashboard", p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AethernetDbContext>("postgres");

var app = builder.Build();

var discordOpt = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DiscordOAuthOptions>>().Value;
if (!discordOpt.IsConfigured)
    app.Logger.LogWarning("Discord sign-in is NOT configured (Discord:ClientId / ClientSecret / RedirectUri) — nobody can create an account until it is.");
else
    app.Logger.LogInformation("Discord sign-in enabled. Redirect URI {RedirectUri}; required guild {Guild}; link required to log in: {RequireLink}",
        discordOpt.RedirectUri, string.IsNullOrWhiteSpace(discordOpt.RequiredGuildId) ? "(none)" : discordOpt.RequiredGuildId,
        discordOpt.RequireLinkedAccount);

app.UseSerilogRequestLogging();
app.UseCors("AdminDashboard");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz");
app.MapMetrics("/metrics");
app.MapControllers();

app.Run();

public partial class Program;
