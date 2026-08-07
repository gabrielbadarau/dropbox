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
}
