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
