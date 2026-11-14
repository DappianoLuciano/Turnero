using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Shared.Persistence;

internal static class PersistenceServiceCollectionExtensions
{
    public const string AppConnectionName = "App";
    public const string MigrationsConnectionName = "Migrations";

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            // Resolved lazily so that tooling (build-time OpenAPI generation) does not need a database.
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(AppConnectionName)
                ?? throw new InvalidOperationException($"Missing connection string '{AppConnectionName}'.");
            DbContextOptionsFactory.Configure(options, connectionString);
        });

        return services;
    }
}
