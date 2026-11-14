namespace Api.Shared.Tenancy;

/// <summary>
/// The tenant (business) and user of the current unit of work. Scoped per request.
/// Read by the EF query filters and by <c>TenantTransactionInterceptor</c>, which copies
/// these values into the Postgres settings <c>app.business_id</c> / <c>app.user_id</c>
/// used by the RLS policies.
/// </summary>
public interface ITenantContext
{
    Guid? BusinessId { get; }
    Guid? UserId { get; }
    bool HasBusiness => BusinessId is not null;

    /// <summary>Returns the current business id or throws if there is none.</summary>
    Guid RequireBusinessId() =>
        BusinessId ?? throw new InvalidOperationException("No tenant (business) set for the current scope.");
}

public sealed class TenantContext : ITenantContext
{
    public Guid? BusinessId { get; private set; }
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Sets the tenant. Must be called BEFORE opening the database transaction: settings are
    /// applied to Postgres when the transaction starts.
    /// </summary>
    public void Set(Guid? businessId, Guid? userId)
    {
        BusinessId = businessId;
        UserId = userId;
    }
}
