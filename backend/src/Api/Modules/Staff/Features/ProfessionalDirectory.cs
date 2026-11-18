using Api.Modules.Staff.Contracts;
using Api.Modules.Staff.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Staff.Features;

internal sealed class ProfessionalDirectory(AppDbContext db) : IProfessionalDirectory
{
    public async Task<IReadOnlyList<ProfessionalSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var professionals = await db.Set<Professional>().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
        var found = professionals.Select(p => p.Id).ToList();
        var links = await db.Set<ProfessionalService>().AsNoTracking()
            .Where(l => found.Contains(l.ProfessionalId))
            .ToListAsync(cancellationToken);
        var byProfessional = links.ToLookup(l => l.ProfessionalId, l => l.ServiceId);

        return professionals
            .Select(p => new ProfessionalSummary(p.Id, p.Name, p.IsActive, byProfessional[p.Id].ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<ProfessionalSummary>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var ids = await db.Set<Professional>().AsNoTracking().Where(p => p.IsActive).Select(p => p.Id)
            .ToListAsync(cancellationToken);
        return (await GetByIdsAsync(ids, cancellationToken)).OrderBy(p => p.Name).ToList();
    }
}
