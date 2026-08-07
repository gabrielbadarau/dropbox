using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Dropbox.Api.Auth;
using Dropbox.Api.Data;
using Dropbox.Api.Data.Entities;
using Dropbox.Api.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<DropboxDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Ensure the storage bucket exists before serving any requests.
var s3Client = app.Services.GetRequiredService<IAmazonS3>();
var storageOptions = app.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3Client, storageOptions.BucketName))
{
    await s3Client.PutBucketAsync(storageOptions.BucketName);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
