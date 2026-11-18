namespace Api.Modules.Customers.Contracts;

/// <summary>Public view of a customer for other modules.</summary>
public sealed record CustomerSummary(Guid Id, string Name, string? Phone, bool IsArchived);

/// <summary>Read access to the customers of the current tenant. Use this instead of querying <c>customers</c>.</summary>
public interface ICustomerDirectory
{
    /// <summary>The customers of the current business that exist among <paramref name="ids"/> (unknown ids are omitted).</summary>
    Task<IReadOnlyList<CustomerSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
