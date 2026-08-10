using Dropbox.Api.Contracts;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Dropbox.Api.Storage;
using Dropbox.Api.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dropbox.Api.Controllers;

// Not [Authorize] - MinIO does not present a JWT here, it presents its own
// configured webhook auth token, checked manually below.
[ApiController]
[Route("webhooks")]
public class StorageWebhooksController(
    DropboxDbContext db,
    IOptions<StorageOptions> storageOptions,
    ChangeEventRecorder changeEvents) : ControllerBase
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    [HttpPost("storage")]
    public async Task<IActionResult> HandleStorageEvent([FromBody] MinioWebhookPayload payload)
    {
        // MinIO sends its configured webhook auth token as "Bearer <token>",
        // confirmed by inspecting the actual header value at runtime.
        var expectedHeader = $"Bearer {_storageOptions.WebhookSecret}";
        if (Request.Headers.Authorization.ToString() != expectedHeader)
        {
            return Unauthorized();
        }

        foreach (var record in payload.Records)
        {
            if (!record.EventName.Contains("ObjectCreated"))
            {
                continue;
            }

            if (!Guid.TryParse(record.S3.Object.Key, out var fileId))
            {
                continue;
            }

            var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId);
            if (file is not null && file.Status == FileStatus.Uploading)
            {
                file.Status = FileStatus.Uploaded;
                file.UpdatedAt = DateTimeOffset.UtcNow;
                changeEvents.Record(file.OwnerId, file.Id, file.Name, ChangeType.Uploaded);
            }
        }

        await db.SaveChangesAsync();
        await changeEvents.PublishPendingAsync();
        return Ok();
    }
}
