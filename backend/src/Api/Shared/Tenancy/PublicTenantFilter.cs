using System.Text.RegularExpressions;
using Api.Shared.Errors;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Shared.Tenancy;

/// <summary>
/// Unit of work for the PUBLIC booking endpoints (<c>/api/public/{slug}/…</c>, no login). The tenant comes from the
/// slug in the URL (ADR 0012):
/// <list type="number">
/// <item>In its own short transaction, sets <c>app.public_slug</c> and reads the business id. A narrow RLS policy on
/// <c>businesses</c> (SELECT only, only the row whose slug matches that setting) is what allows this lookup without a
/// tenant; nothing else is readable.</item>
/// <item>Sets the tenant and opens the request transaction, exactly like <see cref="TenantTransactionFilter"/>, so
/// every query below runs with the usual EF filters and RLS of that single business.</item>
/// </list>
/// An unknown slug is a 404, indistinguishable from any other missing business.
/// </summary>
internal sealed partial class PublicTenantFilter(AppDbContext db, TenantContext tenant) : IEndpointFilter
{
    public const string SlugRouteValue = "slug";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        var slug = context.HttpContext.GetRouteValue(SlugRouteValue) as string;
        var businessId = slug is not null && SlugShape().IsMatch(slug)
            ? await FindBusinessIdAsync(slug, cancellationToken)
            : null;
        if (businessId is null)
        {
            throw AppException.NotFound("business_not_found", "Business not found.");
        }

        tenant.Set(businessId, null);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await next(context);

        if (result is IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    private async Task<Guid?> FindBusinessIdAsync(string slug, CancellationToken cancellationToken)
    {
        tenant.Set(null, null);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT set_config({TenantSettings.PublicSlug}, {slug}, true)", cancellationToken);
        var ids = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM businesses WHERE slug = {slug}")
            .ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>Same shape as <c>Business.IsValidSlug</c>: reject anything else before touching the database.</summary>
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex SlugShape();
}
