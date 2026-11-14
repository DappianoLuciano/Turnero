using Api.Modules.Businesses.Contracts;
using Api.Modules.Businesses.Domain;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Api.Shared.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Businesses.Features;

internal sealed class BusinessDirectory(AppDbContext db) : IBusinessDirectory
{
    public Task<BusinessSummary?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        db.Set<Business>()
            .AsNoTracking()
            .Select(b => new BusinessSummary(b.Id, b.Name, b.Slug, b.TimeZone, b.Currency))
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class BusinessProvisioning(AppDbContext db, TenantScope scope, TimeProvider clock)
    : IBusinessProvisioning
{
    public async Task<BusinessSummary> CreateAsync(string name, string slug, string? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        var business = Business.Create(name, slug, clock.GetUtcNow(), timeZone);

        // The new business is its own tenant: RLS WITH CHECK requires app.business_id = id.
        await scope.RunAsync(business.Id, null, async ct =>
        {
            db.Set<Business>().Add(business);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                throw AppException.Conflict("slug_taken", "The slug is already in use.");
            }
        }, cancellationToken);

        return new BusinessSummary(business.Id, business.Name, business.Slug, business.TimeZone, business.Currency);
    }
}
