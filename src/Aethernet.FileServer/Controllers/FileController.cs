using System.Security.Claims;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.FileServer.Services;
using Aethernet.Shared.Compression;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Aethernet.FileServer.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("files")]
public sealed class FileController : ControllerBase
{
    private readonly IFileService _files;
    private readonly IQuotaService _quota;
    public FileController(IFileService files, IQuotaService quota) { _files = files; _quota = quota; }

    private string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? throw new UnauthorizedAccessException();
    private bool IsAdmin => User.IsInRole("admin");

    [HttpPost("has")]
    public async Task<ActionResult<HasFilesResponseDto>> Has([FromBody] HasFilesRequestDto body, CancellationToken ct)
        => Ok(await _files.HasAsync(body.Hashes, ct));

    [HttpPost("upload")]
    [RequestSizeLimit(AethernetConstants.MaxFileSize)]
    public async Task<ActionResult<FileUploadAckDto>> Upload(
        [FromForm] string hash, [FromForm] IFormFile file, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(hash) || file is null) return BadRequest("hash+file required");
        var isLz4 = file.Headers.ContentEncoding
            .Any(e => string.Equals(e, Lz4Stream.Encoding, StringComparison.OrdinalIgnoreCase));
        try
        {
            await using var stream = file.OpenReadStream();
            var ack = await _files.UploadAsync(Uid, hash, stream, file.ContentType, isLz4, ct);
            return Ok(ack);
        }
        catch (InvalidOperationException e) when (e.Message == "hash_mismatch")    { return BadRequest("hash_mismatch"); }
        catch (InvalidOperationException e) when (e.Message == "too_large")        { return StatusCode(413, "too_large"); }
        catch (InvalidOperationException e) when (e.Message == "quota_exceeded")   { return StatusCode(507, "quota_exceeded"); }
        // Surface storage-backend failures with a short, log-friendly reason instead of an
        // opaque 500 with no body. Without this catch any AWS/R2-side error (signing mismatch,
        // bucket missing, credentials expired) lands in the unhandled-exception path and
        // costs us a full server-log dive to identify. 502 = "we tried, our upstream broke".
        catch (Amazon.S3.AmazonS3Exception e)
        {
            return StatusCode(502, $"storage_error: {e.ErrorCode ?? "unknown"} — {e.Message}");
        }
    }

    [HttpGet("{hash}")]
    public async Task<IActionResult> Download(string hash, CancellationToken ct)
    {
        long? offset = null, length = null;
        if (Request.Headers.TryGetValue(HeaderNames.Range, out var rangeHdr) &&
            RangeHeaderValue.TryParse(rangeHdr.ToString(), out var rh) &&
            rh.Ranges.Count == 1)
        {
            var r = rh.Ranges.First();
            offset = r.From ?? 0;
            if (r.To is not null) length = (r.To.Value - offset.Value) + 1;
        }

        try
        {
            var (entry, stream) = await _files.DownloadAsync(hash, offset, length, ct);
            Response.Headers[HeaderNames.AcceptRanges] = "bytes";
            Response.Headers[HeaderNames.ETag] = $"\"{entry.Hash}\"";
            if (offset is not null)
            {
                Response.StatusCode = StatusCodes.Status206PartialContent;
                Response.Headers[HeaderNames.ContentRange] =
                    $"bytes {offset}-{(offset + (length ?? entry.SizeBytes - offset.Value) - 1)}/{entry.SizeBytes}";
            }

            var wantsLz4 = Request.Headers[HeaderNames.AcceptEncoding].ToString()
                .Split(',').Select(p => p.Split(';')[0].Trim())
                .Any(t => string.Equals(t, Lz4Stream.Encoding, StringComparison.OrdinalIgnoreCase));
            if (wantsLz4 && offset is null)
            {
                Response.Headers[HeaderNames.ContentEncoding] = Lz4Stream.Encoding;
                Response.Headers.Remove(HeaderNames.ContentLength);
                var compressed = await Lz4Stream.CompressAsync(stream, ct);
                return File(compressed, "application/octet-stream", enableRangeProcessing: false);
            }
            return File(stream, "application/octet-stream", enableRangeProcessing: false);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (InvalidOperationException e) when (e.Message == "forbidden") { return StatusCode(451); }
        // R2 GetObject returns its own AmazonS3Exception (NoSuchKey, AccessDenied, etc) when
        // the blob isn't there or perms drift; treat NoSuchKey as 404 (matches the DB-row-
        // missing branch above) and surface everything else as 502 with the code so the
        // client log shows WHY rather than just "500 Internal Server Error".
        catch (Amazon.S3.AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound
                                                 || e.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
        catch (Amazon.S3.AmazonS3Exception e)
        {
            return StatusCode(502, $"storage_error: {e.ErrorCode ?? "unknown"} — {e.Message}");
        }
    }

    [HttpDelete("{hash}")]
    public async Task<IActionResult> Delete(string hash, CancellationToken ct)
    {
        try { await _files.DeleteAsync(Uid, hash, IsAdmin, ct); return NoContent(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("quota")]
    public async Task<ActionResult<FileQuotaDto>> Quota(CancellationToken ct)
    {
        var (used, quota, files) = await _quota.GetForUserAsync(Uid, ct);
        return Ok(new FileQuotaDto(used, quota, files));
    }
}
