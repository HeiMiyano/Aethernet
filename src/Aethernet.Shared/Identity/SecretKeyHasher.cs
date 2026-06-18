using System.Security.Cryptography;
using System.Text;

namespace Aethernet.Shared.Identity;

/// <summary>
/// One-way hash for storing a user's secret key in the database. We use PBKDF2-SHA256 with a
/// per-record random salt — the secret key has high entropy already so the work factor can be
/// moderate. (We don't use the existing ASP.NET Core Identity hasher because we don't want to
/// take a dependency on the rest of the Identity stack here.)
/// </summary>
public static class SecretKeyHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;

    public static string Hash(string secret)
    {
        Span<byte> salt = stackalloc byte[SaltBytes];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt.ToArray(),
            Iterations,
            HashAlgorithmName.SHA256,
            HashBytes);
        return $"v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string secret, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "v1") return false;
        if (!int.TryParse(parts[1], out var iter)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var computed = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            salt,
            iter,
            HashAlgorithmName.SHA256,
            expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, computed);
    }
}
