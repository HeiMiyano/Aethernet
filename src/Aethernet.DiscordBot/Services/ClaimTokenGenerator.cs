using System.Security.Cryptography;
using System.Text;

namespace Aethernet.DiscordBot.Services;

/// <summary>
/// Short, human-typeable one-time tokens for <c>/recover</c> + <c>/claim</c>. Deliberately
/// separate from <c>Aethernet.Shared.Identity.UidGenerator</c> — these are meant to be read off
/// a phone screen and typed back into a slash-command parameter, so they're shorter and use an
/// unambiguous uppercase alphabet. Hashed with SHA-256 before storage, same as
/// <c>RefreshTokenEntity</c> — the plaintext is never persisted.
/// </summary>
public static class ClaimTokenGenerator
{
    // No 0/O, 1/I/L — avoids the classic "is that a zero or an O" DM-transcription error.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    public static string NewToken(int length = 8)
    {
        Span<byte> buf = stackalloc byte[length];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(length);
        foreach (var b in buf) sb.Append(Alphabet[b % Alphabet.Length]);
        return sb.ToString();
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim().ToUpperInvariant())));
}
