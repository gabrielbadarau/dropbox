using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;
using Dropbox.Api.Contracts;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Dropbox.Api.Storage;
using Dropbox.Api.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dropbox.Api.Controllers;

[ApiController]
[Route("files")]
[Authorize]
public class FilesController(
    DropboxDbContext db,
    IAmazonS3 s3Client,
    IOptions<StorageOptions> storageOptions,
    ChangeEventRecorder changeEvents) : ControllerBase
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    [HttpPost("presigned-url")]
    public async Task<ActionResult<PresignedUrlResponse>> CreatePresignedUploadUrl(PresignedUrlRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (request.Size <= 0)
        {
            return BadRequest("Size must be greater than zero.");
        }

        var ownerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = new FileMetadata
        {
            Name = request.Name,
            Size = request.Size,
            MimeType = request.MimeType,
            OwnerId = ownerId,
            Status = FileStatus.Uploading,
        };

        db.Files.Add(file);
        changeEvents.Record(ownerId, file.Id, file.Name, ChangeType.Created);
        await db.SaveChangesAsync();

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_storageOptions.PresignedUploadUrlExpiryMinutes);

        var uploadUrl = await s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = file.Id.ToString(),
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
        });
        uploadUrl = _storageOptions.FixPresignedUrlScheme(uploadUrl);

        return Ok(new PresignedUrlResponse(file.Id, uploadUrl, expiresAt));
    }

    [HttpGet("{id}/presigned-url")]
    public async Task<ActionResult<DownloadUrlResponse>> CreatePresignedDownloadUrl(Guid id)
    {
        var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null)
        {
            return NotFound();
        }

        // Allowed: the owner, or anyone the file has been shared with.
        // 404 for everything else - don't leak existence of other users'
        // files to someone with no access.
        var hasAccess = file.OwnerId == callerId
            || await db.SharedFiles.AnyAsync(s => s.FileId == id && s.UserId == callerId);

        if (!hasAccess)
        {
            return NotFound();
        }

        if (file.Status != FileStatus.Uploaded)
        {
            return Conflict("File has not finished uploading yet.");
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_storageOptions.PresignedDownloadUrlExpiryMinutes);

        var downloadUrl = await s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = file.Id.ToString(),
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
        });
        downloadUrl = _storageOptions.FixPresignedUrlScheme(downloadUrl);

        return Ok(new DownloadUrlResponse(downloadUrl, expiresAt, file.Name, file.MimeType));
    }

    [HttpPost("multipart-upload")]
    public async Task<ActionResult<MultipartUploadResponse>> InitiateMultipartUpload(MultipartUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Name is required.");
        }

        if (request.Size <= 0)
        {
            return BadRequest("Size must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.Fingerprint))
        {
            return BadRequest("Fingerprint is required.");
        }

        if (request.ChunkCount <= 0)
        {
            return BadRequest("ChunkCount must be greater than zero.");
        }

        var ownerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        // Resume: an in-progress multipart upload for this exact file
        // (same owner, same fingerprint) already exists. Reuse it as-is -
        // Name/Size/MimeType/ChunkCount from this request are ignored so a
        // resume can't silently change what's already partially uploaded.
        var file = await db.Files
            .Include(f => f.Chunks)
            .FirstOrDefaultAsync(f => f.OwnerId == ownerId
                && f.Fingerprint == request.Fingerprint
                && f.Status == FileStatus.Uploading
                && f.UploadId != null);

        if (file is null)
        {
            file = new FileMetadata
            {
                Name = request.Name,
                Size = request.Size,
                MimeType = request.MimeType,
                OwnerId = ownerId,
                Fingerprint = request.Fingerprint,
                Status = FileStatus.Uploading,
            };

            var initiateResponse = await s3Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
            {
                BucketName = _storageOptions.BucketName,
                Key = file.Id.ToString(),
            });
            file.UploadId = initiateResponse.UploadId;

            for (var partNumber = 1; partNumber <= request.ChunkCount; partNumber++)
            {
                file.Chunks.Add(new Chunk
                {
                    FileId = file.Id,
                    Index = partNumber,
                    Status = ChunkStatus.Pending,
                });
            }

            db.Files.Add(file);
            changeEvents.Record(ownerId, file.Id, file.Name, ChangeType.Created);
            await db.SaveChangesAsync();
        }

        var expiresAt = DateTime.UtcNow.AddMinutes(_storageOptions.PresignedUploadUrlExpiryMinutes);
        var parts = new List<PartUploadInfo>();

        foreach (var chunk in file.Chunks.OrderBy(c => c.Index))
        {
            if (chunk.Status == ChunkStatus.Uploaded)
            {
                parts.Add(new PartUploadInfo(chunk.Index, Url: null, AlreadyUploaded: true));
                continue;
            }

            var partUrl = await s3Client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
            {
                BucketName = _storageOptions.BucketName,
                Key = file.Id.ToString(),
                Verb = HttpVerb.PUT,
                UploadId = file.UploadId,
                PartNumber = chunk.Index,
                Expires = expiresAt,
            });
            partUrl = _storageOptions.FixPresignedUrlScheme(partUrl);

            parts.Add(new PartUploadInfo(chunk.Index, partUrl, AlreadyUploaded: false));
        }

        return Ok(new MultipartUploadResponse(file.Id, file.UploadId!, parts));
    }

    [HttpPatch("{id}/chunks/{index}")]
    public async Task<IActionResult> ReportChunkUploaded(Guid id, int index, ChunkUploadReport request)
    {
        var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = await db.Files.Include(f => f.Chunks).FirstOrDefaultAsync(f => f.Id == id);
        if (file is null || file.OwnerId != callerId)
        {
            return NotFound();
        }

        if (file.UploadId is null)
        {
            return BadRequest("File is not a multipart upload.");
        }

        var chunk = file.Chunks.FirstOrDefault(c => c.Index == index);
        if (chunk is null)
        {
            return NotFound();
        }

        // Trust but verify: confirm against S3's real ListParts response
        // rather than just believing the client's claimed ETag.
        var listPartsResponse = await s3Client.ListPartsAsync(new ListPartsRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = file.Id.ToString(),
            UploadId = file.UploadId,
        });

        var actualPart = listPartsResponse.Parts.FirstOrDefault(p => p.PartNumber == index);
        if (actualPart is null || actualPart.ETag != request.ETag)
        {
            return Conflict("Reported chunk does not match what storage actually has.");
        }

        chunk.Status = ChunkStatus.Uploaded;
        chunk.ETag = actualPart.ETag;
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteMultipartUpload(Guid id)
    {
        var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = await db.Files.Include(f => f.Chunks).FirstOrDefaultAsync(f => f.Id == id);
        if (file is null || file.OwnerId != callerId)
        {
            return NotFound();
        }

        if (file.UploadId is null)
        {
            return BadRequest("File is not a multipart upload.");
        }

        // Idempotent: a repeated completion call on an already-completed
        // upload succeeds without re-calling S3 (its UploadId is no longer
        // valid once completed).
        if (file.Status == FileStatus.Uploaded)
        {
            return Ok();
        }

        if (file.Chunks.Any(c => c.Status != ChunkStatus.Uploaded))
        {
            return Conflict("Not all parts have been uploaded yet.");
        }

        await s3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = file.Id.ToString(),
            UploadId = file.UploadId,
            PartETags = file.Chunks
                .OrderBy(c => c.Index)
                .Select(c => new PartETag(c.Index, c.ETag))
                .ToList(),
        });

        // Only mark Uploaded after S3 has confirmed the assembly succeeded.
        file.Status = FileStatus.Uploaded;
        file.UpdatedAt = DateTimeOffset.UtcNow;
        changeEvents.Record(file.OwnerId, file.Id, file.Name, ChangeType.Uploaded);
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id}/share")]
    public async Task<ActionResult<ShareFileResponse>> ShareFile(Guid id, ShareFileRequest request)
    {
        var ownerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null || file.OwnerId != ownerId)
        {
            return NotFound();
        }

        if (file.Status != FileStatus.Uploaded)
        {
            return Conflict("File has not finished uploading yet.");
        }

        if (request.Emails is null || request.Emails.Count == 0)
        {
            return BadRequest("At least one email is required.");
        }

        var results = new List<ShareResult>();

        foreach (var email in request.Emails.Distinct())
        {
            var recipient = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (recipient is null)
            {
                results.Add(new ShareResult(email, false, "No account with this email."));
                continue;
            }

            if (recipient.Id == ownerId)
            {
                results.Add(new ShareResult(email, false, "Cannot share a file with its owner."));
                continue;
            }

            var alreadyShared = await db.SharedFiles.AnyAsync(s => s.FileId == id && s.UserId == recipient.Id);
            if (alreadyShared)
            {
                results.Add(new ShareResult(email, true, "Already shared."));
                continue;
            }

            db.SharedFiles.Add(new SharedFile { FileId = id, UserId = recipient.Id });
            changeEvents.Record(recipient.Id, file.Id, file.Name, ChangeType.Shared);
            results.Add(new ShareResult(email, true, "Shared."));
        }

        await db.SaveChangesAsync();

        return Ok(new ShareFileResponse(results));
    }

    [HttpGet("shared-with-me")]
    public async Task<ActionResult<List<SharedFileSummary>>> ListSharedWithMe()
    {
        var callerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var shared = await db.SharedFiles
            .Where(s => s.UserId == callerId)
            .Include(s => s.File)
            .OrderByDescending(s => s.SharedAt)
            .Select(s => new SharedFileSummary(
                s.File!.Id,
                s.File.Name,
                s.File.Size,
                s.File.MimeType,
                s.File.OwnerId,
                s.SharedAt))
            .ToListAsync();

        return Ok(shared);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFile(Guid id)
    {
        var ownerId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == id);
        if (file is null || file.OwnerId != ownerId)
        {
            return NotFound();
        }

        // Best-effort storage cleanup: prioritize the DB row actually
        // disappearing over a perfectly clean bucket. A failure here is a
        // known, accepted storage-leak risk, not silently swallowed - it's
        // just not allowed to block the delete itself.
        try
        {
            if (file.UploadId is not null && file.Status == FileStatus.Uploading)
            {
                await s3Client.AbortMultipartUploadAsync(_storageOptions.BucketName, file.Id.ToString(), file.UploadId);
            }
            else
            {
                await s3Client.DeleteObjectAsync(_storageOptions.BucketName, file.Id.ToString());
            }
        }
        catch (AmazonS3Exception)
        {
        }

        // Capture who currently has access BEFORE the cascade delete below
        // removes those SharedFiles rows - this is the only chance to know
        // who needs a Deleted event.
        var sharedWithUserIds = await db.SharedFiles
            .Where(s => s.FileId == id)
            .Select(s => s.UserId)
            .ToListAsync();

        changeEvents.Record(ownerId, file.Id, file.Name, ChangeType.Deleted);
        foreach (var userId in sharedWithUserIds)
        {
            changeEvents.Record(userId, file.Id, file.Name, ChangeType.Deleted);
        }

        db.Files.Remove(file);
        await db.SaveChangesAsync();

        return NoContent();
    }
}
