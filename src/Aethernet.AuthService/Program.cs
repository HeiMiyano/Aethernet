using System.Text;
using Aethernet.API;
using Aethernet.AuthService.Services;
using Aethernet.Data;
using AspNet.Security.OAuth.Discord;
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

// Only wire up Discord OAuth if it's actually configured. The OAuthOptions validator
// runs eagerly during AuthenticationMiddleware.InitializeAsync — registering the handler
// with empty credentials crashes every request, not just OAuth requests.
var discordClientId = builder.Configuration["Discord:ClientId"];
var discordSecret   = builder.Configuration["Discord:ClientSecret"];
if (!string.IsNullOrWhiteSpace(discordClientId) && !string.IsNullOrWhiteSpace(discordSecret))
{
    authBuilder
        .AddCookie() // needed by the Discord OAuth handler for state correlation
        .AddDiscord(o =>
        {
            o.ClientId     = discordClientId;
            o.ClientSecret = discordSecret;
            o.Scope.Add("identify");
        });
}
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddHealthChecks().AddDbContextCheck<AethernetDbContext>("postgres");

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/healthz");
app.MapMetrics("/metrics");
app.MapControllers();

app.Run();

public partial class Program;
