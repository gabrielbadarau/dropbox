using Microsoft.EntityFrameworkCore;

namespace Dropbox.Api.Data;

// Entities (DbSets) are added in Step 2 Part 2. This skeleton exists first so
// we can wire up configuration, dependency injection, and a real health
// check against the database before there's any schema to argue about.
public class DropboxDbContext(DbContextOptions<DropboxDbContext> options) : DbContext(options)
{
}
