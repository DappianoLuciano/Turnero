namespace Api.Modules.Businesses.Contracts;

/// <summary>Public view of a business for other modules.</summary>
public sealed record BusinessSummary(Guid Id, string Name, string Slug, string TimeZone, string Currency);

/// <summary>Read access to the current tenant's business. Use this instead of querying <c>businesses</c>.</summary>
public interface IBusinessDirectory
{
    /// <summary>The business of the current tenant scope, or null if there is none.</summary>
    Task<BusinessSummary?> GetCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates new tenants (signup, admin tools, dev seed).</summary>
public interface IBusinessProvisioning
{
    /// <summary>Creates a business in its own tenant transaction. Must be called with no open transaction.</summary>
    Task<BusinessSummary> CreateAsync(string name, string slug, string? timeZone = null,
        CancellationToken cancellationToken = default);
}
