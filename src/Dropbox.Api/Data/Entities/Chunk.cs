namespace Dropbox.Api.Data.Entities;

public class Chunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid FileId { get; set; }
    public FileMetadata? File { get; set; }

    // Sequence position within the file - needed for reassembly/ordering.
    // Not in the reference spec's chunk fields directly, but required for
    // any usable multipart upload; added deliberately, not speculatively.
    public int Index { get; set; }

    public ChunkStatus Status { get; set; } = ChunkStatus.Pending;
    public string? ETag { get; set; }
}
