namespace Dropbox.Api.Storage;

public class StorageOptions
{
    public required string ServiceUrl { get; set; }
    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
    public required string BucketName { get; set; }
    public int PresignedUrlExpiryMinutes { get; set; } = 15;

    // Workaround for a confirmed AWSSDK.S3 v4 quirk: GetPreSignedURLAsync
    // always returns an https:// URL, even with AmazonS3Config.UseHttp = true
    // and a http:// ServiceURL (verified by inspecting Config at runtime -
    // both were set correctly, the SDK's URL builder just ignores them for
    // presigned URLs specifically). SigV4 presigned URLs only sign the Host
    // header (X-Amz-SignedHeaders=host), not the scheme, so rewriting the
    // scheme here does not invalidate the signature - confirmed by a real
    // PUT succeeding against the rewritten URL.
    public string FixPresignedUrlScheme(string presignedUrl) =>
        ServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? presignedUrl.Replace("https://", "http://", StringComparison.OrdinalIgnoreCase)
            : presignedUrl;
}
