namespace Dropbox.Api.Contracts;

public record PresignedUrlRequest(string Name, long Size, string? MimeType);

public record PresignedUrlResponse(Guid FileId, string UploadUrl, DateTimeOffset ExpiresAt);

public record DownloadUrlResponse(string DownloadUrl, DateTimeOffset ExpiresAt, string Name, string? MimeType);

public record MultipartUploadRequest(string Name, long Size, string? MimeType, string Fingerprint, int ChunkCount);

public record PartUploadInfo(int PartNumber, string? Url, bool AlreadyUploaded);

public record MultipartUploadResponse(Guid FileId, string UploadId, List<PartUploadInfo> Parts);

public record ChunkUploadReport(string ETag);
