namespace Dropbox.Api.Storage;

public class StorageOptions
{
    // Used for the API's own direct S3 calls (InitiateMultipartUpload,
    // ListParts, CompleteMultipartUpload, bucket bootstrap, etc.) - these
    // are server-to-server, so the in-network hostname (e.g. "minio" in
    // Docker) is correct.
    public required string ServiceUrl { get; set; }

    // Used only when generating presigned URLs, which are handed to an
    // external client (a browser) that cannot resolve Docker's internal
    // service names. Equal to ServiceUrl when running via `dotnet run` on
    // the host (everything is already on localhost); overridden to a
    // host-reachable address when Dropbox.Api itself runs containerized.
    // A separate AmazonS3Client, not a string swap on the generated URL:
    // confirmed empirically that swapping the hostname alone breaks the
    // SigV4 signature (X-Amz-SignedHeaders=host signs the literal Host
    // header value, unlike the scheme - see FixPresignedUrlScheme).
    public required string PublicServiceUrl { get; set; }

    public required string AccessKey { get; set; }
    public required string SecretKey { get; set; }
    public required string BucketName { get; set; }
    public required string WebhookSecret { get; set; }
    public int PresignedUploadUrlExpiryMinutes { get; set; } = 15;

    // Shorter than the upload expiry: download URLs are meant to be
    // consumed immediately, and per the reference spec's security guidance,
    // presigned URLs are bearer tokens - anyone holding one can use it
    // before it expires, so shorter is safer here.
    public int PresignedDownloadUrlExpiryMinutes { get; set; } = 5;

    // Workaround for a confirmed AWSSDK.S3 v4 quirk: GetPreSignedURLAsync
    // always returns an https:// URL, even with AmazonS3Config.UseHttp = true
    // and a http:// ServiceURL (verified by inspecting Config at runtime -
    // both were set correctly, the SDK's URL builder just ignores them for
    // presigned URLs specifically). SigV4 presigned URLs only sign the Host
    // header (X-Amz-SignedHeaders=host), not the scheme, so rewriting the
    // scheme here does not invalidate the signature - confirmed by a real
    // PUT succeeding against the rewritten URL.
    public string FixPresignedUrlScheme(string presignedUrl) =>
        PublicServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? presignedUrl.Replace("https://", "http://", StringComparison.OrdinalIgnoreCase)
            : presignedUrl;
}
