namespace Dropbox.Api.Contracts;

public record PresignedUrlRequest(string Name, long Size, string? MimeType);

public record PresignedUrlResponse(Guid FileId, string UploadUrl, DateTimeOffset ExpiresAt);
