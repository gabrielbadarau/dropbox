using System.Text;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Dropbox.Api.Auth;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Dropbox.Api.Storage;
using Dropbox.Api.Sync;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

const string changesHubPath = "/hubs/changes";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    // Enums serialize as their string name ("Created"), not the default
    // numeric value (0) - matches how they are already stored in Postgres
    // (HasConversion<string>) for the same readability reason.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<DropboxDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<ChangeEventRecorder>();
builder.Services.AddSignalR()
    // SignalR's JSON Hub Protocol has its own separate serializer options -
    // does not inherit the AddControllers().AddJsonOptions() converter
    // above. Without this, ChangeType pushed over the hub serializes as
    // its numeric value (0) while the same enum in a REST response
    // serializes as "Created" - confirmed by a real push arriving as
    // "type":0 before this was added.
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

builder.Services.AddSingleton<IAmazonS3>(sp =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    return new AmazonS3Client(options.AccessKey, options.SecretKey, new AmazonS3Config
    {
        ServiceURL = options.ServiceUrl,
        ForcePathStyle = true, // MinIO requires path-style addressing (endpoint/bucket/key), not virtual-hosted-style (bucket.endpoint/key)
        UseHttp = true, // ServiceURL's "http://" scheme alone isn't honored for presigned URL generation; MinIO here has no TLS configured
    });
});

// Second client, used only for presigned URL generation. Confirmed
// empirically (see StorageOptions.PublicServiceUrl) that a presigned URL
// built with one hostname cannot simply have its hostname swapped for
// another afterward - the signature breaks. When ServiceUrl and
// PublicServiceUrl are the same value (host-run dev), this is just a
// second client pointed at the same place.
builder.Services.AddKeyedSingleton<IAmazonS3>("public", (sp, _) =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    return new AmazonS3Client(options.AccessKey, options.SecretKey, new AmazonS3Config
    {
        ServiceURL = options.PublicServiceUrl,
        ForcePathStyle = true,
        UseHttp = true,
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DropboxDbContext>()
    .AddCheck<S3HealthCheck>("s3");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<PasswordHasher<User>>();
builder.Services.AddSingleton<JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, JwtSecurityTokenHandler silently remaps short claim
        // names ("sub", "email") to legacy long-form URIs (ClaimTypes.*) when
        // building the ClaimsPrincipal, so User.FindFirstValue("sub") would
        // find nothing even though the token clearly has a "sub" claim.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
        };

        // SignalR clients cannot always set a custom Authorization header on
        // the WebSocket handshake (browsers can't at all). The documented
        // ASP.NET Core pattern is to accept the token from the query string
        // instead, but only for requests actually hitting the hub - never
        // as a general fallback for ordinary API calls.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments(changesHubPath))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

// The React client is served from a different origin than the API
// (different port at minimum), so the browser enforces CORS on every
// call - including the SignalR hub's negotiate request. Presigned URLs
// point straight at MinIO, a third origin, which needs its own CORS
// config instead (bucket bootstrap, below).
const string corsPolicyName = "client";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicyName, policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// Ensure the storage bucket exists before serving any requests.
var s3Client = app.Services.GetRequiredService<IAmazonS3>();
var storageOptions = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, storageOptions.BucketName))
{
    await s3Client.PutBucketAsync(storageOptions.BucketName);
}

// Ensure the bucket is subscribed to ObjectCreated events on the "DROPBOX"
// webhook target configured on the MinIO server (docker-compose.yml's
// MINIO_NOTIFY_WEBHOOK_*_DROPBOX env vars). Idempotent, same pattern as the
// bucket bootstrap above - no manual "mc event add" step to remember.
const string webhookQueueArn = "arn:minio:sqs::DROPBOX:webhook";
var notificationConfig = await s3Client.GetBucketNotificationAsync(storageOptions.BucketName);
if (!notificationConfig.QueueConfigurations.Any(q => q.Queue == webhookQueueArn))
{
    notificationConfig.QueueConfigurations.Add(new QueueConfiguration
    {
        Id = "dropbox-upload-complete",
        Queue = webhookQueueArn,
        Events = [EventType.ObjectCreatedPut],
    });
    await s3Client.PutBucketNotificationAsync(new PutBucketNotificationRequest
    {
        BucketName = storageOptions.BucketName,
        QueueConfigurations = notificationConfig.QueueConfigurations,
    });
}

// No bucket CORS configuration needed for MinIO: confirmed empirically
// that it already answers preflight requests correctly for object-level
// operations (GET/PUT against a presigned URL) with zero configuration -
// reflects the Origin, echoes the requested method/headers. Attempting
// PutCORSConfigurationAsync here actually crashed startup: MinIO returned
// "NotImplemented" for that specific S3 API call (confirmed independently
// via `mc cors set` failing with the identical error), even though object
// requests were never blocked in the first place.

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(corsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<ChangesHub>(changesHubPath);

app.Run();
