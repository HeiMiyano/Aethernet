using System.Buffers;
using System.Security.Cryptography;

namespace Aethernet.Shared.Hashing;

/// <summary>
/// SHA-1 helpers — used everywhere we hash mod-file bytes (Penumbra file replacements).
/// SHA-1 is fine here because we're not relying on it for security, only content-addressing —
/// two files with the same bytes get the same hash, which is the only property we need.
/// </summary>
public static class Sha1Helper
{
    private const int BufferSize = 81920;

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return HashStream(stream);
    }

    public static string HashBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(bytes, hash);
        return ToHex(hash);
    }

    public static string HashStream(Stream stream)
    {
        using var sha = SHA1.Create();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                sha.TransformBlock(buffer, 0, read, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return ToHex(sha.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<string> HashStreamAsync(Stream stream, CancellationToken ct = default)
    {
        using var sha = SHA1.Create();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                sha.TransformBlock(buffer, 0, read, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return ToHex(sha.Hash!);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[bytes.Length * 2];
        const string hex = "0123456789ABCDEF";
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i * 2]     = hex[bytes[i] >> 4];
            chars[i * 2 + 1] = hex[bytes[i] & 0xF];
        }
        return new string(chars);
    }

    /// <summary>Cheap non-cryptographic hash used for "is this character data different" checks.</summary>
    public static string Fnv1aHex(ReadOnlySpan<byte> bytes)
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime  = 1099511628211UL;
        ulong hash = fnvOffset;
        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= fnvPrime;
        }
        return hash.ToString("X16");
    }
}
