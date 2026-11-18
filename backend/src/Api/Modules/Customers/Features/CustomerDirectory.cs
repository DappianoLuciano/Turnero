using Api.Modules.Customers.Contracts;
using Api.Modules.Customers.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Customers.Features;

internal sealed class CustomerDirectory(AppDbContext db) : ICustomerDirectory
{
    public async Task<IReadOnlyList<CustomerSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<Customer>().AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Phone, c.IsArchived))
            .ToListAsync(cancellationToken);
    }
}
