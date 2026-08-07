namespace Dropbox.Api.Data.Entities;

public class FileMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }

    // long, not int: the 50GB NFR would overflow int32 (max ~2.1GB).
    public long Size { get; set; }

    public string? MimeType { get; set; }
    public required Guid OwnerId { get; set; }
    public User? Owner { get; set; }
    public FileStatus Status { get; set; } = FileStatus.Uploading;
    public string? Fingerprint { get; set; }

    // Set only for chunked multipart uploads (Step 6) - S3/MinIO's opaque
    // handle identifying an in-progress multipart upload. Null for the
    // Step 4 small-file single-PUT flow.
    public string? UploadId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Chunk> Chunks { get; set; } = [];
    public List<SharedFile> SharedWith { get; set; } = [];
}
