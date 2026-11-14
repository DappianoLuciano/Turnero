using System.Linq.Expressions;
using Api.Shared.Monetary;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Shared.Persistence;

/// <summary>
/// Single DbContext for the modular monolith. It has no DbSets: each module maps its own
/// entities (IEntityTypeConfiguration inside the module) and accesses them via <c>Set&lt;T&gt;()</c>.
/// Modules must not touch entities of other modules (enforced by architecture tests).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>
    /// Current tenant used by the global query filters. <see cref="Guid.Empty"/> when there is
    /// no tenant, which matches no row (fail closed).
    /// </summary>
    public Guid CurrentBusinessId => tenant.BusinessId ?? Guid.Empty;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money and any other decimal: numeric(12,2). Never float/double.
        configurationBuilder.Properties<decimal>().HavePrecision(Money.Precision, Money.Scale);
        configurationBuilder.Properties<string>().AreUnicode();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Needed later for exclusion constraints that prevent overlapping appointments.
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentBusinessId));
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clr = entityType.ClrType;
            string? column = null;
            if (typeof(ITenantEntity).IsAssignableFrom(clr))
            {
                column = nameof(ITenantEntity.BusinessId);
            }
            else if (typeof(ITenantRoot).IsAssignableFrom(clr))
            {
                column = nameof(ITenantRoot.Id);
            }

            if (column is null || entityType.IsOwned())
            {
                continue;
            }

            var parameter = Expression.Parameter(clr, "e");
            var body = Expression.Equal(Expression.Property(parameter, column), current);
            modelBuilder.Entity(clr).HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
