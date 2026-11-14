using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Shared.Persistence;

/// <summary>Applies EF migrations using the migrator role connection.</summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(string migrationsConnectionString, CancellationToken cancellationToken = default)
    {
        await using var db = new AppDbContext(
            DbContextOptionsFactory.Create(migrationsConnectionString), new TenantContext());
        await db.Database.MigrateAsync(cancellationToken);
    }
}
