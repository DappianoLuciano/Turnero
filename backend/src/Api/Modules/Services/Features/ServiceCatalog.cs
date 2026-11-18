using Api.Modules.Services.Contracts;
using Api.Modules.Services.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Services.Features;

internal sealed class ServiceCatalog(AppDbContext db) : IServiceCatalog
{
    public async Task<IReadOnlyList<ServiceSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<Service>().AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new ServiceSummary(s.Id, s.Name, s.DurationMinutes, s.Price, s.Currency, s.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ServiceSummary>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await db.Set<Service>().AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new ServiceSummary(s.Id, s.Name, s.DurationMinutes, s.Price, s.Currency, s.IsActive))
            .ToListAsync(cancellationToken);
}
