using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aethernet.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without spinning up the full server.
/// Reads the connection string from the AETHERNET_DB env var, falling back to a localhost default.
/// </summary>
public sealed class AethernetDbContextFactory : IDesignTimeDbContextFactory<AethernetDbContext>
{
    public AethernetDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("AETHERNET_DB")
                   ?? "Host=localhost;Database=aethernet;Username=aethernet;Password=aethernet_dev_password";
        var options = new DbContextOptionsBuilder<AethernetDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsAssembly(typeof(AethernetDbContextFactory).Assembly.FullName))
            .Options;
        return new AethernetDbContext(options);
    }
}
