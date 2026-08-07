using Amazon.S3;
using Amazon.S3.Util;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dropbox.Api.Storage;

public class S3HealthCheck(IAmazonS3 s3Client, IOptions<StorageOptions> options) : IHealthCheck
{
    private readonly StorageOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, _options.BucketName);
            return exists
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"Bucket '{_options.BucketName}' does not exist.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Could not reach object storage.", ex);
        }
    }
}
