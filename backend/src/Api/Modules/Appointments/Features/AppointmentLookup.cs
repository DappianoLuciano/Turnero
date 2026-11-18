using Api.Modules.Appointments.Contracts;
using Api.Modules.Appointments.Domain;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Appointments.Features;

internal sealed class AppointmentLookup(AppDbContext db) : IAppointmentLookup
{
    public async Task<IReadOnlyList<AppointmentSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<Appointment>().AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new AppointmentSummary(
                a.Id, a.CustomerId, a.StartsAt, a.Price, a.Currency,
                a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Completed))
            .ToListAsync(cancellationToken);
    }
}
