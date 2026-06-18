using System.Security.Cryptography;
using System.Text;

namespace Aethernet.Shared.Identity;

/// <summary>
/// Generates the user-facing identifiers (UIDs) and group identifiers (GIDs) Aethernet uses.
///
/// Format: 14-character base32 string drawn from a clear-confusables alphabet (Crockford base32
/// minus I/L/O/U). Roughly 70 bits of entropy — collision-resistant for the address space we care
/// about (millions of accounts) and short enough for users to type in chat.
/// </summary>
public static class UidGenerator
{
    /// <summary>Crockford base32 minus I, L, O, U. Lowercase printed so it's easy to type.</summary>
    private const string Alphabet = "abcdefghjkmnpqrstvwxyz0123456789";

    public static string NewUid()  => Generate(prefix: "u-");
    public static string NewGid()  => Generate(prefix: "g-");

    /// <summary>Recovery secret returned to the user once at registration time — long, ugly, but typeable.</summary>
    public static string NewRecoverySecret() => Generate(prefix: "r-", payloadLength: 32);

    /// <summary>Secret key (the user's actual login credential, hashed at rest).</summary>
    public static string NewSecretKey() => Generate(prefix: "k-", payloadLength: 48);

    /// <summary>Random group password — 12 chars, rotates each time.</summary>
    public static string NewGroupPassword() => Generate(prefix: string.Empty, payloadLength: 12);

    private static string Generate(string prefix, int payloadLength = 14)
    {
        Span<byte> buf = stackalloc byte[payloadLength];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder(prefix.Length + payloadLength);
        sb.Append(prefix);
        for (int i = 0; i < payloadLength; i++)
            sb.Append(Alphabet[buf[i] % Alphabet.Length]);
        return sb.ToString();
    }
}
