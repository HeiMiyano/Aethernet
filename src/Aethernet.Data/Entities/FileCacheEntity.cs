using System.ComponentModel.DataAnnotations;

namespace Aethernet.Data.Entities;

/// <summary>
/// One row per unique mod blob the file server has accepted. Content-addressed by SHA-1.
/// <see cref="ReferenceCount"/> lets us garbage-collect blobs no character data references anymore.
/// </summary>
public class FileCacheEntity
{
    [Key, MaxLength(40)]
    public string Hash { get; set; } = null!;   // SHA-1 hex (40 chars)

    public long SizeBytes { get; set; }

    /// <summary>Soft reference count — incremented when a CharacterDataDto references the hash, decremented when superseded.</summary>
    public long ReferenceCount { get; set; }

    /// <summary>UID of the first uploader (charged against quota).</summary>
    [Required, MaxLength(32)]
    public string FirstUploaderUid { get; set; } = null!;

    public DateTime UploadedAt { get; set; }
    public DateTime LastTouchedAt { get; set; }
    public DateTime? OrphanedAt { get; set; }

    /// <summary>Storage key — for MinIO/S3 this is the object key, for disk it's the relative path.</summary>
    [Required]
    public string StorageKey { get; set; } = null!;

    /// <summary>Tagged "forbidden" if a moderator decided this content can't be redistributed.</summary>
    public bool IsForbidden { get; set; }
    public string? ForbiddenReason { get; set; }
}
