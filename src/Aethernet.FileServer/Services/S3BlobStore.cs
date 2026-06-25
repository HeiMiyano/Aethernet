using Amazon.S3;
using Amazon.S3.Model;

namespace Aethernet.FileServer.Services;

public sealed class S3BlobStore : IBlobStore
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    public S3BlobStore(IAmazonS3 s3, IConfiguration cfg)
    {
        _s3 = s3;
        _bucket = cfg["Storage:Bucket"] ?? "aethernet-blobs";
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try { await _s3.GetObjectMetadataAsync(_bucket, key, ct); return true; }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { return false; }
    }

    public async Task<long> PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
    {
        var put = new PutObjectRequest
        {
            BucketName  = _bucket,
            Key         = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = contentType ?? "application/octet-stream",
            // Cloudflare R2 does NOT implement the AWS chunked streaming signature
            // ("STREAMING-AWS4-HMAC-SHA256-PAYLOAD") that the SDK reaches for whenever the
            // input stream's length is unknown. Force single-shot SigV4 with UNSIGNED-PAYLOAD,
            // which R2 accepts. Auth still happens via the request signature on headers; only
            // the body payload itself becomes unsigned. This is the upstream-recommended R2
            // workaround. See: https://developers.cloudflare.com/r2/api/s3/api/#implemented-object-level-operations
            DisablePayloadSigning = true,
        };
        var resp = await _s3.PutObjectAsync(put, ct);
        return resp.ContentLength;
    }

    public async Task<Stream> GetAsync(string key, long? offset, long? length, CancellationToken ct)
    {
        var get = new GetObjectRequest { BucketName = _bucket, Key = key };
        if (offset is not null)
            get.ByteRange = length is not null
                ? new ByteRange(offset.Value, offset.Value + length.Value - 1)
                : new ByteRange($"bytes={offset.Value}-");
        var resp = await _s3.GetObjectAsync(get, ct);
        return resp.ResponseStream;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
        => _s3.DeleteObjectAsync(_bucket, key, ct);
}
