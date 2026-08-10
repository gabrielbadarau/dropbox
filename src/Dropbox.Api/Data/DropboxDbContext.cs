using Dropbox.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dropbox.Api.Data;

public class DropboxDbContext(DbContextOptions<DropboxDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<FileMetadata> Files => Set<FileMetadata>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<SharedFile> SharedFiles => Set<SharedFile>();
    public DbSet<ChangeEvent> ChangeEvents => Set<ChangeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(u => u.Email).IsRequired().HasMaxLength(320); // RFC 5321 max
            entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255); // PasswordHasher<T> output, generously sized
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<FileMetadata>(entity =>
        {
            entity.Property(f => f.Name).IsRequired().HasMaxLength(255);
            entity.Property(f => f.MimeType).HasMaxLength(255);
            entity.Property(f => f.Fingerprint).HasMaxLength(128);
            entity.Property(f => f.UploadId).HasMaxLength(512);

            entity.HasIndex(f => f.Fingerprint); // resumability lookups: find an in-progress upload by fingerprint

            // Stored as text (e.g. "Uploading"), not the default int, so the
            // column is readable directly in psql without a lookup table.
            entity.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(f => f.Owner)
                .WithMany()
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(f => f.OwnerId);
        });

        modelBuilder.Entity<Chunk>(entity =>
        {
            entity.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(c => c.ETag).HasMaxLength(255);

            entity.HasOne(c => c.File)
                .WithMany(f => f.Chunks)
                .HasForeignKey(c => c.FileId)
                .OnDelete(DeleteBehavior.Cascade);

            // A file can't have two chunks claiming the same position.
            entity.HasIndex(c => new { c.FileId, c.Index }).IsUnique();
        });

        modelBuilder.Entity<SharedFile>(entity =>
        {
            entity.HasKey(s => new { s.UserId, s.FileId });

            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(s => s.File)
                .WithMany(f => f.SharedWith)
                .HasForeignKey(s => s.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChangeEvent>(entity =>
        {
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);

            // FileId is intentionally not configured as a relationship here -
            // no HasOne/HasForeignKey to FileMetadata. It stays a plain
            // column so a Deleted event can still be read after the file
            // row it refers to is gone.
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The polling query's access pattern: this user's events, newest first.
            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
        });
    }
}
