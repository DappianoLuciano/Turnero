namespace Api.Shared.Persistence;

/// <summary>
/// Business-owned data. The table MUST have a <c>business_id</c> column, an RLS policy
/// (see <see cref="RlsMigrationExtensions.EnableTenantRls"/>) and an isolation test.
/// A global EF query filter restricts it to the current tenant.
/// </summary>
public interface ITenantEntity
{
    Guid BusinessId { get; }
}

/// <summary>The tenant itself (the <c>businesses</c> table): its <c>Id</c> is the tenant id.</summary>
public interface ITenantRoot
{
    Guid Id { get; }
}

/// <summary>
/// Explicit opt-out from tenant isolation (e.g. <c>users</c>, which can belong to several businesses).
/// Every global entity must be justified in an ADR. Global tables must NOT hold business data.
/// </summary>
public interface IGlobalEntity;
