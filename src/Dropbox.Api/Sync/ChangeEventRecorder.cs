using Dropbox.Api.Contracts;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Microsoft.AspNetCore.SignalR;

namespace Dropbox.Api.Sync;

// Two-step usage: Record() stages a row (no SaveChangesAsync of its own, so
// it commits atomically with whatever mutation triggered it); the caller
// calls PublishPendingAsync() only after its own SaveChangesAsync succeeds,
// so a client is never pushed a notification for a change that failed to
// actually persist.
public class ChangeEventRecorder(DropboxDbContext db, IHubContext<ChangesHub> hub)
{
    private readonly List<ChangeEvent> _pending = [];

    public void Record(Guid userId, Guid fileId, string fileName, ChangeType type)
    {
        var changeEvent = new ChangeEvent
        {
            UserId = userId,
            FileId = fileId,
            FileName = fileName,
            Type = type,
        };

        db.ChangeEvents.Add(changeEvent);
        _pending.Add(changeEvent);
    }

    public async Task PublishPendingAsync()
    {
        foreach (var changeEvent in _pending)
        {
            var summary = new ChangeEventSummary(changeEvent.FileId, changeEvent.FileName, changeEvent.Type, changeEvent.OccurredAt);
            await hub.Clients.Group(changeEvent.UserId.ToString()).SendAsync("ChangeOccurred", summary);
        }

        _pending.Clear();
    }
}
