using System.Security.Cryptography;
using System.Text;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.AuthService.Services;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly AethernetDbContext _db;
    private readonly IConfiguration _cfg;
    public RefreshTokenService(AethernetDbContext db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    public async Task<(string TokenId, string Plain)> IssueAsync(string uid, string? userAgent, string? ip)
    {
        var lifetime = TimeSpan.FromDays(int.Parse(_cfg["Jwt:RefreshLifetimeDays"] ?? "30"));
        var tokenId  = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var plain    = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                       .Replace('+','-').Replace('/','_').TrimEnd('=');
        var combined = $"{tokenId}.{plain}";
        var hash     = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combined)));

        _db.RefreshTokens.Add(new RefreshTokenEntity
        {
            TokenId   = tokenId,
            TokenHash = hash,
            Uid       = uid,
            IssuedAt  = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(lifetime),
            UserAgent = userAgent, IpAddress = ip,
        });
        await _db.SaveChangesAsync();
        return (tokenId, combined);
    }

    public async Task<string?> ValidateAndRotateAsync(string refreshToken, string? userAgent, string? ip)
    {
        var parts = refreshToken.Split('.', 2);
        if (parts.Length != 2) return null;
        var tokenId = parts[0];

        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenId == tokenId);
        if (row is null) return null;
        if (row.RevokedAt is not null) return null;
        if (row.ExpiresAt < DateTime.UtcNow) return null;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(row.TokenHash)))
            return null;

        // Rotate: revoke the current token, issue a new one.
        row.RevokedAt = DateTime.UtcNow;
        var (newId, newPlain) = await IssueAsync(row.Uid, userAgent, ip);
        row.ReplacedByTokenId = newId;
        await _db.SaveChangesAsync();
        return newPlain;
    }

    public async Task RevokeAllForUserAsync(string uid)
    {
        var rows = await _db.RefreshTokens.Where(t => t.Uid == uid && t.RevokedAt == null).ToListAsync();
        foreach (var r in rows) r.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
