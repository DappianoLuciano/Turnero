namespace Api.Modules.Services.Contracts;

/// <summary>Public view of a service for other modules.</summary>
public sealed record ServiceSummary(
    Guid Id, string Name, int DurationMinutes, decimal Price, string Currency, bool IsActive);

/// <summary>Read access to the catalog of the current tenant. Use this instead of querying <c>services</c>.</summary>
public interface IServiceCatalog
{
    /// <summary>The services of the current business that exist among <paramref name="ids"/> (unknown ids are omitted).</summary>
    Task<IReadOnlyList<ServiceSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>The services that can be booked (active), by name.</summary>
    Task<IReadOnlyList<ServiceSummary>> ListActiveAsync(CancellationToken cancellationToken = default);
}
