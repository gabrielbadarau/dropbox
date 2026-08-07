using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Amazon.S3;
using Amazon.S3.Model;
using Dropbox.Api.Contracts;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Dropbox.Api.Storage;
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
    IOptions<StorageOptions> storageOptions) : ControllerBase
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

        // 404 for both "doesn't exist" and "exists but isn't yours" - don't
        // leak existence of other users' files to a non-owner.
        if (file is null || file.OwnerId != callerId)
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
}
