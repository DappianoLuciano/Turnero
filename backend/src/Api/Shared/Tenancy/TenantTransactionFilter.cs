using Api.Shared.Errors;
using Api.Shared.Persistence;

namespace Api.Shared.Tenancy;

/// <summary>
/// Unit of work for tenant endpoints: opens one database transaction per request (which applies
/// the tenant settings for RLS), commits on success and rolls back on error results or exceptions.
/// Applied to the authenticated <c>/api</c> group, so handlers never manage transactions.
/// </summary>
internal sealed class TenantTransactionFilter(AppDbContext db, ITenantContext tenant) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!tenant.HasBusiness)
        {
            throw AppException.Forbidden("no_tenant", "The access token is not bound to a business.");
        }

        var cancellationToken = context.HttpContext.RequestAborted;
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
}
