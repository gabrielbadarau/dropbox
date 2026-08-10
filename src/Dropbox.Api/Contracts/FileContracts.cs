using Dropbox.Api.Data.Entities;

namespace Dropbox.Api.Contracts;

public record PresignedUrlRequest(string Name, long Size, string? MimeType);

public record PresignedUrlResponse(Guid FileId, string UploadUrl, DateTimeOffset ExpiresAt);

public record DownloadUrlResponse(string DownloadUrl, DateTimeOffset ExpiresAt, string Name, string? MimeType);

public record MultipartUploadRequest(string Name, long Size, string? MimeType, string Fingerprint, int ChunkCount);

public record PartUploadInfo(int PartNumber, string? Url, bool AlreadyUploaded);

public record MultipartUploadResponse(Guid FileId, string UploadId, List<PartUploadInfo> Parts);

public record ChunkUploadReport(string ETag);

public record ShareFileRequest(List<string> Emails);

public record ShareResult(string Email, bool Success, string Reason);

public record ShareFileResponse(List<ShareResult> Results);

public record SharedFileSummary(Guid FileId, string Name, long Size, string? MimeType, Guid OwnerId, DateTimeOffset SharedAt);

public record ChangeEventSummary(Guid FileId, string FileName, ChangeType Type, DateTimeOffset OccurredAt);
