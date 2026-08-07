using System.Text.Json.Serialization;

namespace Dropbox.Api.Contracts;

// Minimal shape of MinIO's S3-compatible event notification payload -
// only the fields we actually read.
public record MinioWebhookPayload(List<MinioWebhookRecord> Records);

public record MinioWebhookRecord(
    [property: JsonPropertyName("eventName")] string EventName,
    [property: JsonPropertyName("s3")] MinioWebhookS3 S3);

public record MinioWebhookS3(
    [property: JsonPropertyName("bucket")] MinioWebhookBucket Bucket,
    [property: JsonPropertyName("object")] MinioWebhookObject Object);

public record MinioWebhookBucket([property: JsonPropertyName("name")] string Name);

public record MinioWebhookObject([property: JsonPropertyName("key")] string Key);
