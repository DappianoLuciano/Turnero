using Api.Shared.Hosting;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Shared.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c>. Migrations always run with the migrator role
/// (<c>ConnectionStrings__Migrations</c>), never with the runtime role.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT",
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development");
        DotEnv.LoadForDevelopment();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Migrations")
            ?? "Host=localhost;Database=turnero;Username=app_migrator";
        return new AppDbContext(DbContextOptionsFactory.Create(connectionString), new TenantContext());
    }
}
