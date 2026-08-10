using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;

namespace Dropbox.Api.Sync;

// Stages a ChangeEvent row - does not call SaveChangesAsync itself, so it
// commits atomically together with whatever mutation triggered it.
public class ChangeEventRecorder(DropboxDbContext db)
{
    public void Record(Guid userId, Guid fileId, string fileName, ChangeType type)
    {
        db.ChangeEvents.Add(new ChangeEvent
        {
            UserId = userId,
            FileId = fileId,
            FileName = fileName,
            Type = type,
        });
    }
}
