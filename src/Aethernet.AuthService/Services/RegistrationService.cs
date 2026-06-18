using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Shared.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.AuthService.Services;

public sealed class RegistrationService : IRegistrationService
{
    private readonly AethernetDbContext _db;
    public RegistrationService(AethernetDbContext db) { _db = db; }

    public async Task<RegisterResponseDto> RegisterAsync(RegisterRequestDto request, string? discordUserId)
    {
        // If they're authenticating via Discord and an account already exists, hand back a new
        // secret key for it instead of creating a duplicate.
        if (!string.IsNullOrEmpty(discordUserId))
        {
            var existing = await _db.Users.FirstOrDefaultAsync(u => u.DiscordUserId == discordUserId);
            if (existing is not null) return await RotateSecretAsync(existing.Uid);
        }

        var uid = UidGenerator.NewUid();
        var key = UidGenerator.NewSecretKey();
        var recovery = string.IsNullOrEmpty(request.RecoverySecret)
            ? UidGenerator.NewRecoverySecret() : request.RecoverySecret;

        _db.Users.Add(new UserEntity
        {
            Uid               = uid,
            SecretKeyHash     = SecretKeyHasher.Hash(key),
            RecoverySecretHash= SecretKeyHasher.Hash(recovery),
            DiscordUserId     = discordUserId,
            CreatedAt         = DateTime.UtcNow,
            LastSeenAt        = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return new RegisterResponseDto(uid, key);
    }

    public async Task<RegisterResponseDto> RotateSecretAsync(string uid)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == uid)
                   ?? throw new InvalidOperationException("not_found");
        var key = UidGenerator.NewSecretKey();
        user.SecretKeyHash = SecretKeyHasher.Hash(key);
        await _db.SaveChangesAsync();
        return new RegisterResponseDto(uid, key);
    }

    public async Task<RegisterResponseDto> RecoverAsync(string recoverySecret)
    {
        // O(n) — for production replace with a lookup-table or a Bloom-filter style index.
        // In practice we expect recovery to be rare and admin-paged.
        var candidates = await _db.Users.AsNoTracking()
            .Where(u => u.RecoverySecretHash != null)
            .Select(u => new { u.Uid, u.RecoverySecretHash })
            .ToListAsync();
        var match = candidates.FirstOrDefault(c => SecretKeyHasher.Verify(recoverySecret, c.RecoverySecretHash!));
        if (match is null) throw new InvalidOperationException("not_found");
        return await RotateSecretAsync(match.Uid);
    }
}
