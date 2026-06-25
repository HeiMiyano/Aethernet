using System.Text;
using Aethernet.Data;
using Aethernet.FileServer.Services;
using Amazon.S3;
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

var jwtKey = builder.Configuration["Jwt:SigningKey"]
             ?? throw new InvalidOperationException("Jwt:SigningKey is required");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer            = builder.Configuration["Jwt:Issuer"]   ?? "aethernet-auth",
            ValidAudience          = builder.Configuration["Jwt:Audience"] ?? "aethernet-hub",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey       = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        };
    });
builder.Services.AddAuthorization();

var storageMode = builder.Configuration["Storage:Mode"] ?? "s3";
if (storageMode.Equals("s3", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
        builder.Configuration["Storage:AccessKey"],
        builder.Configuration["Storage:SecretKey"],
        new AmazonS3Config
        {
            ServiceURL = builder.Configuration["Storage:Endpoint"],
            ForcePathStyle = true,
            AuthenticationRegion = builder.Configuration["Storage:Region"] ?? "us-east-1",
            // Cloudflare R2 doesn't implement the AWS chunked streaming signature, and recent
            // AWS SDK builds also try to attach a CRC32 checksum trailer that R2 rejects.
            // Forcing the signature version to "4" (vs "4a") keeps us on plain SigV4, and
            // we disable payload signing per-request in S3BlobStore.PutAsync.
            SignatureVersion = "4",
        }));
    builder.Services.AddSingleton<IBlobStore, S3BlobStore>();
}
else
{
    builder.Services.AddSingleton<IBlobStore, DiskBlobStore>();
}

builder.Services.AddScoped<IQuotaService, QuotaService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddHostedService<OrphanBlobJanitor>();

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
