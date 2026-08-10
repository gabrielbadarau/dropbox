namespace Dropbox.Api.Data.Entities;

// Append-only event log for sync. One row per affected user, not per file
// mutation - a file shared with two people writes separate events for each.
public class ChangeEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Who this event is FOR - a real FK, cascades if the user is deleted.
    public required Guid UserId { get; set; }
    public User? User { get; set; }

    // Deliberately NOT a foreign key to FileMetadata: a Deleted event must
    // still be readable after the file row it refers to is gone. FileName
    // is a denormalized snapshot for the same reason - there is nowhere
    // left to join to once the file is deleted.
    public required Guid FileId { get; set; }
    public required string FileName { get; set; }

    public ChangeType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
