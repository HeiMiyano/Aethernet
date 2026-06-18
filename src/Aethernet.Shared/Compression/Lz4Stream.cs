using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace Aethernet.Shared.Compression;

/// <summary>
/// LZ4 frame compression for HTTP bodies negotiated via Content-Encoding: lz4 /
/// Accept-Encoding: lz4. Self-describing format — decoder doesn't need the original size.
/// </summary>
public static class Lz4Stream
{
    public const string Encoding = "lz4";

    public static async Task<MemoryStream> CompressAsync(Stream source, CancellationToken ct = default)
    {
        var dest = new MemoryStream();
        await using (var enc = LZ4Stream.Encode(dest, new LZ4EncoderSettings
                     {
                         CompressionLevel = LZ4Level.L09_HC,
                         ChainBlocks      = true,
                         BlockSize        = 1 << 20,
                     }, leaveOpen: true))
        {
            await source.CopyToAsync(enc, ct);
        }
        dest.Position = 0;
        return dest;
    }

    public static Stream Decompress(Stream compressed, bool leaveOpen = false)
        => LZ4Stream.Decode(compressed, new LZ4DecoderSettings(), leaveOpen);
}
