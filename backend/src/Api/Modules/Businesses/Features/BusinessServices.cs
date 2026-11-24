using Api.Modules.Businesses.Contracts;
using Api.Modules.Businesses.Domain;
using Api.Modules.Subscriptions.Contracts;
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
            .Select(b => new BusinessSummary(
                b.Id, b.Name, b.Slug, b.TimeZone, b.Currency, b.Phone, b.CustomerChangeNoticeHours))
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class BusinessProvisioning(
    AppDbContext db, TenantScope scope, TimeProvider clock, ISubscriptionProvisioning subscriptions)
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
            // Every business starts with a free trial, saved atomically with the business itself.
            subscriptions.StartTrial(business.Id);
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

internal sealed class BusinessLookup(AppDbContext db, TenantContext tenant) : IBusinessLookup
{
    public async Task<Guid?> FindIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (!Business.IsValidSlug(slug))
        {
            return null;
        }

        // Same narrow, SELECT-only RLS policy as the public booking site (ADR 0012): sees exactly one row.
        tenant.Set(null, null);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT set_config({TenantSettings.PublicSlug}, {slug}, true)", cancellationToken);
        var ids = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM businesses WHERE slug = {slug}")
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }
}
