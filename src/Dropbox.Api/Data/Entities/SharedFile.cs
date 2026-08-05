namespace Dropbox.Api.Data.Entities;

// Composite primary key (UserId, FileId) configured in DropboxDbContext.
public class SharedFile
{
    public required Guid UserId { get; set; }
    public User? User { get; set; }
    public required Guid FileId { get; set; }
    public FileMetadata? File { get; set; }
    public DateTimeOffset SharedAt { get; set; } = DateTimeOffset.UtcNow;
}
